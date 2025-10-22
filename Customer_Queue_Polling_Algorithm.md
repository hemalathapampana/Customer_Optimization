# Customer Queue Polling Algorithm

## Overview
The customer optimization queue polling system monitors customer processing completion using exponential backoff strategies, queue depth tracking, and customer-specific retry logic to ensure efficient resource utilization.

---

## 1. Poll Customer Optimization Queues for Completion

### What
Continuously monitors customer optimization queues to detect when processing is complete and ready for cleanup.

### Why
- **Processing Coordination**: Ensures all customer optimizations complete before cleanup
- **Resource Management**: Prevents premature cleanup of active optimization processes
- **Status Monitoring**: Tracks completion status across multiple customer optimization instances
- **System Reliability**: Provides robust completion detection mechanism

### How
The system polls optimization queue status and customer processing records to determine completion.

### Algorithm
```
INPUT: sessionId, serviceProviderId, instanceId

STEP 1: Check Customer Processing Status
    unprocessedCustomers ← CheckOptCustomerProcessing(serviceProviderId, sessionId)
    
    IF unprocessedCustomers = TRUE:
        LogInfo("Customer processing still in progress")
        RETURN POLLING_CONTINUE
    END IF

STEP 2: Check Queue Completion Status
    optimizationQueueLength ← GetOptimizationQueueLength()
    
    IF optimizationQueueLength > 0:
        LogInfo("Optimization queue not empty: " + optimizationQueueLength)
        RETURN POLLING_CONTINUE
    END IF

STEP 3: Verify Instance Status
    instance ← GetInstance(instanceId)
    
    IF instance.RunStatusId NOT IN FINISHED_STATUSES:
        LogInfo("Instance not in finished status")
        RETURN POLLING_CONTINUE
    END IF

STEP 4: Completion Detected
    LogInfo("All customer optimizations completed")
    RETURN POLLING_COMPLETE

OUTPUT: pollingStatus (CONTINUE or COMPLETE)
```

### Code Location
**File: `AltaworxSimCardCostOptimizerCleanup.cs`**
```csharp
// Lines 1862-1901: CheckOptCustomerProcessing function
private bool CheckOptCustomerProcessing(KeySysLambdaContext context, int serviceProviderId, long sessionId)
{
    string query = @"SELECT COUNT(*) FROM [OptimizationCustomerProcessing]
                     WHERE [ServiceProviderId] = @serviceProviderId AND [IsProcessed] = @isProcessed AND [SessionId] = @sessionId";
    // Returns true if unprocessed customers remain
}
```

---

## 2. Exponential Backoff for Customer Processing

### What
Implements exponential backoff timing pattern: 30s → 60s → 120s → max 300s for customer processing retries.

### Why
- **Resource Efficiency**: Avoids overwhelming the system with frequent polling
- **Adaptive Timing**: Increases delay intervals as processing continues
- **System Stability**: Prevents excessive load during peak processing times
- **Graceful Degradation**: Handles extended processing times appropriately

### How
The system calculates delay intervals using exponential backoff with a maximum cap.

### Algorithm
```
INPUT: retryCount, baseDelaySeconds = 30, maxDelaySeconds = 300

STEP 1: Calculate Exponential Delay
    IF retryCount = 0:
        delaySeconds ← baseDelaySeconds  // 30 seconds
    ELSE IF retryCount = 1:
        delaySeconds ← baseDelaySeconds × 2  // 60 seconds
    ELSE IF retryCount = 2:
        delaySeconds ← baseDelaySeconds × 4  // 120 seconds
    ELSE IF retryCount ≥ 3:
        delaySeconds ← maxDelaySeconds  // 300 seconds (max cap)
    END IF

STEP 2: Apply Maximum Cap
    IF delaySeconds > maxDelaySeconds:
        delaySeconds ← maxDelaySeconds
    END IF

STEP 3: Log Backoff Strategy
    LogInfo("Exponential backoff: retry " + retryCount + ", delay " + delaySeconds + "s")

STEP 4: Schedule Next Poll
    ScheduleMessage(delaySeconds, retryCount + 1)

OUTPUT: delaySeconds
```

### Enhanced Implementation
```csharp
private int CalculateExponentialBackoff(int retryCount)
{
    const int baseDelay = 30;     // 30 seconds base
    const int maxDelay = 300;     // 5 minutes maximum
    
    int delaySeconds;
    switch (retryCount)
    {
        case 0: delaySeconds = 30; break;   // First retry: 30s
        case 1: delaySeconds = 60; break;   // Second retry: 60s  
        case 2: delaySeconds = 120; break;  // Third retry: 120s
        default: delaySeconds = 300; break; // Max: 300s
    }
    
    return Math.Min(delaySeconds, maxDelay);
}
```

### Current Implementation (for reference)
**File: `AltaworxSimCardCostOptimizerCleanup.cs`**
```csharp
// Lines 2260-2272: Current delay calculation
private int DelaySecondsFromQueueLength(int optimizationQueueLength)
{
    var delaySeconds = 600;  // 10 minutes default
    
    if (optimizationQueueLength > 50)
    {
        delaySeconds = 900;  // 15 minutes if queue busy
    }
    
    return delaySeconds;
}
```

---

## 3. Track Customer Queue Depths and Processing Status

### What
Monitors and tracks the depth of optimization queues and customer processing status across multiple dimensions.

### Why
- **Load Balancing**: Distributes processing load based on queue depth
- **Performance Monitoring**: Identifies bottlenecks in customer processing
- **Resource Planning**: Provides data for capacity planning decisions  
- **Operational Visibility**: Enables monitoring of system health

### How
The system queries AWS SQS queue attributes and database processing records to track status.

### Algorithm
```
INPUT: watchQueueUrl, serviceProviderId, sessionId

STEP 1: Get Queue Depth Metrics
    queueAttributes ← GetQueueAttributes(watchQueueUrl)
    
    totalQueueDepth ← queueAttributes.ApproximateNumberOfMessages +
                      queueAttributes.ApproximateNumberOfMessagesDelayed +
                      queueAttributes.ApproximateNumberOfMessagesNotVisible
    
    LogInfo("Total queue depth: " + totalQueueDepth)

STEP 2: Track Customer Processing Status
    customerProcessingRecords ← GetOptCustomerProcessing(serviceProviderId, sessionId)
    
    processedCount ← 0
    unprocessedCount ← 0
    
    FOR EACH record IN customerProcessingRecords:
        IF record.IsProcessed = TRUE:
            processedCount ← processedCount + 1
        ELSE:
            unprocessedCount ← unprocessedCount + 1
        END IF
    END FOR

STEP 3: Calculate Processing Metrics
    totalCustomers ← processedCount + unprocessedCount
    completionPercentage ← (processedCount / totalCustomers) × 100
    
    LogInfo("Customer processing: " + processedCount + "/" + totalCustomers + " (" + completionPercentage + "%)")

STEP 4: Assess System Load
    IF totalQueueDepth > 50:
        systemLoad ← "HIGH"
        recommendedDelay ← 900  // 15 minutes
    ELSE IF totalQueueDepth > 10:
        systemLoad ← "MEDIUM" 
        recommendedDelay ← 600  // 10 minutes
    ELSE:
        systemLoad ← "LOW"
        recommendedDelay ← 300  // 5 minutes
    END IF

OUTPUT: queueMetrics { totalDepth, processedCount, unprocessedCount, systemLoad, recommendedDelay }
```

### Code Location
**File: `AltaworxSimCardCostOptimizerCleanup.cs`**
```csharp
// Lines 280-298: Queue depth tracking
private int GetOptimizationQueueLength(KeySysLambdaContext context)
{
    using (var client = new AmazonSQSClient(awsCredentials, RegionEndpoint.USEast1))
    {
        var request = new GetQueueAttributesRequest(_watchQueueUrl, 
            new List<string> { "ApproximateNumberOfMessages", 
                             "ApproximateNumberOfMessagesDelayed", 
                             "ApproximateNumberOfMessagesNotVisible" });
        var response = client.GetQueueAttributesAsync(request);
        
        return response.Result.ApproximateNumberOfMessages + 
               response.Result.ApproximateNumberOfMessagesDelayed + 
               response.Result.ApproximateNumberOfMessagesNotVisible;
    }
}
```

```csharp
// Lines 1903-1945: Customer processing status tracking
private List<OptimizationCustomerProcessing> GetOptCustomerProcessing(KeySysLambdaContext context, int serviceProviderId, long sessionId)
{
    var query = @"SELECT [ServiceProviderId], [CustomerId], [CustomerName], [DeviceCount], [IsProcessed], [StartTime], [EndTime]
                  FROM [OptimizationCustomerProcessing] o
                  WHERE [ServiceProviderId] = @serviceProviderId AND [IsProcessed] = @isProcessed AND [SessionId] = @sessionId";
    // Returns list of customer processing records
}
```

---

## 4. Handle Customer-Specific Retry Logic

### What
Implements customer-specific retry logic with configurable retry limits and error handling.

### Why
- **Customer Isolation**: Prevents one customer's issues from affecting others
- **Resilience**: Provides recovery mechanisms for transient failures
- **Configurability**: Allows tuning retry behavior per customer requirements
- **Error Recovery**: Handles various failure scenarios gracefully

### How
The system tracks retry counts per customer and applies specific retry policies.

### Algorithm
```
INPUT: customerId, serviceProviderId, retryCount, maxRetryCount

STEP 1: Validate Retry Eligibility
    IF retryCount > maxRetryCount:
        LogError("Customer " + customerId + " exceeded max retries (" + maxRetryCount + ")")
        RETURN RETRY_EXHAUSTED
    END IF

STEP 2: Determine Customer-Specific Policy
    customerPolicy ← GetCustomerRetryPolicy(customerId)
    
    maxCustomerRetries ← customerPolicy.MaxRetries ?? maxRetryCount
    retryDelayMultiplier ← customerPolicy.DelayMultiplier ?? 1.0
    priorityLevel ← customerPolicy.Priority ?? "NORMAL"

STEP 3: Calculate Customer-Specific Delay
    baseDelay ← CalculateExponentialBackoff(retryCount)
    customerDelay ← baseDelay × retryDelayMultiplier
    
    IF priorityLevel = "HIGH":
        customerDelay ← customerDelay × 0.5  // Reduce delay for high priority
    ELSE IF priorityLevel = "LOW":
        customerDelay ← customerDelay × 1.5  // Increase delay for low priority
    END IF

STEP 4: Schedule Customer Retry
    nextRetryTime ← CurrentTime + customerDelay
    
    retryMessage ← CreateRetryMessage(customerId, serviceProviderId, retryCount + 1)
    ScheduleMessage(retryMessage, customerDelay)
    
    LogInfo("Customer " + customerId + " retry " + retryCount + " scheduled for " + nextRetryTime)

STEP 5: Update Customer Processing Record
    UpdateCustomerProcessingStatus(customerId, "RETRYING", retryCount + 1, nextRetryTime)

OUTPUT: retryScheduled (boolean)
```

### Enhanced Customer Retry Framework
```csharp
public class CustomerRetryPolicy
{
    public string CustomerId { get; set; }
    public int MaxRetries { get; set; } = 5;
    public double DelayMultiplier { get; set; } = 1.0;
    public string Priority { get; set; } = "NORMAL"; // HIGH, NORMAL, LOW
    public List<string> RetryableErrors { get; set; } = new List<string>();
    public bool EnableExponentialBackoff { get; set; } = true;
}

private bool HandleCustomerSpecificRetry(string customerId, int serviceProviderId, int retryCount, Exception error)
{
    var policy = GetCustomerRetryPolicy(customerId);
    
    // Check if error is retryable for this customer
    if (!policy.RetryableErrors.Contains(error.GetType().Name))
    {
        LogError($"Non-retryable error for customer {customerId}: {error.Message}");
        return false;
    }
    
    // Check retry limit
    if (retryCount >= policy.MaxRetries)
    {
        LogError($"Customer {customerId} exceeded max retries ({policy.MaxRetries})");
        return false;
    }
    
    // Calculate delay with customer-specific adjustments
    var delay = CalculateCustomerDelay(retryCount, policy);
    
    // Schedule retry
    QueueCustomerRetry(customerId, serviceProviderId, retryCount + 1, delay);
    
    return true;
}
```

### Code Location
**File: `AltaworxSimCardCostOptimizerCleanup.cs`**
```csharp
// Lines 195-216: Customer optimization retry logic
private void OptCustomerSendEmail(KeySysLambdaContext context, long instanceId, long sessionId, int serviceProviderId, int retryCount)
{
    if (retryCount <= _cleanUpSendEmailRetryCount)
    {
        QueueLastStepOptCustomerCleanup(context, instanceId, sessionId, true, serviceProviderId, _optCustomerCleanUpDelaySeconds, retryCount + 1);
    }
    else
    {
        LogInfo(context, CommonConstants.WARNING, $"Customer Optimization process has retried {_cleanUpSendEmailRetryCount} times.");
    }
}
```

```csharp
// Lines 2220-2258: Requeue cleanup with retry tracking
private void RequeueCleanup(KeySysLambdaContext context, long instanceId, int retryCount, int optimizationQueueLength, bool isCustomerOptimization)
{
    retryCount += 1;
    int delaySeconds = DelaySecondsFromQueueLength(optimizationQueueLength);
    
    var request = new SendMessageRequest
    {
        DelaySeconds = delaySeconds,
        MessageAttributes = new Dictionary<string, MessageAttributeValue>
        {
            { "RetryCount", new MessageAttributeValue { DataType = "String", StringValue = retryCount.ToString() } }
        }
    };
}
```

---

## Complete Customer Queue Polling Algorithm

### Integrated Polling System
```
INPUT: sessionId, serviceProviderId, instanceId, retryCount

STEP 1: Poll Queue Completion
    pollingStatus ← PollCustomerOptimizationQueues(sessionId, serviceProviderId, instanceId)
    
    IF pollingStatus = POLLING_COMPLETE:
        LogInfo("Customer optimization polling complete")
        RETURN SUCCESS
    END IF

STEP 2: Track Queue Metrics
    queueMetrics ← TrackQueueDepthAndStatus(serviceProviderId, sessionId)
    LogMetrics(queueMetrics)

STEP 3: Apply Exponential Backoff
    delaySeconds ← CalculateExponentialBackoff(retryCount)
    
    IF queueMetrics.systemLoad = "HIGH":
        delaySeconds ← delaySeconds × 1.5  // Increase delay under high load
    END IF

STEP 4: Handle Customer-Specific Retries
    FOR EACH unprocessedCustomer IN queueMetrics.unprocessedCustomers:
        IF unprocessedCustomer.retryCount < maxRetries:
            ScheduleCustomerRetry(unprocessedCustomer, delaySeconds)
        ELSE:
            MarkCustomerFailed(unprocessedCustomer)
        END IF
    END FOR

STEP 5: Schedule Next Polling Cycle
    ScheduleNextPoll(sessionId, serviceProviderId, instanceId, retryCount + 1, delaySeconds)

OUTPUT: pollingContinued (boolean)
```

## Key Implementation Points

**Exponential Backoff Pattern**
- 30s → 60s → 120s → 300s (max)
- Adaptive to system load conditions
- Customer-specific multipliers

**Queue Depth Tracking**
- AWS SQS metrics monitoring
- Database processing status queries
- Real-time system load assessment

**Customer-Specific Logic**
- Individual retry policies per customer
- Priority-based delay adjustments
- Configurable retry limits and error handling

**Error Handling**
- Graceful degradation under high load
- Comprehensive logging and monitoring
- Automatic recovery mechanisms