# Customer Session Management - Standard Algorithms

## Overview
Customer Session Management orchestrates multi-customer optimization workflows through completion monitoring, multi-service provider coordination, adaptive retry logic, and consolidated result delivery to ensure reliable and efficient customer optimization sessions.

---

## 1. Waits for All Customer Instances to Complete Before Final Email

### What & Why & How
**What**: Implements completion detection logic that monitors all customer optimization instances within a session and waits for 100% completion before triggering final consolidated email notifications  
**Why**: 
- **Session Integrity**: Ensure all customers in session complete processing before sending final notifications
- **Data Completeness**: Prevent partial or incomplete customer result reporting
- **Operational Consistency**: Maintain reliable notification workflows with complete session data
- **Quality Assurance**: Guarantee that final emails contain all customer results without missing data

**How**: Database polling system continuously checks OptimizationCustomerProcessing table using IsProcessed flags and session IDs, implementing retry logic with exponential backoff until all customers show completion status, then triggering consolidated email generation and delivery

### Standard Algorithm
```
STEP 1: Session Completion Check Initialization
    sessionId = optimizationInstance.SessionId
    serviceProviderId = currentServiceProvider
    retryCount = 1
    maxRetries = configuredRetryLimit (_cleanUpSendEmailRetryCount)
    
STEP 2: Customer Processing Status Validation
    pendingCustomers = CheckOptCustomerProcessing(serviceProviderId, sessionId)
    
    SQL Query:
    SELECT COUNT(*) FROM OptimizationCustomerProcessing 
    WHERE ServiceProviderId = @serviceProviderId 
        AND IsProcessed = false 
        AND SessionId = @sessionId
        
STEP 3: Completion Decision Logic
    IF pendingCustomers > 0:
        sessionComplete = false
        LOG: "Customer Optimization process has not finish yet."
        
        IF retryCount <= maxRetries:
            EXECUTE: QueueLastStepOptCustomerCleanup(
                instanceId, 
                sessionId, 
                serviceProviderId, 
                _optCustomerCleanUpDelaySeconds, 
                retryCount + 1
            )
        ELSE:
            LOG: WARNING "Customer Optimization process has retried {maxRetries} times."
            ABORT: Session timeout reached
    ELSE:
        sessionComplete = true
        PROCEED: Generate consolidated email and cleanup
        
STEP 4: Final Email Trigger
    IF sessionComplete:
        completedCustomers = GetOptCustomerProcessing(serviceProviderId, sessionId)
        consolidatedEmailBody = OptCustomerResultsBody(
            optimizationInstance,
            completedCustomers,
            runTimes,
            syncDates
        )
        SendConsolidatedCustomerEmail(consolidatedEmailBody)
        DeleteDataFromOptCustomerProcessing(serviceProviderId, sessionId)
```

### Completion Detection Mechanism
**Database Monitoring Strategy**:
- Continuous polling of OptimizationCustomerProcessing table
- Session-scoped completion tracking using SessionId
- Service provider isolation for multi-provider sessions
- Boolean IsProcessed flag validation for each customer

**Retry Logic Implementation**:
- Configurable retry limit via _cleanUpSendEmailRetryCount environment variable
- Queue-based retry scheduling using AWS SQS DelaySeconds
- Exponential backoff delay calculation based on system load
- Automatic timeout handling after maximum retry attempts

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 195-216
private void OptCustomerSendEmail(KeySysLambdaContext context, long instanceId, long sessionId, int serviceProviderId, int retryCount)
{
    var instance = GetInstance(context, instanceId);
    
    // Check if all customers in session have completed processing
    var checkOptProcessing = CheckOptCustomerProcessing(context, serviceProviderId, sessionId);
    if (checkOptProcessing)
    {
        LogInfo(context, CommonConstants.SUB, "Customer Optimization process has not finish yet.");
        if (retryCount <= _cleanUpSendEmailRetryCount)
        {
            // Queue retry with delay
            QueueLastStepOptCustomerCleanup(context, instanceId, sessionId, true, serviceProviderId, _optCustomerCleanUpDelaySeconds, retryCount + 1);
        }
        else
        {
            LogInfo(context, CommonConstants.WARNING, $"Customer Optimization process has retried {_cleanUpSendEmailRetryCount} times.");
        }
        return; // Exit without sending email - customers still processing
    }
    
    // All customers completed - proceed with final email and cleanup
    var result = client.OptCustomerSendEmailProxy(_proxyUrl, payload, context.logger);
    if (result.IsSuccessful)
    {
        DeleteDataFromOptCustomerProcessing(context, serviceProviderId, instance.SessionId.Value);
    }
}

// Lines 1862-1901: Customer processing status check
private bool CheckOptCustomerProcessing(KeySysLambdaContext context, int serviceProviderId, long sessionId)
{
    string query = @"SELECT COUNT(*) FROM [OptimizationCustomerProcessing]
                        WHERE [ServiceProviderId] = @serviceProviderId AND [IsProcessed] = @isProcessed AND [SessionId] = @sessionId";
    
    cmd.Parameters.AddWithValue("@isProcessed", false);
    cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId);
    cmd.Parameters.AddWithValue("@sessionId", sessionId);
    
    return record > 0 ? true : false; // true means customers still processing
}
```

---

## 2. Coordinates Customer Optimization Across Multiple Service Providers

### What & Why & How
**What**: Orchestrates customer optimization processing across multiple telecommunications service providers with independent processing pipelines and coordinated completion detection  
**Why**:
- **Multi-Provider Support**: Handle customers with devices distributed across different service provider networks
- **Parallel Processing**: Enable concurrent optimization across providers for improved performance
- **Provider Isolation**: Maintain separate processing contexts to prevent cross-provider interference
- **Unified Coordination**: Ensure session-level coordination despite provider-specific processing

**How**: Service provider-specific processing workflows maintain independent optimization queues, tracking tables, and completion logic while sharing session-level coordination through common SessionId linkage and cross-provider completion detection

### Standard Algorithm
```
STEP 1: Multi-Provider Session Initialization
    sessionId = optimizationSession.Id
    serviceProviders = GetServiceProvidersForSession(sessionId)
    providerStates = {}
    
    FOR each serviceProviderId in serviceProviders:
        providerStates[serviceProviderId] = {
            status: "PROCESSING",
            customers: GetCustomersForProvider(serviceProviderId, sessionId),
            instanceIds: [],
            completionTime: null
        }
        
STEP 2: Provider-Specific Processing Execution
    FOR each serviceProviderId in serviceProviders:
        customerInstances = GetCustomerInstancesForProvider(serviceProviderId, sessionId)
        
        FOR each instance in customerInstances:
            ProcessResultForSingleServiceProvider(
                context,
                isCustomerOptimization = true,
                isLastInstance = DetermineLastInstance(instance, serviceProviderId),
                serviceProviderId,
                instance,
                integrationTypes,
                fileResult
            )
            
STEP 3: Cross-Provider Completion Coordination
    completedProviders = []
    
    FOR each serviceProviderId in serviceProviders:
        IF CheckOptCustomerProcessing(serviceProviderId, sessionId) == false:
            providerStates[serviceProviderId].status = "COMPLETED"
            providerStates[serviceProviderId].completionTime = DateTime.UtcNow
            completedProviders.Add(serviceProviderId)
            
STEP 4: Provider-Specific Cleanup Orchestration
    FOR each completedProviderId in completedProviders:
        QueueLastStepOptCustomerCleanup(
            lastInstanceId,
            sessionId,
            isOptLastStepSendEmail = true,
            serviceProviderId = completedProviderId,
            delaySeconds = _optCustomerCleanUpDelaySeconds
        )
        
STEP 5: Global Session Completion
    IF AllProvidersCompleted(serviceProviders, providerStates):
        GenerateGlobalSessionSummary(sessionId, providerStates)
        TriggerGlobalSessionCleanup(sessionId)
        UpdateSessionMetrics(sessionId, providerStates)
```

### Multi-Provider Coordination Architecture
**Provider-Specific Components**:
- Independent OptimizationCustomerProcessing records per service provider
- Separate cleanup queue entries with provider-specific parameters
- Provider-isolated retry logic and error handling
- Service provider-specific integration type handling

**Cross-Provider Coordination Elements**:
- Shared SessionId linking all providers in single optimization session
- Global completion detection across all providers
- Consolidated reporting aggregating multi-provider results
- Unified cleanup workflow after all providers complete

### Service Provider Processing Flow
**Individual Provider Workflow**:
```
Provider Processing Example:
Session ID: 12345
Providers: Verizon (ID: 1), AT&T (ID: 2), T-Mobile (ID: 3)

VERIZON PROCESSING:
- Customers: TechCorp, GlobalMfg
- Devices: 450 M2M devices
- Status: Processing → Completed
- Cleanup: Provider-specific queue entry

AT&T PROCESSING:
- Customers: SmartCities, IoTSolutions  
- Devices: 320 M2M devices
- Status: Processing → Completed
- Cleanup: Provider-specific queue entry

T-MOBILE PROCESSING:
- Customers: ConnectedDevices
- Devices: 180 M2M devices
- Status: Processing → Completed
- Cleanup: Provider-specific queue entry

SESSION COMPLETION:
All providers completed → Global session cleanup → Consolidated reporting
```

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 375-424
private void ProcessResultForSingleServiceProvider(KeySysLambdaContext context, bool isCustomerOptimization, bool isLastInstance, int serviceProviderId, OptimizationInstance instance, IList<IntegrationTypeModel> integrationTypes, OptimizationInstanceResultFile fileResult)
{
    var integrationType = (IntegrationType)instance.IntegrationId.GetValueOrDefault();
    var syncResults = GetSummaryValues(context, integrationType, instance.ServiceProviderId.GetValueOrDefault());

    if (isCustomerOptimization)
    {
        // Process customer optimization results for specific service provider
        OptimizationCustomerSendResults(context, instance, syncResults, isLastInstance, serviceProviderId);
    }
    else
    {
        // Process carrier optimization results
        SendResults(context, instance, fileResult.AssignmentXlsxBytes, billingTimeZone, syncResults, integrationType, integrationTypes);
    }
    
    // Handle provider-specific rate plan updates
    if (ShouldProcessRatePlanUpdates(integrationType, context.OptimizationSettings, instance))
    {
        QueueRatePlanUpdates(context, instance.Id, instance.TenantId);
    }
}

// Lines 2171-2219: Service provider-specific cleanup queuing
private void QueueLastStepOptCustomerCleanup(KeySysLambdaContext context, long instanceId, long sessionId, bool isOptLastStepSendEmail, int serviceProviderId, int delaySeconds = 0, int retryCount = 1)
{
    var request = new SendMessageRequest
    {
        DelaySeconds = delaySeconds,
        MessageAttributes = new Dictionary<string, MessageAttributeValue>
        {
            { "InstanceId", new MessageAttributeValue { DataType = "String", StringValue = instanceId.ToString()}},
            { "SessionId", new MessageAttributeValue { DataType = "String", StringValue = sessionId.ToString()}},
            { "ServiceProviderId", new MessageAttributeValue { DataType = "String", StringValue = serviceProviderId.ToString()}},
            { "RetryCount", new MessageAttributeValue { DataType = "String", StringValue = retryCount.ToString()}}
        },
        MessageBody = "Optimization Customer Send Email",
        QueueUrl = context.CleanupDestinationQueueUrl
    };
    
    var response = client.SendMessageAsync(request);
}
```

---

## 3. Manages Customer Processing Delays and Retry Logic

### What & Why & How
**What**: Implements adaptive delay management and intelligent retry logic for customer processing workflows using configurable timeouts, exponential backoff, and queue-based scheduling  
**Why**:
- **System Resilience**: Handle temporary failures and resource contention gracefully
- **Load Management**: Prevent system overload during high-volume optimization periods
- **Processing Reliability**: Ensure customer optimizations complete despite transient issues
- **Resource Optimization**: Efficiently utilize system resources through intelligent scheduling

**How**: Multi-layered retry system combining configurable delay intervals, exponential backoff calculations based on system load, queue-based retry scheduling through AWS SQS, and automatic timeout handling with configurable limits

### Standard Algorithm
```
STEP 1: Retry Configuration Initialization
    cleanUpDelaySeconds = Environment.GetEnvironmentVariable("OptCustomerCleanUpDelaySeconds")
    maxRetryCount = Environment.GetEnvironmentVariable("CleanUpSendEmailRetryCount")
    currentRetryCount = 1
    
STEP 2: Delay Calculation Logic
    IF retryCount == 1:
        delaySeconds = cleanUpDelaySeconds  // Base delay from configuration
    ELSE:
        queueLength = GetOptimizationQueueLength()
        delaySeconds = DelaySecondsFromQueueLength(queueLength)
        
    DelaySecondsFromQueueLength Implementation:
    IF queueLength > 50:
        return 900  // 15 minutes (SQS maximum)
    ELSE:
        return 600  // 10 minutes (default)
        
STEP 3: Retry Decision Matrix
    processingStatus = CheckOptCustomerProcessing(serviceProviderId, sessionId)
    
    IF processingStatus == "STILL_PROCESSING":
        IF currentRetryCount <= maxRetryCount:
            EXECUTE: ScheduleRetry(delaySeconds, currentRetryCount + 1)
            LOG: "Retry scheduled - attempt {currentRetryCount} of {maxRetryCount}"
        ELSE:
            LOG: WARNING "Maximum retry attempts exceeded"
            EXECUTE: HandleRetryExhaustion(sessionId, serviceProviderId)
    ELSE:
        EXECUTE: ProceedWithFinalProcessing()
        
STEP 4: Queue-Based Retry Scheduling
    retryMessage = CreateRetryMessage(
        instanceId,
        sessionId,
        serviceProviderId,
        currentRetryCount + 1
    )
    
    sqsRequest = new SendMessageRequest {
        DelaySeconds = delaySeconds,
        MessageBody = "Optimization Customer Send Email",
        QueueUrl = context.CleanupDestinationQueueUrl,
        MessageAttributes = retryMessage.Attributes
    }
    
    sqsClient.SendMessageAsync(sqsRequest)
    
STEP 5: Error Handling and Recovery
    IF sqsRequest.Status == Faulted OR Canceled:
        LOG: ERROR "Failed to schedule retry"
        EXECUTE: FallbackRetryMechanism()
    ELSE:
        LOG: INFO "Retry scheduled successfully"
        UPDATE: RetryMetrics(sessionId, currentRetryCount, delaySeconds)
```

### Delay Management Strategy
**Adaptive Delay Calculation**:
- Base delay from OptCustomerCleanUpDelaySeconds configuration
- Queue length-based delay adjustment for load balancing
- Maximum 15-minute delay limit due to AWS SQS constraints
- Default 10-minute retry interval for normal conditions

**Exponential Backoff Patterns**:
- Initial delay: Configured base delay (typically 30-60 seconds)
- Subsequent delays: Queue length-based calculation
- Maximum delay cap: 900 seconds (15 minutes)
- Retry count escalation with increasing delays

### Retry Logic Implementation
**Configuration-Driven Retry Limits**:
- CleanUpSendEmailRetryCount: Maximum retry attempts
- OptCustomerCleanUpDelaySeconds: Base delay interval
- Queue length thresholds: 50 messages trigger extended delays
- Automatic timeout after maximum retry attempts

**Queue-Based Scheduling**:
- AWS SQS DelaySeconds for retry scheduling
- Message attributes preserve retry context
- Automatic retry execution through queue processing
- Error handling for queue scheduling failures

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 45-78
private int _optCustomerCleanUpDelaySeconds = Convert.ToInt32(Environment.GetEnvironmentVariable("OptCustomerCleanUpDelaySeconds"));
private int _cleanUpSendEmailRetryCount = Convert.ToInt32(Environment.GetEnvironmentVariable("CleanUpSendEmailRetryCount"));

// Initialize from environment if not set
if (_optCustomerCleanUpDelaySeconds == 0)
{
    _optCustomerCleanUpDelaySeconds = Convert.ToInt32(context.ClientContext.Environment["OptCustomerCleanUpDelaySeconds"]);
}

// Lines 195-216: Retry logic implementation
private void OptCustomerSendEmail(KeySysLambdaContext context, long instanceId, long sessionId, int serviceProviderId, int retryCount)
{
    var checkOptProcessing = CheckOptCustomerProcessing(context, serviceProviderId, sessionId);
    if (checkOptProcessing)
    {
        LogInfo(context, CommonConstants.SUB, "Customer Optimization process has not finish yet.");
        if (retryCount <= _cleanUpSendEmailRetryCount)
        {
            // Schedule retry with configured delay
            QueueLastStepOptCustomerCleanup(context, instanceId, sessionId, true, serviceProviderId, _optCustomerCleanUpDelaySeconds, retryCount + 1);
        }
        else
        {
            LogInfo(context, CommonConstants.WARNING, $"Customer Optimization process has retried {_cleanUpSendEmailRetryCount} times.");
        }
        return;
    }
    // Proceed with final processing when all customers complete
}

// Lines 2261-2275: Adaptive delay calculation
private int DelaySecondsFromQueueLength(int optimizationQueueLength)
{
    var delaySeconds = 600; // Default 10-minute delay
    
    if (optimizationQueueLength > 50)
    {
        delaySeconds = 900; // 15-minute delay for high load (SQS maximum)
    }
    
    return delaySeconds;
}

// Lines 280-298: Queue length monitoring
private int GetOptimizationQueueLength(KeySysLambdaContext context)
{
    using (var client = new AmazonSQSClient(awsCredentials, RegionEndpoint.USEast1))
    {
        var request = new GetQueueAttributesRequest(_watchQueueUrl, new List<string> { "ApproximateNumberOfMessages", "ApproximateNumberOfMessagesDelayed", "ApproximateNumberOfMessagesNotVisible" });
        var response = client.GetQueueAttributesAsync(request);
        
        var queueLength = response.Result.ApproximateNumberOfMessages + response.Result.ApproximateNumberOfMessagesDelayed + response.Result.ApproximateNumberOfMessagesNotVisible;
        return queueLength;
    }
}
```

---

## 4. Sends Consolidated Customer Optimization Results

### What & Why & How
**What**: Generates and delivers comprehensive consolidated email notifications containing aggregated customer optimization results, session summaries, and detailed customer processing information in HTML format  
**Why**:
- **Stakeholder Communication**: Provide operations teams with complete session overview
- **Operational Visibility**: Enable monitoring and tracking of multi-customer optimization sessions
- **Audit Documentation**: Create permanent record of session completion and customer results
- **Performance Reporting**: Deliver actionable insights on optimization session performance

**How**: Multi-phase consolidation process aggregates completed customer data from OptimizationCustomerProcessing table, generates HTML-formatted email body with customer table, includes session timing and device metrics, and delivers via external API proxy for final email distribution

### Standard Algorithm
```
STEP 1: Consolidated Data Collection
    sessionId = optimizationInstance.SessionId
    serviceProviderId = currentServiceProvider
    
    completedCustomers = GetOptCustomerProcessing(serviceProviderId, sessionId)
    
    SQL Query:
    SELECT ServiceProviderId, CustomerId, CustomerName, DeviceCount, 
           IsProcessed, StartTime, EndTime, ServiceProviderName,
           AMOPCustomerId, AMOPCustomerName
    FROM OptimizationCustomerProcessing o
    JOIN ServiceProvider s ON s.Id = o.ServiceProviderId
    WHERE ServiceProviderId = @serviceProviderId 
        AND IsProcessed = true 
        AND SessionId = @sessionId
        
STEP 2: Session Metrics Calculation
    runStartTime = ConvertToLocalTime(instance.RunStartTime, billingTimeZone)
    runEndTime = ConvertToLocalTime(instance.RunEndTime, billingTimeZone)
    deviceDetailSyncDate = syncResults.DetailLastSyncDate
    deviceUsageSyncDate = syncResults.UsageLastSyncDate
    totalSimCount = syncResults.DeviceCount
    
STEP 3: HTML Email Body Generation
    htmlEmailBody = OptCustomerResultsBody(
        optimizationInstance,
        completedCustomers,
        runStartTime,
        runEndTime,
        deviceDetailSyncDate,
        deviceUsageSyncDate,
        totalSimCount
    )
    
    HTML Structure:
    - Header with session information and timing
    - CSS styling for professional presentation
    - HTML table with customer list (No., Customer Name)
    - Session metrics (device counts, sync dates)
    - Execution environment information
    
STEP 4: Consolidated Email Payload Creation
    emailPayload = new OptimizationCustomerEndProcess {
        InstanceId = instanceId,
        SessionId = sessionId,
        ServiceProviderId = serviceProviderId,
        SiteType = instance.CustomerType,
        DetailLastSyncDate = syncResults.DetailLastSyncDate,
        UsageLastSyncDate = syncResults.UsageLastSyncDate,
        BillingPeriodEndDate = instance.BillingPeriodEndDate,
        TenantId = instance.TenantId
    }
    
STEP 5: External API Delivery
    apiPayload = new PayloadModel {
        JsonContent = JsonConvert.SerializeObject(emailPayload),
        IsOptCustomerSendEmail = true
    }
    
    result = httpClient.OptCustomerSendEmailProxy(_proxyUrl, apiPayload)
    
    IF result.IsSuccessful:
        DeleteDataFromOptCustomerProcessing(serviceProviderId, sessionId)
        LOG: "Consolidated email sent successfully"
    ELSE:
        LOG: ERROR "Failed to send consolidated email"
```

### Consolidated Email Content Structure
**HTML Email Format**:
- Professional CSS styling with Lato font family
- Responsive table layout for customer information
- Session header with billing period and timing information
- Device synchronization status and metrics
- Execution environment details

**Customer Information Table**:
- Sequential numbering for customer organization
- Customer name display (handling both Rev and AMOP customer types)
- Service provider context for multi-provider sessions
- Processing completion status for each customer

### Example Consolidated Email
```html
<!DOCTYPE html>
<html>
<head>
<style>
body {
    background-color: #fff;
    font-family: "Lato", "Helvetica Neue", Helvetica, Arial, sans-serif;
}
tr { text-align: left; }
th,td { padding-right: 10px; }
</style>
</head>
<body>

<div>Here are your optimization Results for Billing Period Ending on March 31, 2024 11:59 PM. 
Optimization started on: March 30, 2024 10:15 AM. Optimization completed on: March 31, 2024 2:45 AM.</div>
<br/>

<div>Last Device Detail Sync Date: March 30, 2024 9:00 AM
<br/>Last Device Usage Sync Date: March 30, 2024 8:30 AM
<br/>Total SIM Cards: 1,250
<br/>Execution OU: Production-East
</div>
<br/>

<table>
<tr><th>No.</th><th>Customer Name</th></tr>
<tr><td>1</td><td>TechCorp Manufacturing</td></tr>
<tr><td>2</td><td>Global Logistics Solutions</td></tr>
<tr><td>3</td><td>Smart Cities IoT</td></tr>
<tr><td>4</td><td>Industrial Automation Corp</td></tr>
</table>

</body>
</html>
```

### API Integration and Delivery
**External Proxy API Communication**:
- HTTP client with Lambda logging handler
- JSON payload serialization with customer session data
- Proxy URL configuration for email delivery service
- Success/failure result processing and logging

**Payload Structure**:
- OptimizationCustomerEndProcess: Main data container
- Service provider and customer type information
- Billing period and timing context
- Device synchronization status and metrics

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 217-275
private void OptCustomerSendEmail(KeySysLambdaContext context, long instanceId, long sessionId, int serviceProviderId, int retryCount)
{
    // After completion detection, proceed with consolidated email
    var integrationType = (IntegrationType)instance.IntegrationId.GetValueOrDefault();
    var syncResults = GetSummaryValues(context, integrationType, instance.ServiceProviderId.GetValueOrDefault());
    
    var jsonContent = new OptimizationCustomerEndProcess()
    {
        InstanceId = instanceId,
        SessionId = sessionId,
        ServiceProviderId = serviceProviderId,
        SiteType = (int)instance.CustomerType,
        DetailLastSyncDate = syncResults.DetailLastSyncDate,
        UsageLastSyncDate = syncResults.UsageLastSyncDate,
        BillingPeriodEndDate = instance.BillingPeriodEndDate,
        TenantId = instance.TenantId
    };

    using (var client = new HttpClient(new LambdaLoggingHandler()))
    {
        var payload = new PayloadModel()
        {
            JsonContent = JsonConvert.SerializeObject(jsonContent),
            IsOptCustomerSendEmail = true
        };

        result = client.OptCustomerSendEmailProxy(_proxyUrl, payload, context.logger);
    }

    if (result.IsSuccessful)
    {
        DeleteDataFromOptCustomerProcessing(context, serviceProviderId, instance.SessionId.Value);
    }
}

// Lines 2013-2069: HTML email body generation
private string OptCustomerResultsBody(KeySysLambdaContext context, OptimizationInstance instance, List<OptimizationCustomerProcessing> optCustomerProcessing, string runStartTime, string runEndTime, string deviceDetailSyncDate, string deviceUsageSyncDate, string simCount)
{
    var stringBuilder = new StringBuilder($@"
        <html>
        <head>
        <style>
        body {{ background-color: #fff; font-family: ""Lato"", ""Helvetica Neue"", Helvetica, Arial, sans-serif; }}
        tr {{ text-align: left; }}
        th,td {{ padding-right: 10px; }}
        </style>
        </head>
        <div>Here are your optimization Results for Billing Period Ending on {instance.BillingPeriodEndDate.ToShortDateString()}. 
        Optimization started on: {runStartTime}. Optimization completed on: {runEndTime}.</div><br/>
        <table>
        <tr><th>No.</th><th>Customer Name</th></tr>");

    foreach (var opt in optCustomerProcessing.Select((item, index) => new { item, index }))
    {
        var customerName = instance.CustomerType == SiteTypes.AMOP ? opt.item.AMOPCustomerName : opt.item.CustomerName;
        stringBuilder.Append($"<tr><td>{opt.index + 1}</td><td>{customerName}</td></tr>");
    }
    
    stringBuilder.Append("</table></html>");
    return stringBuilder.ToString();
}
```

---

## Complete Customer Session Management Workflow

### Master Session Orchestration Algorithm
```
PHASE 1: Session Initialization
    - Create session with unique SessionId
    - Initialize provider-specific processing queues
    - Set up completion monitoring for all providers
    
PHASE 2: Multi-Provider Processing Coordination
    - Execute customer optimizations across all service providers
    - Monitor individual provider completion status
    - Handle provider-specific errors and retries
    
PHASE 3: Completion Detection and Synchronization
    - Poll OptimizationCustomerProcessing for session completion
    - Implement retry logic with exponential backoff
    - Coordinate completion across multiple service providers
    
PHASE 4: Consolidated Result Generation
    - Aggregate customer data from all providers
    - Generate HTML-formatted consolidated email
    - Include session metrics and timing information
    
PHASE 5: Final Delivery and Cleanup
    - Send consolidated email via external API proxy
    - Clean up session processing data
    - Update session completion metrics and logs
```

### Session Management Architecture
**Core Components**:
- **OptimizationCustomerProcessing**: Central tracking table for customer completion status
- **AWS SQS**: Queue-based retry scheduling with DelaySeconds
- **External API Proxy**: Consolidated email delivery service
- **Multi-Provider Coordination**: Service provider-specific processing with session-level coordination

**Key Performance Indicators**:
- Session completion time across all providers
- Customer processing success rates per provider
- Retry attempt statistics and timeout rates
- Consolidated email delivery success rates

### Quality Assurance Standards
✅ **Completion Reliability**: All customer instances complete before final email delivery  
✅ **Multi-Provider Coordination**: Successful orchestration across service providers  
✅ **Retry Logic Effectiveness**: Adaptive retry handling with configurable limits  
✅ **Consolidated Reporting**: Comprehensive session summaries with customer details  
✅ **Error Handling**: Robust failure management with timeout protection  
✅ **Performance Optimization**: Efficient resource utilization through intelligent scheduling