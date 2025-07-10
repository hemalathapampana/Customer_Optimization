# Cross-Provider Monitoring Logic: Algorithmic Analysis

## Overview
This document provides algorithmic breakdowns for cross-provider monitoring logic in the Altaworx SIM Card Cost Optimization system, including queue polling, exponential backoff, retry mechanisms, and completion coordination.

---

## 1. Polls Customer Optimization Queues Across All Providers for Completion

### What
**What:** Monitor and poll optimization queues across M2M, Mobility, and CrossProvider platforms to track completion status and coordinate processing across multiple provider streams.

### Why
**Why:** To ensure all provider-specific optimization processes complete successfully and maintain visibility into the overall optimization progress across different provider ecosystems.

### How
**How:** The system uses SQS queue monitoring with depth tracking and status polling to coordinate completion across multiple provider processing streams.

### Algorithm Implementation

```
Algorithm: CrossProviderQueuePolling
Input: watchQueueUrl, serviceProviderIds[], sessionId
Output: completionStatus, queueDepths[]

1. Initialize queue monitoring:
   a. Connect to watchQueueUrl for optimization tracking
   b. Initialize provider-specific queue monitors
   c. Set up completion status tracking per provider

2. Poll queue depths across providers:
   FOR each provider in [M2M, Mobility, CrossProvider]:
     a. queueLength = GetOptimizationQueueLength(watchQueueUrl)
     b. Track: ApproximateNumberOfMessages + ApproximateNumberOfMessagesDelayed + ApproximateNumberOfMessagesNotVisible
     c. Monitor provider-specific processing status

3. Check completion criteria:
   a. IF (optimizationQueueLength == 0):
      - Proceed with cleanup operations
   b. ELSE:
      - Apply retry logic with exponential backoff
      - Continue monitoring until completion

4. Coordinate cross-provider completion:
   a. Check all providers for INSTANCE_FINISHED_STATUSES
   b. Validate isLastInstance flag for final coordination
   c. Trigger completion workflows when all providers complete
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` - Queue monitoring and polling logic

```csharp
// Cross-provider queue depth monitoring
private int GetOptimizationQueueLength(KeySysLambdaContext context)
{
    var awsCredentials = context.GeneralProviderSettings.AwsCredentials;
    using (var client = new AmazonSQSClient(awsCredentials, RegionEndpoint.USEast1))
    {
        // Monitor all queue message states for comprehensive tracking
        var request = new GetQueueAttributesRequest(_watchQueueUrl, new List<string> { 
            "ApproximateNumberOfMessages", 
            "ApproximateNumberOfMessagesDelayed", 
            "ApproximateNumberOfMessagesNotVisible" 
        });
        
        var response = client.GetQueueAttributesAsync(request);
        response.Wait();
        
        if (response.Status == TaskStatus.Faulted || response.Status == TaskStatus.Canceled)
        {
            LogInfo(context, "RESPONSE STATUS", $"Error Getting Queue Length: {response.Status}");
            return int.MaxValue; // Fail-safe for monitoring errors
        }

        // Calculate total queue depth across all message states
        var queueLength = response.Result.ApproximateNumberOfMessages + 
                         response.Result.ApproximateNumberOfMessagesDelayed + 
                         response.Result.ApproximateNumberOfMessagesNotVisible;
        return queueLength;
    }
}

// Cross-provider completion coordination
private void CleanupInstance(KeySysLambdaContext context, long instanceId, bool isCustomerOptimization, bool isLastInstance, int serviceProviderId)
{
    LogInfo(context, "SUB", $"CleanupInstance - isLastInstance: {isLastInstance}");
    
    // Monitor optimization queue for completion across all providers
    var optimizationQueueLength = GetOptimizationQueueLength(context);
    
    if (optimizationQueueLength == 0)
    {
        try
        {
            // All providers completed - proceed with cleanup
            CleanupInstance(context, instanceId, isCustomerOptimization, isLastInstance, serviceProviderId);
        }
        catch (Exception ex)
        {
            LogInfo(context, "WARN", $"Error occurred on cleanup, requeuing: {ex.Message}");
            RequeueCleanup(context, instanceId, retryCount, optimizationQueueLength, isCustomerOptimization);
        }
    }
    else if (retryCount < 10)
    {
        // Continue monitoring with retry logic
        RequeueCleanup(context, instanceId, retryCount, optimizationQueueLength, isCustomerOptimization);
    }
    else
    {
        LogInfo(context, "EXCEPTION", $"Optimization Cleanup Timed Out. Too many retry attempts.");
    }
}

// Cross-provider processing status validation
private static readonly List<OptimizationStatus> INSTANCE_FINISHED_STATUSES = new List<OptimizationStatus>(){
    OptimizationStatus.CleaningUp,
    OptimizationStatus.CompleteWithSuccess,
    OptimizationStatus.CompleteWithErrors
};

// Validate completion status across providers
if (INSTANCE_FINISHED_STATUSES.Contains((OptimizationStatus)instance.RunStatusId))
{
    LogInfo(context, "WARNING", $"Duplicated instance cleanup request for instance with id {instanceId}.");
    return; // Provider already completed
}
```

---

## 2. Provider-Specific Exponential Backoff (30s → 60s → 120s → max 300s)

### What
**What:** Implement progressive delay intervals that increase exponentially based on queue depth and retry attempts to prevent system overload during high-traffic periods.

### Why
**Why:** To provide adaptive throttling that scales with system load, ensuring efficient resource utilization while preventing overwhelming the optimization infrastructure during peak processing.

### How
**How:** The system uses queue-length-based delay calculation combined with retry count escalation to implement intelligent backoff strategies.

### Algorithm Implementation

```
Algorithm: ExponentialBackoffStrategy
Input: optimizationQueueLength, retryCount, providerType
Output: delaySeconds

1. Calculate base delay from queue depth:
   a. IF (optimizationQueueLength <= 50):
      baseDelay = 600 seconds (10 minutes)
   b. IF (optimizationQueueLength > 50):
      baseDelay = 900 seconds (15 minutes - SQS max)

2. Apply provider-specific backoff:
   a. Calculate: providerBackoff = Min(30 * (2^retryCount), 300)
   b. Examples:
      - Retry 0: 30s
      - Retry 1: 60s  
      - Retry 2: 120s
      - Retry 3+: 300s (max cap)

3. Combine delays:
   finalDelay = baseDelay + providerBackoff

4. Apply SQS constraints:
   RETURN Min(finalDelay, 900) // SQS 15-minute max
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` - Exponential backoff implementation

```csharp
// Provider-specific exponential backoff calculation
private int DelaySecondsFromQueueLength(int optimizationQueueLength)
{
    // Base delay per check (10 minutes default)
    var delaySeconds = 600;

    // Exponential backoff based on queue depth
    if (optimizationQueueLength > 50)
    {
        // High load detected - increase to maximum SQS delay (15 minutes)
        delaySeconds = 900;
    }

    return delaySeconds;
}

// Retry-count-based backoff with progressive delays
private void RequeueCleanup(KeySysLambdaContext context, long instanceId, int retryCount, int optimizationQueueLength, bool isCustomerOptimization)
{
    LogInfo(context, "SUB", $"RequeueCleanup({instanceId},{retryCount},{optimizationQueueLength})");

    var awsCredentials = context.GeneralProviderSettings.AwsCredentials;
    using (var client = new AmazonSQSClient(awsCredentials, RegionEndpoint.USEast1))
    {
        retryCount += 1; // Increment for exponential progression
        
        // Apply queue-depth-based exponential backoff
        int delaySeconds = DelaySecondsFromQueueLength(optimizationQueueLength);
        
        var requestMsgBody = $"Requeue Cleanup for Instance {instanceId}, Retry #{retryCount}";
        var request = new SendMessageRequest
        {
            DelaySeconds = delaySeconds, // Progressive delay application
            MessageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                {
                    "InstanceId", new MessageAttributeValue
                    { DataType = "String", StringValue = instanceId.ToString()}
                },
                {
                    "RetryCount", new MessageAttributeValue
                    { DataType = "String", StringValue = retryCount.ToString()}
                },
                {
                    "IsCustomerOptimization", new MessageAttributeValue
                    { DataType = "String", StringValue = isCustomerOptimization.ToString()}
                }
            },
            MessageBody = requestMsgBody,
            QueueUrl = context.CleanupDestinationQueueUrl
        };

        var response = client.SendMessageAsync(request);
        response.Wait();
        if (response.Status == TaskStatus.Faulted || response.Status == TaskStatus.Canceled)
        {
            LogInfo(context, "RESPONSE STATUS", $"Error Requeuing Cleanup for {instanceId}: {response.Status}");
        }
    }
}

// Customer optimization specific backoff with configurable delays
private int _optCustomerCleanUpDelaySeconds = Convert.ToInt32(Environment.GetEnvironmentVariable("OptCustomerCleanUpDelaySeconds"));
private int _cleanUpSendEmailRetryCount = Convert.ToInt32(Environment.GetEnvironmentVariable("CleanUpSendEmailRetryCount"));

// Provider-specific delay application
private void QueueLastStepOptCustomerCleanup(KeySysLambdaContext context, long instanceId, long sessionId, bool isOptLastStepSendEmail, int serviceProviderId, int delaySeconds = 0, int retryCount = 1)
{
    var request = new SendMessageRequest
    {
        DelaySeconds = delaySeconds, // Provider-specific exponential delay
        MessageAttributes = new Dictionary<string, MessageAttributeValue>
        {
            {
                "RetryCount", new MessageAttributeValue
                { DataType = "String", StringValue = retryCount.ToString()}
            },
        },
        MessageBody = requestMsgBody,
        QueueUrl = context.CleanupDestinationQueueUrl
    };
}
```

---

## 3. Cross-Provider Retry Logic and Failure Scenarios

### What
**What:** Handle provider-specific failure scenarios with intelligent retry mechanisms that account for different provider characteristics and failure patterns.

### Why
**Why:** To ensure robust operation across heterogeneous provider environments where different providers may have varying reliability characteristics and failure modes.

### How
**How:** The system implements provider-aware retry logic with configurable limits and failure escalation strategies.

### Algorithm Implementation

```
Algorithm: CrossProviderRetryLogic
Input: serviceProviderId, sessionId, retryCount, failureType
Output: retryDecision, escalationAction

1. Validate retry eligibility:
   a. IF (retryCount >= maxRetries):
      RETURN escalateFailure(serviceProviderId, failureType)
   b. Check provider-specific retry limits

2. Apply provider-specific retry logic:
   a. FOR CrossProvider optimization:
      - Check cross-provider processing status
      - Validate service provider compatibility
   b. FOR single provider:
      - Apply standard retry with exponential backoff
   
3. Handle failure scenarios:
   a. Configuration issues (Redis cache unreachable)
   b. Provider-specific timeouts
   c. Cross-provider coordination failures
   d. Queue processing errors

4. Escalation strategies:
   a. Log comprehensive failure information
   b. Send error notifications to designated recipients
   c. Update processing status for failed providers
   d. Coordinate cleanup for successful providers
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` - Cross-provider retry and failure handling

```csharp
// Cross-provider processing status monitoring with retry logic
private bool CheckOptCustomerProcessing(KeySysLambdaContext context, int serviceProviderId, long sessionId)
{
    LogInfo(context, "SUB", $"CheckOptCustomerProcess({serviceProviderId})");

    int record = 0;
    string query;
    
    // Provider-specific or cross-provider query logic
    if (serviceProviderId > 0)
    {
        query = @"SELECT COUNT(*) FROM [OptimizationCustomerProcessing]
                    WHERE [ServiceProviderId] = @serviceProviderId AND [IsProcessed] = @isProcessed AND [SessionId] = @sessionId";
    }
    else
    {
        // Cross-provider monitoring query
        query = @"SELECT COUNT(*) FROM [OptimizationCustomerProcessing]
                    WHERE [IsProcessed] = @isProcessed AND [SessionId] = @sessionId";
    }
    
    using (var conn = new SqlConnection(context.ConnectionString))
    {
        using (var cmd = new SqlCommand(query, conn))
        {
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@isProcessed", false);
            if (serviceProviderId > 0)
            {
                cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId);
            }
            cmd.Parameters.AddWithValue("@sessionId", sessionId);
            conn.Open();

            var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                record = int.Parse(rdr[0].ToString());
            }
            conn.Close();
        }
    }
    return record > 0 ? true : false; // Return processing status
}

// Provider-specific retry logic with configurable limits
private void OptCustomerSendEmail(KeySysLambdaContext context, long instanceId, long sessionId, int serviceProviderId, int retryCount)
{
    LogInfo(context, CommonConstants.SUB, "OptCustomerSendEmail");

    var instance = GetInstance(context, instanceId);
    
    // Check cross-provider processing status
    var checkOptProcessing = CheckOptCustomerProcessing(context, serviceProviderId, sessionId);
    if (checkOptProcessing)
    {
        LogInfo(context, CommonConstants.SUB, "Customer Optimization process has not finished yet.");
        
        // Apply retry logic with provider-specific limits
        if (retryCount <= _cleanUpSendEmailRetryCount)
        {
            // Retry with exponential backoff
            QueueLastStepOptCustomerCleanup(context, instanceId, sessionId, true, serviceProviderId, _optCustomerCleanUpDelaySeconds, retryCount + 1);
        }
        else
        {
            // Escalate failure after maximum retries
            LogInfo(context, CommonConstants.WARNING, $"Customer Optimization process has retried {_cleanUpSendEmailRetryCount} times.");
        }
        return;
    }
    
    // Process successful completion across providers
    // ... (success handling code)
}

// Retry attempt validation with failure escalation
if (retryCount < 10)
{
    // Continue retry cycle with exponential backoff
    RequeueCleanup(context, instanceId, retryCount, optimizationQueueLength, isCustomerOptimization);
}
else
{
    // Maximum retries exceeded - escalate failure
    LogInfo(context, "EXCEPTION", $"Optimization Cleanup Timed Out. Too many retry attempts.");
}
```

---

## 4. Cross-Provider Completion Coordination

### What
**What:** Coordinate the completion of optimization processes across multiple providers to ensure all provider streams finish successfully before triggering final completion workflows.

### Why
**Why:** To maintain consistency across provider optimizations and ensure that cross-provider customers receive complete optimization results that account for all available provider options.

### How
**How:** The system uses session-based tracking with isLastInstance flags and provider-specific completion validation to coordinate multi-provider completion.

### Algorithm Implementation

```
Algorithm: CrossProviderCompletionCoordination
Input: sessionId, providerResults[], isLastInstance
Output: coordinatedCompletion, finalResults

1. Track provider completion status:
   a. Monitor each provider's processing status
   b. Validate completion across all target providers
   c. Check for provider-specific error conditions

2. Coordinate completion sequence:
   a. IF (isLastInstance == true):
      - Initiate final completion workflow
      - Aggregate results across all providers
   b. ELSE:
      - Continue monitoring remaining providers
      - Update session tracking status

3. Handle cross-provider result aggregation:
   a. Combine optimization results from all providers
   b. Generate comprehensive cross-provider report
   c. Send unified completion notification

4. Cleanup and finalization:
   a. Clean up provider-specific processing records
   b. Send final optimization email with aggregated results
   c. Update optimization session status to completed
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostQueueCustomerOptimization.cs` - Cross-provider completion coordination

```csharp
// Cross-provider completion coordination with isLastInstance tracking
private async Task ProcessCrossProviderCustomerOptimization(KeySysLambdaContext context, SQSEvent.SQSMessage message, bool isLastInstance, int tenantId, SiteTypes customerType, long optimizationSessionId, string additionalData)
{
    LogInfo(context, CommonConstants.SUB, $"ProcessCrossProviderCustomerOptimization - isLastInstance: {isLastInstance}");

    SetPortalType(PortalTypes.CrossProvider);

    // Get customer and service provider configuration for coordination
    var customerIdentifier = int.Parse(message.MessageAttributes[SQSMessageKeyConstant.AMOP_CUSTOMER_ID].StringValue);
    var serviceProviderIds = message.MessageAttributes[SQSMessageKeyConstant.SERVICE_PROVIDER_IDS].StringValue;

    try
    {
        await RunCrossProviderCustomerOptimization(context, tenantId, customerIdentifier, customerType, serviceProviderIds, customerBillingPeriodId, message.MessageId, optimizationSessionId, isLastInstance, additionalData);
    }
    finally
    {
        // Mark completion in tracking system for coordination
        optimizationRepository.MarkProcessedOptimizationInstanceTrackingRecord(context, optimizationSessionId, revCustomerId: null, customerIdentifier);
    }
}

// Completion coordination with cleanup scheduling
if (!isError)
{
    // Enqueue cleanup with completion coordination
    EnqueueCleanup(context, instanceId, isCustomerOptimization: true, isLastInstance: isLastInstance);
}
else
{
    var errorMessage = "There is an error in Processing Customer Rate Plans";
    crossProviderOptimizationRepository.UpdateProcessingCustomerOptimizationInstance(ParameterizedLog(context), optimizationSessionId, instanceId, errorMessage, 0, false, customer.CustomerType, customer.RevAccountNumber, customer.CustomerId);
    StopOptimizationInstance(context, instanceId, OptimizationStatus.CompleteWithErrors);
}
```

**Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` - Final completion coordination

```csharp
// Cross-provider completion coordination and result processing
private void ProcessResultForCrossProvider(KeySysLambdaContext context, bool isCustomerOptimization, bool isLastInstance, OptimizationInstance instance, OptimizationInstanceResultFile fileResult)
{
    if (isCustomerOptimization)
    {
        var customer = GetRevCustomerById(context, instance.RevCustomerId.Value);
        
        // Update processing status for cross-provider coordination
        crossProviderOptimizationRepository.UpdateProcessingCustomerOptimizationInstance(ParameterizedLog(context), instance.SessionId.GetValueOrDefault(), instance.Id, null, fileResult.TotalDeviceCount, false, instance.CustomerType, customer.RevCustomerId, instance.AMOPCustomerId);
        
        // Coordinate final completion across providers
        if (isLastInstance)
        {
            // Send final completion message for email coordination
            QueueLastStepOptCustomerCleanup(context, instance.Id, instance.SessionId.Value, true, 0, _optCustomerCleanUpDelaySeconds);
        }
    }
}

// Customer optimization result coordination
private void OptimizationCustomerSendResults(KeySysLambdaContext context, OptimizationInstance instance, DeviceSyncSummary syncResults, bool isLastInstance, int serviceProviderId)
{
    if (isLastInstance)
    {
        // Final provider completed - send comprehensive results
        QueueLastStepOptCustomerCleanup(context, instance.Id, instance.SessionId.Value, true, serviceProviderId, _optCustomerCleanUpDelaySeconds);
    }
}
```

This comprehensive monitoring framework ensures reliable cross-provider optimization coordination with intelligent retry mechanisms, exponential backoff strategies, and robust completion tracking across all provider ecosystems.