# Customer Processing Tracking - Standard Algorithms

## Overview
Customer processing tracking system manages the complete lifecycle of customer optimization processes through database state management, session coordination, service provider orchestration, and automated cleanup workflows.

---

## 1. Updates OptimizationCustomerProcessing Table

### What & Why & How
**What**: Maintains real-time processing state for each customer optimization instance in the OptimizationCustomerProcessing database table  
**Why**: 
- **State Management**: Track processing status and completion for each customer across multiple service providers
- **Coordination Control**: Enable multi-customer session coordination and synchronization
- **Audit Trail**: Maintain historical record of customer processing metrics and timestamps
- **Cleanup Orchestration**: Provide completion signals for automated cleanup and notification workflows

**How**: Database operations update customer processing records with device counts, timestamps, completion status, and instance associations using customer type-specific queries (Rev vs AMOP customer identification)

### Standard Algorithm
```
STEP 1: Customer Type Determination
    IF siteType == SiteTypes.Rev:
        customerIdField = "CustomerId"
        customerIdValue = revCustomerId
    ELSE IF siteType == SiteTypes.AMOP:
        customerIdField = "AMOPCustomerId"  
        customerIdValue = amopCustomerId
        
STEP 2: Processing State Update
    UPDATE OptimizationCustomerProcessing SET
        DeviceCount = totalDeviceCount,
        IsProcessed = true,
        EndTime = currentUtcTime,
        InstanceId = optimizationInstanceId
    WHERE customerIdField = customerIdValue
        AND ServiceProviderId = serviceProviderId
        AND SessionId = sessionId
        
STEP 3: Completion Validation
    - Verify update operation success
    - Log processing completion status
    - Trigger downstream workflows if applicable
    
STEP 4: Session Coordination Check
    IF isLastInstance == true:
        QueueLastStepOptCustomerCleanup(instanceId, sessionId, serviceProviderId)
```

### Database Schema Operations
**OptimizationCustomerProcessing Table Structure**:
- **ServiceProviderId**: Groups customers by service provider
- **CustomerId** / **AMOPCustomerId**: Customer identification (Rev vs AMOP)
- **CustomerName** / **AMOPCustomerName**: Customer display names
- **DeviceCount**: Total devices processed for customer
- **IsProcessed**: Boolean completion status flag
- **StartTime** / **EndTime**: Processing timestamp boundaries
- **SessionId**: Links related customer optimizations
- **InstanceId**: Associates with optimization instance

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1777-1828
private void UpdateOptCustomerProcessing(KeySysLambdaContext context, string customerId, DateTime endTime, int deviceCount, int serviceProviderId, SiteTypes siteType, OptimizationInstance instance)
{
    var query = @"UPDATE [OptimizationCustomerProcessing]
                    SET [DeviceCount] = @deviceCount,
                        [IsProcessed] = @isProcessing,
                        [EndTime] = @endTime,
                        [InstanceId] = @instanceId
                    WHERE {0}
                    AND [ServiceProviderId] = @serviceProviderId
                    AND [SessionId] = @sessionId";

    if (siteType == SiteTypes.Rev)
    {
        query = string.Format(query, "[CustomerId] = @customerId");
    }
    else
    {
        query = string.Format(query, "[AMOPCustomerId] = @amopCustomerId");
    }
    
    // Execute parameterized SQL update with customer-specific parameters
    cmd.Parameters.AddWithValue("@deviceCount", deviceCount);
    cmd.Parameters.AddWithValue("@isProcessing", true);
    cmd.Parameters.AddWithValue("@endTime", endTime);
    cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId);
    cmd.Parameters.AddWithValue("@instanceId", instance.Id);
    cmd.Parameters.AddWithValue("@sessionId", instance.SessionId);
}

// Lines 1742-1776: Customer processing coordination
private void OptimizationCustomerSendResults(KeySysLambdaContext context, OptimizationInstance instance, DeviceSyncSummary syncResults, bool isLastInstance, int serviceProviderId)
{
    // Calculate total device count across M2M and Mobility platforms
    var totalM2MSimCount = GetTotalSimCountForCustomer(context, customer.RevCustomerId, instance.TenantId);
    var totalMobilitySimCount = GetTotalMobilitySimCountForCustomer(context, customer.RevCustomerId, instance.TenantId);
    syncResults.DeviceCount = totalM2MSimCount.GetValueOrDefault() + totalMobilitySimCount.GetValueOrDefault();

    // Update processing table with completion status
    UpdateOptCustomerProcessing(context, customerId, DateTime.UtcNow, (int)syncResults.DeviceCount, serviceProviderId, siteType, instance);
}
```

---

## 2. Tracks Customer Optimization Session Progress

### What & Why & How
**What**: Monitors and coordinates multi-customer optimization sessions to ensure all customers complete processing before triggering final workflows  
**Why**:
- **Session Synchronization**: Coordinate completion across multiple customers in single optimization session
- **Resource Management**: Prevent premature cleanup while customer optimizations are still processing
- **Workflow Orchestration**: Trigger consolidated notifications and cleanup only when all customers complete
- **Quality Assurance**: Ensure complete processing before customer communication and cleanup

**How**: Database polling monitors customer processing completion status using IsProcessed flags and session IDs, implementing completion detection logic that waits for all customers in session to finish processing

### Standard Algorithm
```
STEP 1: Session Monitoring Initialization
    activeCustomers = []
    completedCustomers = []
    sessionId = optimizationInstance.SessionId
    
STEP 2: Customer Processing Status Check
    pendingCustomers = COUNT(
        SELECT * FROM OptimizationCustomerProcessing 
        WHERE SessionId = sessionId 
            AND IsProcessed = false
            AND ServiceProviderId = serviceProviderId
    )
    
STEP 3: Completion Detection Logic
    IF pendingCustomers == 0:
        sessionComplete = true
        TRIGGER: QueueLastStepOptCustomerCleanup()
    ELSE:
        sessionComplete = false
        CONTINUE: Monitor with exponential backoff
        
STEP 4: Progress Reporting
    completedCustomers = GetOptCustomerProcessing(sessionId, serviceProviderId)
    GenerateSessionProgressReport(completedCustomers)
    
STEP 5: Final Session Coordination
    IF sessionComplete:
        - Generate consolidated customer summary email
        - Execute session-wide cleanup operations
        - Delete processing tracking records
```

### Session Coordination Workflow
**Multi-Customer Session Management**:
- Each customer optimization runs as separate instance within session
- Session ID links related customer optimizations together
- Completion detection waits for all customers to finish processing
- Final notification sent only when session is complete

**Progress Monitoring Points**:
- Individual customer completion status updates
- Device count aggregation per customer
- Processing timestamp tracking (StartTime, EndTime)
- Error handling and retry logic coordination

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1862-1901
private bool CheckOptCustomerProcessing(KeySysLambdaContext context, int serviceProviderId, long sessionId)
{
    int record = 0;
    string query;
    if (serviceProviderId > 0)
    {
        query = @"SELECT COUNT(*) FROM [OptimizationCustomerProcessing]
                    WHERE [ServiceProviderId] = @serviceProviderId AND [IsProcessed] = @isProcessed AND [SessionId] = @sessionId";
    }
    else
    {
        query = @"SELECT COUNT(*) FROM [OptimizationCustomerProcessing]
                    WHERE [IsProcessed] = @isProcessed AND [SessionId] = @sessionId";
    }
    
    cmd.Parameters.AddWithValue("@isProcessed", false);
    cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId);
    cmd.Parameters.AddWithValue("@sessionId", sessionId);
    
    return record > 0 ? true : false; // Returns true if customers still processing
}

// Lines 1903-1945: Completed customer data retrieval
private List<OptimizationCustomerProcessing> GetOptCustomerProcessing(KeySysLambdaContext context, int serviceProviderId, long sessionId)
{
    var query = @"SELECT [ServiceProviderId], [CustomerId], [CustomerName], [DeviceCount], [IsProcessed], [StartTime], [EndTime], s.[Name], o.[AMOPCustomerId], o.[AMOPCustomerName]
                  FROM [OptimizationCustomerProcessing] o
                  JOIN ServiceProvider s ON s.Id = o.ServiceProviderId
                  WHERE [ServiceProviderId] = @serviceProviderId AND [IsProcessed] = @isProcessed AND [SessionId] = @sessionId";
                  
    // Returns completed customers for session summary generation
    cmd.Parameters.AddWithValue("@isProcessed", true);
    cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId);
    cmd.Parameters.AddWithValue("@sessionId", sessionId);
}
```

---

## 3. Manages Customer Processing State Across Service Providers

### What & Why & How
**What**: Orchestrates customer optimization processing across multiple service providers with independent state management and coordination  
**Why**:
- **Multi-Provider Support**: Handle customers with devices across different telecommunications service providers
- **Independent Processing**: Enable parallel processing of customer optimizations per service provider
- **State Isolation**: Maintain separate processing state for each service provider to prevent conflicts
- **Scalability**: Support concurrent customer processing across distributed service provider systems

**How**: Service provider-specific processing logic maintains separate state tracking per provider, coordinates result compilation, and manages provider-specific cleanup workflows while maintaining session-level coordination

### Standard Algorithm
```
STEP 1: Service Provider Identification
    FOR each serviceProviderId in customerServiceProviders:
        processingState[serviceProviderId] = {
            customers: GetCustomersForServiceProvider(serviceProviderId),
            status: "PENDING",
            sessionId: optimizationSessionId
        }
        
STEP 2: Provider-Specific Processing
    FOR each serviceProviderId:
        - Execute customer optimizations for provider
        - Update provider-specific processing records
        - Track completion status independently
        - Handle provider-specific error conditions
        
STEP 3: Cross-Provider State Coordination
    completedProviders = []
    FOR each serviceProviderId:
        IF AllCustomersCompleteForProvider(serviceProviderId, sessionId):
            completedProviders.Add(serviceProviderId)
            
STEP 4: Provider Cleanup Management
    FOR each completedProviderId in completedProviders:
        IF IsLastInstanceForProvider(completedProviderId):
            QueueLastStepOptCustomerCleanup(instanceId, sessionId, completedProviderId)
            
STEP 5: Global Session Completion
    IF AllProvidersComplete(sessionId):
        ExecuteGlobalSessionCleanup(sessionId)
        GenerateConsolidatedCustomerReport(sessionId)
```

### Service Provider State Management
**Provider-Specific Operations**:
- Independent customer processing queues per service provider
- Separate completion tracking and status management
- Provider-specific retry logic and error handling
- Isolated cleanup workflows preventing cross-provider interference

**Cross-Provider Coordination**:
- Session-level coordination across all service providers
- Consolidated reporting combining all provider results
- Global cleanup triggering after all providers complete
- Unified customer communication aggregating multi-provider data

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 375-424
private void ProcessResultForSingleServiceProvider(KeySysLambdaContext context, bool isCustomerOptimization, bool isLastInstance, int serviceProviderId, OptimizationInstance instance, IList<IntegrationTypeModel> integrationTypes, OptimizationInstanceResultFile fileResult)
{
    // Get service provider-specific sync results
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
    
    // Handle provider-specific rate plan updates if applicable
    if (ShouldProcessRatePlanUpdates(integrationType, context.OptimizationSettings, instance))
    {
        QueueRatePlanUpdates(context, instance.Id, instance.TenantId);
    }
}

// Lines 2171-2219: Service provider-specific cleanup queuing
private void QueueLastStepOptCustomerCleanup(KeySysLambdaContext context, long instanceId, long sessionId, bool isOptLastStepSendEmail, int serviceProviderId, int delaySeconds = 0, int retryCount = 1)
{
    var requestMsgBody = $"Optimization Customer Send Email";
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
        MessageBody = requestMsgBody,
        QueueUrl = context.CleanupDestinationQueueUrl
    };
    
    // Queue provider-specific cleanup operations
    var response = client.SendMessageAsync(request);
}
```

---

## 4. Handles Customer-Specific Cleanup Logic

### What & Why & How
**What**: Executes automated cleanup workflows that remove temporary processing data, send final notifications, and reset system state after customer optimization completion  
**Why**:
- **Resource Management**: Clean up temporary processing tables and queue entries to prevent database bloat
- **System Hygiene**: Remove completed processing records to maintain optimal system performance
- **Notification Completion**: Send final customer notifications with consolidated optimization results
- **State Reset**: Prepare system for next optimization cycle by clearing session-specific data

**How**: Multi-phase cleanup process deletes processing records, triggers final email notifications, removes optimization queue entries, and performs system state reset using automated workflows with retry logic and error handling

### Standard Algorithm
```
STEP 1: Cleanup Readiness Validation
    IF CheckOptCustomerProcessing(serviceProviderId, sessionId) == false:
        allCustomersComplete = true
        PROCEED: Execute cleanup workflow
    ELSE:
        allCustomersComplete = false
        RETRY: Requeue cleanup with exponential backoff
        
STEP 2: Customer Notification Generation
    completedCustomers = GetOptCustomerProcessing(serviceProviderId, sessionId)
    consolidatedReport = OptCustomerResultsBody(
        optimizationInstance,
        completedCustomers,
        runTimes,
        syncDates
    )
    SendConsolidatedCustomerEmail(consolidatedReport)
    
STEP 3: Processing Data Cleanup
    DELETE FROM OptimizationCustomerProcessing 
    WHERE ServiceProviderId = serviceProviderId 
        AND SessionId = sessionId
        
STEP 4: Optimization Queue Cleanup
    FOR each communicationPlanGroup in session:
        winningQueueId = GetWinningQueueId(commGroupId)
        CleanupDeviceResultsForCommGroup(commGroupId, winningQueueId)
        EndQueuesForCommGroup(commGroupId)
        
STEP 5: System State Reset
    - Clear temporary optimization result tables
    - Reset processing flags and status indicators
    - Log cleanup completion for audit purposes
    - Update system metrics and performance counters
```

### Cleanup Workflow Components
**Data Cleanup Operations**:
- OptimizationCustomerProcessing table record deletion
- OptimizationDeviceResult temporary data removal
- OptimizationQueue status updates and cleanup
- Session-specific temporary file cleanup

**Notification Workflows**:
- Consolidated customer summary email generation
- HTML formatted customer table with processing results
- Final optimization completion notifications
- Error notification handling for failed operations

### Example Cleanup Execution
```
Session Cleanup Example:
Session ID: 12345
Service Provider: Verizon
Customers Processed: 4 customers

CLEANUP SEQUENCE:
1. Verify all 4 customers completed processing
2. Generate consolidated HTML email with customer table
3. Send final notification to operations team
4. Delete 4 records from OptimizationCustomerProcessing
5. Clean up 12 optimization queues for customer device groups
6. Remove 1,250 temporary device result records
7. Reset session processing flags
8. Log cleanup completion

RESULT:
- Database cleaned of temporary session data
- Operations team notified of completion
- System ready for next optimization session
```

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1829-1861
private void DeleteDataFromOptCustomerProcessing(KeySysLambdaContext context, int serviceProviderId, long sessionId)
{
    string query;
    if (serviceProviderId > 0)
    {
        query = @"DELETE FROM [OptimizationCustomerProcessing]
                    WHERE [ServiceProviderId] = @serviceProviderId AND [SessionId] = @sessionId";
    }
    else
    {
        query = @"DELETE FROM [OptimizationCustomerProcessing]
                    WHERE [SessionId] = @sessionId";
    }
    
    cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId);
    cmd.Parameters.AddWithValue("@sessionId", sessionId);
    
    var rdr = cmd.ExecuteNonQuery(); // Execute cleanup deletion
}

// Lines 2094-2135: Communication plan group cleanup
private void EndQueuesForCommGroup(KeySysLambdaContext context, long commGroupId)
{
    using (var cmd = new SqlCommand("UPDATE OptimizationQueue WITH (HOLDLOCK) SET RunEndTime = GETUTCDATE(), RunStatusId = @runStatusId, TotalCost = NULL WHERE CommPlanGroupId = @commGroupId AND RunEndTime IS NULL", conn))
    {
        cmd.Parameters.AddWithValue("@commGroupId", commGroupId);
        cmd.Parameters.AddWithValue("@runStatusId", (int)OptimizationStatus.CompleteWithErrors);
        cmd.ExecuteNonQuery(); // End non-winning optimization queues
    }
}

private void CleanupDeviceResultsForCommGroup(KeySysLambdaContext context, long commGroupId, long queueId)
{
    using (var cmd = new SqlCommand("usp_Optimization_DeviceResultAndQueueRatePlan_Cleanup", conn))
    {
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.AddWithValue("@commGroupId", commGroupId);
        cmd.Parameters.AddWithValue("@winningQueueId", queueId);
        cmd.CommandTimeout = 900;
        cmd.ExecuteNonQuery(); // Execute stored procedure for comprehensive cleanup
    }
}
```

---

## Complete Customer Processing Tracking Workflow

### Master Process Algorithm
```
PHASE 1: Processing Initialization
    - Create OptimizationCustomerProcessing records for session
    - Initialize customer processing state tracking
    - Set up service provider-specific processing queues
    
PHASE 2: Active Processing Monitoring
    - Update customer processing state as optimizations complete
    - Track device counts and processing timestamps
    - Monitor session progress across service providers
    
PHASE 3: Completion Detection
    - Poll OptimizationCustomerProcessing table for completion status
    - Detect when all customers in session have completed processing
    - Trigger cleanup workflows when session is ready
    
PHASE 4: Cleanup Execution
    - Generate consolidated customer notification emails
    - Delete temporary processing records and queue entries
    - Reset system state for next optimization cycle
    
PHASE 5: Audit and Logging
    - Log completion status and cleanup operations
    - Update system metrics and performance counters
    - Record session processing history for analysis
```

### Database Integration Architecture
**Primary Tables**:
- **OptimizationCustomerProcessing**: Central tracking table for customer state
- **OptimizationQueue**: Optimization processing queue management
- **OptimizationDeviceResult**: Temporary device assignment results
- **OptimizationInstance**: Main optimization instance coordination

**Key Relationships**:
- SessionId links related customer optimizations
- ServiceProviderId enables multi-provider coordination
- InstanceId associates processing with optimization runs
- Customer ID fields support both Rev and AMOP customer types

### Quality Assurance Standards
✅ **State Consistency**: Customer processing state accurately reflects optimization progress  
✅ **Session Coordination**: Multi-customer sessions complete in coordinated manner  
✅ **Service Provider Isolation**: Provider-specific processing maintains independent state  
✅ **Cleanup Reliability**: All temporary data removed after successful completion  
✅ **Error Handling**: Robust retry logic handles transient failures and system issues  
✅ **Audit Trail**: Complete processing history maintained for troubleshooting and analysis