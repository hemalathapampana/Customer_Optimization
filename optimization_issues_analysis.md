# Optimization System Issues Analysis

## Issue 1: Sessions Not Created or Displayed in Session List

### Root Cause Analysis

**Primary Method**: `StartConfirm` in OptimizationController (lines 334-407)
**Secondary Method**: `FunctionHandler` in QueueCustomerOptimization (lines 46-70)

#### Controller-Side Issues:
```csharp
// Lines 334-407: OptimizationController.StartConfirm
if (optimizationSessionId == 0)
    errorMessage = "Error creating Optimization Session. Please contact AMOP Support.";
```

**Problem 1**: Session creation failure in `CreateOptimizationSession` (lines 308-325) but error is generic without specific logging.

**Problem 2**: Exception handling that masks real issues:
```csharp
// Lines 546-552: EnqueueCustomerOptimizationSqsAsync
catch (Exception ex)
{
    Log.Error($"Error Queuing Optimization for {siteId}", ex);
    return "Error Queuing Optimization: Exception occured";
}
```

#### Lambda-Side Issues:
```csharp
// Lines 64-67: QueueCustomerOptimization.FunctionHandler
catch (Exception ex)
{
    LogInfo(keysysContext, "EXCEPTION", ex.Message);
}
```

**Problem 3**: Lambda exceptions are logged but not propagated, causing silent failures.

**Problem 4**: Early validation failures that prevent session creation:
```csharp
// Lines 149-166: ProcessCustomerOptimizationByPortalType
if (!message.MessageAttributes.ContainsKey("CustomerId") && !message.MessageAttributes.ContainsKey("AMOPCustomerId"))
{
    LogInfo(context, "EXCEPTION", "No Customer Id provided in message");
    return; // Session never created
}
```

### How to Fix

#### Fix 1: Improve Session Creation Error Handling
```csharp
// In OptimizationController.CreateOptimizationSession
try 
{
    await optimizationSesstionRepository.CreateOptimizationSession(optimizationSession);
    return optimizationSession.Id;
}
catch (Exception ex)
{
    Log.Error($"Failed to create optimization session for tenant {permissionManager.Tenant.id}, serviceProvider {serviceProviderId}: {ex.Message}", ex);
    throw; // Propagate to caller for proper error handling
}
```

#### Fix 2: Add Session Creation Validation in Lambda
```csharp
// In QueueCustomerOptimization.ProcessCustomerOptimizationByPortalType
private bool ValidateSessionExists(KeySysLambdaContext context, long optimizationSessionId)
{
    var session = optimizationRepository.GetOptimizationSession(context, optimizationSessionId);
    if (session == null)
    {
        LogInfo(context, "ERROR", $"Optimization session {optimizationSessionId} not found in database");
        return false;
    }
    return true;
}

// Call before processing:
if (!ValidateSessionExists(context, optimizationSessionId))
{
    return;
}
```

#### Fix 3: Add Transaction Rollback Safety
```csharp
// In OptimizationController.StartConfirm
using (var transaction = altaWrxDb.Database.BeginTransaction())
{
    try 
    {
        optimizationSessionId = await CreateOptimizationSession(...);
        
        // Add customer processing records
        foreach (var optCus in customerProcessingRecords)
        {
            altaWrxDb.OptimizationCustomerProcessings.Add(optCus);
        }
        altaWrxDb.SaveChanges();
        
        // Enqueue messages
        errorMessage = await EnqueueCustomerOptimizationAsync(...);
        
        if (!string.IsNullOrEmpty(errorMessage))
        {
            transaction.Rollback();
            // Clean up session
            await CleanupFailedSession(optimizationSessionId);
        }
        else
        {
            transaction.Commit();
        }
    }
    catch (Exception ex)
    {
        transaction.Rollback();
        Log.Error($"Session creation failed: {ex.Message}", ex);
        throw;
    }
}
```

---

## Issue 2: Sessions Remain in "In Progress" Status Indefinitely

### Root Cause Analysis

**Primary Method**: `ProcessDevicesByCustomerRatePlans` in QueueCustomerOptimization (lines 510-564)
**Secondary Method**: `CleanupInstance` in OptimizerCleanup (lines 170-189)

#### Missing Status Updates:
```csharp
// Lines 339-352: QueueCustomerOptimization
if (!isError)
{
    EnqueueCleanup(context, instanceId, ...);
}
else
{
    UpdateCustomerOptimization(context, optimizationSessionId, errorMessage, ...);
    StopOptimizationInstance(context, instanceId, OptimizationStatus.CompleteWithErrors);
}
```

**Problem 1**: If `EnqueueCleanup` fails, no fallback status update occurs.

**Problem 2**: Cleanup retry exhaustion without final status update:
```csharp
// Lines 175-189: OptimizerCleanup
catch (Exception ex)
{
    LogInfo(context, "WARN", $"Error occurred on cleanup, requeuing: {ex.Message}");
    RequeueCleanup(context, instanceId, retryCount, optimizationQueueLength, isCustomerOptimization);
}
// ...
else
{
    LogInfo(context, "EXCEPTION", $"Optimization Cleanup Timed Out. Too many retry attempts.");
    // NO STATUS UPDATE HERE - Session stuck forever
}
```

#### Controller-Side Tracking Issues:
```csharp
// Lines 480-487: EnqueueAllCustomersOptimizationAsync
var optCus = new OptimizationCustomerProcessing()
{
    StartTime = DateTime.UtcNow,
    IsProcessed = false, // Never updated on completion
    SessionId = optimizationSessionId
};
```

### How to Fix

#### Fix 1: Add Fallback Status Updates
```csharp
// In QueueCustomerOptimization.ProcessDevicesByCustomerRatePlans
if (!isError)
{
    try 
    {
        EnqueueCleanup(context, instanceId, ...);
    }
    catch (Exception ex)
    {
        LogInfo(context, "ERROR", $"Failed to enqueue cleanup for instance {instanceId}: {ex.Message}");
        // Fallback: Mark as complete with errors
        UpdateCustomerOptimization(context, optimizationSessionId, "Cleanup enqueue failed", serviceProviderId.Value, revAccountNumber);
        StopOptimizationInstance(context, instanceId, OptimizationStatus.CompleteWithErrors);
    }
}
```

#### Fix 2: Fix Cleanup Retry Exhaustion
```csharp
// In OptimizerCleanup.ProcessCleanupRecord
else if (retryCount >= 10)
{
    LogInfo(context, "EXCEPTION", $"Optimization Cleanup Timed Out. Too many retry attempts.");
    
    // Mark instance as failed instead of leaving in progress
    try 
    {
        StopOptimizationInstance(context, instanceId, OptimizationStatus.CompleteWithErrors);
        
        // Update session status
        var sessionRepo = new OptimizationSessionRepository(altaWrxDb);
        sessionRepo.UpdateSessionStatus(instanceId, "Cleanup failed after maximum retries");
    }
    catch (Exception ex)
    {
        LogInfo(context, "ERROR", $"Failed to update status for timed out instance {instanceId}: {ex.Message}");
    }
}
```

#### Fix 3: Add Watchdog Process
```csharp
// New method in OptimizationController
public async Task<ActionResult> CleanupStuckOptimizations()
{
    var stuckThreshold = DateTime.UtcNow.AddHours(-2); // 2 hours timeout
    
    var stuckInstances = altaWrxDb.OptimizationInstances
        .Where(oi => oi.RunStatusId == (int)OptimizationStatus.InProgress 
                  && oi.RunStartTime < stuckThreshold)
        .ToList();
    
    foreach (var instance in stuckInstances)
    {
        try 
        {
            // Force completion
            instance.RunStatusId = (int)OptimizationStatus.CompleteWithErrors;
            instance.RunEndTime = DateTime.UtcNow;
            
            // Update customer processing records
            var customerRecords = altaWrxDb.OptimizationCustomerProcessings
                .Where(ocp => ocp.SessionId == instance.OptimizationSessionId);
            
            foreach (var record in customerRecords)
            {
                record.IsProcessed = true;
                record.EndTime = DateTime.UtcNow;
            }
            
            altaWrxDb.SaveChanges();
            
            Log.Info($"Cleaned up stuck optimization instance {instance.Id}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to cleanup stuck instance {instance.Id}: {ex.Message}", ex);
        }
    }
    
    return Json(new { cleaned = stuckInstances.Count });
}
```

#### Fix 4: Update Customer Processing Status
```csharp
// In QueueCustomerOptimization.finally block
finally
{
    optimizationRepository.MarkProcessedOptimizationInstanceTrackingRecord(context, optimizationSessionId, customerId, amopCustomerId);
    
    // Also update controller-side tracking
    try 
    {
        UpdateCustomerProcessingStatus(context, optimizationSessionId, customerId, amopCustomerId, true);
    }
    catch (Exception ex)
    {
        LogInfo(context, "WARN", $"Failed to update customer processing status: {ex.Message}");
    }
}

private void UpdateCustomerProcessingStatus(KeySysLambdaContext context, long sessionId, Guid? customerId, int? amopCustomerId, bool isProcessed)
{
    // Implementation to update OptimizationCustomerProcessing.IsProcessed
    var connectionString = context.ConnectionString.Replace("AltaWorxCentral", context.TenantDbName);
    
    using (var conn = new SqlConnection(connectionString))
    {
        var sql = @"UPDATE OptimizationCustomerProcessing 
                   SET IsProcessed = @isProcessed, EndTime = @endTime 
                   WHERE SessionId = @sessionId";
        
        if (customerId.HasValue)
            sql += " AND CustomerId = @customerId";
        if (amopCustomerId.HasValue)
            sql += " AND AMOPCustomerId = @amopCustomerId";
            
        using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@isProcessed", isProcessed);
            cmd.Parameters.AddWithValue("@endTime", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@sessionId", sessionId);
            
            if (customerId.HasValue)
                cmd.Parameters.AddWithValue("@customerId", customerId.Value);
            if (amopCustomerId.HasValue)
                cmd.Parameters.AddWithValue("@amopCustomerId", amopCustomerId.Value);
                
            conn.Open();
            cmd.ExecuteNonQuery();
        }
    }
}
```

---

## Issue 3: Multiple Duplicate Entries for Same Optimization Session

### Root Cause Analysis

**Primary Method**: `AddOptimizationInstanceToBeProcessed` called in controller (lines 489, 507, 531, 549)
**Secondary Method**: SQS message processing without deduplication

#### Controller-Side Race Conditions:
```csharp
// Lines 489, 507: EnqueueAllCustomersOptimizationAsync
optimizationSessionRepository.AddOptimizationInstanceToBeProcessed(optimizationSessionId, customer.AmopCustomerId, null);
optimizationSessionRepository.AddOptimizationInstanceToBeProcessed(optimizationSessionId, null, amopCustomer.SiteId);
```

**Problem 1**: Multiple calls create duplicate tracking records for same session.

#### Lambda-Side SQS Duplication:
```csharp
// Missing deduplication check in ProcessEventRecord
// Unlike regular optimizer which has:
if (QUEUE_FINISHED_STATUSES.Contains(queue.RunStatusId))
{
    LogInfo(context, "WARNING", $"Duplicated queue processing request...");
    continue;
}
```

#### Database Query Issues:
The UI queries likely don't use proper DISTINCT or GROUP BY, causing same session to appear multiple times when joined with multiple service providers or customers.

### How to Fix

#### Fix 1: Add Session Deduplication in Lambda
```csharp
// In QueueCustomerOptimization.ProcessEventRecord
private bool IsSessionAlreadyProcessing(KeySysLambdaContext context, long optimizationSessionId, Guid? customerId, int? amopCustomerId)
{
    using (var conn = new SqlConnection(context.ConnectionString))
    {
        var sql = @"SELECT COUNT(*) FROM OptimizationInstanceTracking 
                   WHERE OptimizationSessionId = @sessionId 
                   AND IsProcessed = 1";
        
        if (customerId.HasValue)
            sql += " AND RevCustomerId = @customerId";
        if (amopCustomerId.HasValue)
            sql += " AND AMOPCustomerId = @amopCustomerId";
            
        using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@sessionId", optimizationSessionId);
            if (customerId.HasValue)
                cmd.Parameters.AddWithValue("@customerId", customerId.Value);
            if (amopCustomerId.HasValue)
                cmd.Parameters.AddWithValue("@amopCustomerId", amopCustomerId.Value);
                
            conn.Open();
            return (int)cmd.ExecuteScalar() > 0;
        }
    }
}

// Add check in ProcessEventRecord:
if (IsSessionAlreadyProcessing(context, optimizationSessionId, customerId, amopCustomerId))
{
    LogInfo(context, "WARNING", $"Duplicate processing request for session {optimizationSessionId}, customer {customerId ?? amopCustomerId?.ToString()}");
    return;
}
```

#### Fix 2: Add Database Constraints
```sql
-- Add unique constraint to prevent duplicate tracking records
ALTER TABLE OptimizationInstanceTracking 
ADD CONSTRAINT UK_OptimizationTracking_Session_Customer 
UNIQUE (OptimizationSessionId, RevCustomerId, AMOPCustomerId);

-- Add unique constraint for customer processing
ALTER TABLE OptimizationCustomerProcessing
ADD CONSTRAINT UK_OptimizationProcessing_Session_Customer
UNIQUE (SessionId, CustomerId, AMOPCustomerId);
```

#### Fix 3: Fix Controller Duplication Logic
```csharp
// In OptimizationController.EnqueueAllCustomersOptimizationAsync
private async Task<string> EnqueueAllCustomersOptimizationAsync(...)
{
    var optimizationSessionRepository = new OptimizationSessionRepository(altaWrxDb);
    var processedCustomers = new HashSet<string>(); // Track processed customers
    
    if (siteType == SiteType.Rev)
    {
        for (var i = 0; i < customers.Count; i++)
        {
            var customer = customers[i];
            var customerKey = $"REV_{customer.AmopCustomerId}";
            
            if (processedCustomers.Contains(customerKey))
            {
                Log.Warn($"Skipping duplicate customer {customer.RevCustomerId} for session {optimizationSessionId}");
                continue;
            }
            processedCustomers.Add(customerKey);
            
            // Check if already exists in database
            if (!optimizationSessionRepository.CustomerAlreadyQueued(optimizationSessionId, customer.AmopCustomerId, null))
            {
                optimizationSessionRepository.AddOptimizationInstanceToBeProcessed(optimizationSessionId, customer.AmopCustomerId, null);
                
                var optCus = new OptimizationCustomerProcessing() { ... };
                altaWrxDb.OptimizationCustomerProcessings.Add(optCus);
            }
        }
    }
    
    // Similar logic for AMOP customers...
}
```

#### Fix 4: Fix UI Query Deduplication
```csharp
// In OptimizationListModel constructor
// Add DISTINCT to prevent duplicate display
public OptimizationListModel(...)
{
    var query = @"
        SELECT DISTINCT 
            oi.Id,
            oi.OptimizationSessionId,
            os.SessionId,
            oi.RunStatusId,
            oi.RunStartTime,
            oi.RunEndTime,
            -- other fields
        FROM OptimizationInstance oi
        INNER JOIN OptimizationSession os ON oi.OptimizationSessionId = os.Id
        LEFT JOIN OptimizationCustomerProcessing ocp ON os.Id = ocp.SessionId
        WHERE oi.TenantId = @tenantId
        ORDER BY oi.RunStartTime DESC";
    
    // Execute query with proper deduplication
}
```

#### Fix 5: Add SQS Message Deduplication
```csharp
// In OptimizationController.EnqueueCustomerOptimizationSqsAsync
private static async Task<string> EnqueueCustomerOptimizationSqsAsync(...)
{
    var messageGroupId = $"optimization_{optimizationSessionId}_{revCustId ?? AMOPCustomerId?.ToString()}";
    var messageDeduplicationId = $"{messageGroupId}_{DateTime.UtcNow.Ticks}";
    
    var request = new SendMessageRequest
    {
        MessageAttributes = messageAttributes,
        MessageBody = requestMsgBody,
        QueueUrl = queueUrl,
        DelaySeconds = delaySeconds,
        MessageGroupId = messageGroupId, // For FIFO queues
        MessageDeduplicationId = messageDeduplicationId // Prevent exact duplicates
    };
    
    // Rest of implementation...
}
```

---

## Summary of Fixes

### Issue 1 - Sessions Not Created:
1. **Improve error propagation** from session creation
2. **Add transaction safety** with rollback cleanup
3. **Validate session existence** in Lambda before processing
4. **Better logging** with specific error details

### Issue 2 - Sessions Stuck in Progress:
1. **Add fallback status updates** when cleanup fails
2. **Fix retry exhaustion** to mark failed instead of stuck
3. **Implement watchdog process** to clean stuck sessions
4. **Update customer processing status** in Lambda

### Issue 3 - Duplicate Entries:
1. **Add deduplication checks** in Lambda processing
2. **Implement database constraints** to prevent duplicates
3. **Fix controller logic** to avoid duplicate tracking records
4. **Add DISTINCT clauses** in UI queries
5. **Use SQS message deduplication** features

### Additional Recommendations:

1. **Add comprehensive monitoring** for session lifecycle
2. **Implement circuit breaker pattern** for external service calls
3. **Add retry policies** with exponential backoff
4. **Create dashboard** for real-time optimization status tracking
5. **Add alerting** for stuck or failed optimizations