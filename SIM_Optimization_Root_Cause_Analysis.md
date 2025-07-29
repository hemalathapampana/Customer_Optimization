# SIM Optimization Root Cause Analysis & Fix

## Issue Summary
**Date**: July 24-25, 2024  
**Provider**: AT&T Telegence  
**Problem**: Large SIM optimization stuck in progress for hours, received 4 optimization summary emails on 7/24 at 8:50 AM, optimization disappeared from session list on 7/25

## Root Cause Analysis

### 1. 🔴 **Redis Cache Connection Issues**

**Evidence Found:**
- `AltaworxSimCardCostOptimizer.cs:54-61` - Redis connectivity testing
- `AltaworxSimCardCostQueueCustomerOptimization.cs:777-779` - Cache checks that send error emails
- `AltaworxSimCardCostOptimizer.cs:117` - Process termination when cache connection invalid

```csharp
if (!context.IsRedisConnectionStringValid)
{
    LogInfo(context, "EXCEPTION", $"No cache connection string is setup. Stopping process.");
    return;
}
```

**Root Cause:** When Redis cache is configured but unreachable, the system either:
- Terminates processing for chaining operations (hard failure)
- Continues without cache, losing optimization state between lambda invocations
- Fails to restore partial optimization state, causing sessions to appear stuck

### 2. ⚠️ **Cross-Provider Optimization State Management Issues**

**Evidence Found:**
- `AltaworxSimCardCostOptimizer.cs:296-300` - Cross-provider handling
- `AltaworxSimCardCostQueueCustomerOptimization.cs:205-250` - Cross-provider session management
- Heavy Redis cache dependency for cross-provider optimizations

```csharp
else if (portalType == PortalTypes.CrossProvider)
{
    return crossProviderOptimizationRepository.GetCrossProviderOptimizationDevices(
        ParameterizedLog(context), instance.Id, billingPeriod, commPlanGroupId, instance.IsCustomerOptimization);
}
```

**Root Cause:** Cross-provider optimizations (like AT&T Telegence) have specific session tracking that depends heavily on Redis cache for state persistence. When cache fails, the session state becomes inconsistent.

### 3. ⏰ **Lambda Timeout and Retry Logic Gaps**

**Evidence Found:**
- `AltaworxSimCardCostOptimizer.cs:246-248` - Remaining time calculation but no timeout handling
- `AltaworxSimCardCostOptimizer.cs:369-378` - Incomplete optimization chaining logic
- `AltaworxSimCardCostOptimizerCleanup.cs:180-186` - Retry limits (10 attempts) but no recovery

```csharp
var remainingSeconds = (int)Math.Floor(context.LambdaContext.RemainingTime.TotalSeconds);
LogInfo(context, "INFO", $"Remaining run time: {remainingSeconds} seconds.");
// No timeout handling logic follows
```

**Root Cause:** Large optimizations that exceed lambda timeout are supposed to chain to new instances, but when Redis is unavailable or fails, the chaining breaks and sessions get stuck in "InProgress" state.

### 4. 📋 **Session List Management Issues**

**Evidence Found:**
- `AltaworxSimCardCostOptimizer.cs:37-40` - Finished statuses: `CleaningUp`, `CompleteWithSuccess`, `CompleteWithErrors`
- `AltaworxSimCardCostOptimizerCleanup.cs:316-318` - Duplicate status checking
- Premature session removal from user view

```csharp
private static readonly List<OptimizationStatus> QUEUE_FINISHED_STATUSES = new List<OptimizationStatus>(){
    OptimizationStatus.CleaningUp,
    OptimizationStatus.CompleteWithSuccess,
    OptimizationStatus.CompleteWithErrors
};
```

**Root Cause:** Sessions disappearing from the list (as experienced on 7/25) indicates the cleanup process is removing sessions that should remain visible until truly complete.

### 5. 📧 **Email Notification Without Session Completion**

**Evidence Found:**
- `AltaworxSimCardCostOptimizerCleanup.cs:195-278` - Email sending logic
- `AltaworxSimCardCostOptimizerCleanup.cs:203-214` - Retry logic for email sending
- `AltaworxSimCardCostOptimizerCleanup.cs:275` - Session cleanup after email success

**Root Cause:** The system sends optimization summary emails (like the 4 emails received on 7/24) based on completion of individual components, not the overall session completion.

## 🔧 Comprehensive Fix Plan

### Fix 1: Redis Cache Dependency Issues

**File**: `AltaworxSimCardCostOptimizer.cs`  
**Location**: Lines 116-120

**Current Code:**
```csharp
if (!context.IsRedisConnectionStringValid)
{
    LogInfo(context, "EXCEPTION", $"No cache connection string is setup. Stopping process.");
    return;
}
```

**Fixed Code:**
```csharp
if (!context.IsRedisConnectionStringValid)
{
    LogInfo(context, "WARNING", $"No cache connection string setup. Proceeding with single-instance processing.");
    // Convert chaining process to single-instance processing
    await ProcessQueues(context, queueIds, messageId, skipLowerCostCheck, chargeType);
    return;
}
```

### Fix 2: Session State Recovery

**File**: `AltaworxSimCardCostOptimizer.cs`  
**Add New Method:**

```csharp
private async Task<bool> TryRecoverStuckSession(KeySysLambdaContext context, List<long> queueIds)
{
    // Check for sessions stuck in InProgress for > 4 hours
    var stuckQueues = queueIds.Where(qId => 
    {
        var queue = GetQueue(context, qId);
        var instance = GetInstance(context, queue.InstanceId);
        return instance.StartTime.HasValue && 
               DateTime.UtcNow.Subtract(instance.StartTime.Value).TotalHours > 4 &&
               !QUEUE_FINISHED_STATUSES.Contains(queue.RunStatusId);
    }).ToList();
    
    if (stuckQueues.Any())
    {
        LogInfo(context, "WARNING", $"Found {stuckQueues.Count} stuck sessions. Attempting recovery.");
        // Reset sessions to allow reprocessing
        foreach (var queueId in stuckQueues)
        {
            ResetStuckQueue(context, queueId);
        }
        return true;
    }
    return false;
}

private void ResetStuckQueue(KeySysLambdaContext context, long queueId)
{
    using (var conn = new SqlConnection(context.ConnectionString))
    {
        using (var cmd = new SqlCommand("UPDATE OptimizationQueue SET RunStatusId = @resetStatus, StartTime = NULL WHERE Id = @queueId", conn))
        {
            cmd.Parameters.AddWithValue("@resetStatus", (int)OptimizationStatus.Queued);
            cmd.Parameters.AddWithValue("@queueId", queueId);
            conn.Open();
            cmd.ExecuteNonQuery();
        }
    }
    LogInfo(context, "INFO", $"Reset stuck queue {queueId} to Queued status");
}
```

### Fix 3: Enhanced Cross-Provider Session Management

**File**: `AltaworxSimCardCostQueueCustomerOptimization.cs`  
**Location**: Lines 775-779

**Current Code:**
```csharp
private async Task CheckRedisCache(KeySysLambdaContext context, long optimizationSessionId, long instanceId)
{
    if (context.IsRedisConnectionStringValid && !IsUsingRedisCache)
    {
        await LogAndSendConfigurationIssueEmailAsync(context, ErrorNotificationEmailReceiver, optimizationSessionId, instanceId);
    }
}
```

**Enhanced Code:**
```csharp
private async Task CheckRedisCache(KeySysLambdaContext context, long optimizationSessionId, long instanceId)
{
    if (context.IsRedisConnectionStringValid && !IsUsingRedisCache)
    {
        await LogAndSendConfigurationIssueEmailAsync(context, ErrorNotificationEmailReceiver, optimizationSessionId, instanceId);
        
        // NEW: Implement graceful degradation instead of hard failure
        LogInfo(context, "WARNING", "Redis cache unavailable. Implementing single-instance processing for cross-provider optimization.");
        
        // Update session to indicate degraded mode
        crossProviderOptimizationRepository.UpdateSessionProcessingMode(ParameterizedLog(context), 
            optimizationSessionId, instanceId, "SINGLE_INSTANCE_MODE");
    }
}
```

### Fix 4: Session Cleanup and Visibility

**File**: `AltaworxSimCardCostOptimizerCleanup.cs`  
**Location**: Lines 299-318

**Enhanced Code:**
```csharp
private void CleanupInstance(KeySysLambdaContext context, long instanceId, bool isCustomerOptimization, bool isLastInstance, int serviceProviderId)
{
    LogInfo(context, "SUB", $"CleanupInstance");
    var instance = GetInstance(context, instanceId);

    if (instance.Id <= 0)
    {
        LogInfo(context, "EXCEPTION", $"Instance with id {instanceId} not found.");
        return;
    }

    // NEW: Check if session should remain visible
    if (ShouldKeepSessionVisible(context, instance, isCustomerOptimization))
    {
        LogInfo(context, "INFO", $"Keeping session {instance.SessionId} visible for user tracking.");
        // Update status to "CompletedButVisible" instead of removing
        UpdateInstanceStatus(context, instanceId, OptimizationStatus.CompleteWithSuccess, keepVisible: true);
        return;
    }

    if (INSTANCE_FINISHED_STATUSES.Contains((OptimizationStatus)instance.RunStatusId))
    {
        LogInfo(context, "WARNING", $"Duplicated instance cleanup request for instance with id {instanceId}.");
        return;
    }
    // ... rest of existing cleanup logic
}

private bool ShouldKeepSessionVisible(KeySysLambdaContext context, OptimizationInstance instance, bool isCustomerOptimization)
{
    // Keep cross-provider optimizations visible for 24 hours after completion
    if (instance.PortalType == PortalTypes.CrossProvider && isCustomerOptimization)
    {
        var hoursSinceCompletion = instance.EndTime.HasValue ? 
            DateTime.UtcNow.Subtract(instance.EndTime.Value).TotalHours : 0;
        return hoursSinceCompletion < 24;
    }
    return false;
}

private void UpdateInstanceStatus(KeySysLambdaContext context, long instanceId, OptimizationStatus status, bool keepVisible = false)
{
    using (var conn = new SqlConnection(context.ConnectionString))
    {
        var sql = keepVisible ? 
            "UPDATE OptimizationInstance SET RunStatusId = @status, EndTime = @endTime, IsVisible = 1 WHERE Id = @instanceId" :
            "UPDATE OptimizationInstance SET RunStatusId = @status, EndTime = @endTime WHERE Id = @instanceId";
            
        using (var cmd = new SqlCommand(sql, conn))
        {
            cmd.Parameters.AddWithValue("@status", (int)status);
            cmd.Parameters.AddWithValue("@endTime", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@instanceId", instanceId);
            conn.Open();
            cmd.ExecuteNonQuery();
        }
    }
}
```

### Fix 5: Lambda Timeout Recovery Mechanism

**File**: `AltaworxSimCardCostOptimizer.cs`  
**Add New Method:**

```csharp
private async Task<bool> HandleLambdaTimeout(KeySysLambdaContext context, List<long> queueIds, 
    RatePoolAssigner assigner, bool skipLowerCostCheck, OptimizationChargeType chargeType)
{
    var remainingSeconds = (int)Math.Floor(context.LambdaContext.RemainingTime.TotalSeconds);
    
    // If less than 30 seconds remaining and not completed
    if (remainingSeconds < 30 && !assigner.IsCompleted)
    {
        LogInfo(context, "WARNING", $"Lambda timeout approaching with {remainingSeconds}s remaining. Saving state and chaining.");
        
        if (context.IsRedisConnectionStringValid && IsUsingRedisCache)
        {
            // Save current state
            var remainingQueueIds = RedisCacheHelper.RecordPartialAssignerToCache(context, assigner);
            if (remainingQueueIds != null && remainingQueueIds.Count > 0)
            {
                await EnqueueOptimizationContinueProcessAsync(context, remainingQueueIds, chargeType, skipLowerCostCheck);
                return true;
            }
        }
        else
        {
            // Fallback: Mark as needs retry and schedule cleanup
            LogInfo(context, "ERROR", "Cannot save state due to Redis unavailability. Marking for retry.");
            await MarkSessionForRetry(context, queueIds);
            return true;
        }
    }
    return false;
}

private async Task MarkSessionForRetry(KeySysLambdaContext context, List<long> queueIds)
{
    foreach (var queueId in queueIds)
    {
        using (var conn = new SqlConnection(context.ConnectionString))
        {
            using (var cmd = new SqlCommand(@"
                UPDATE OptimizationQueue 
                SET RunStatusId = @retryStatus, 
                    RetryCount = ISNULL(RetryCount, 0) + 1,
                    LastRetryTime = @retryTime
                WHERE Id = @queueId", conn))
            {
                cmd.Parameters.AddWithValue("@retryStatus", (int)OptimizationStatus.PendingRetry);
                cmd.Parameters.AddWithValue("@retryTime", DateTime.UtcNow);
                cmd.Parameters.AddWithValue("@queueId", queueId);
                conn.Open();
                cmd.ExecuteNonQuery();
            }
        }
    }
    
    // Schedule retry after 5 minutes
    await EnqueueOptimizationWithDelay(context, queueIds, TimeSpan.FromMinutes(5));
}
```

### Fix 6: Session Monitoring and Auto-Recovery

**Create New File**: `AltaworxSimCardCostOptimizerMonitor.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Lambda.Core;
using Altaworx.SimCard.Cost.Optimizer.Core;
using Altaworx.SimCard.Cost.Optimizer.Core.Enumerations;

namespace Altaworx.SimCard.Cost.Optimizer.Monitor
{
    public class Function : AwsFunctionBase
    {
        public async Task MonitorStuckSessions(ILambdaContext context)
        {
            KeySysLambdaContext keysysContext = null;
            try
            {
                keysysContext = BaseFunctionHandler(context);
                InitializeRepositories(context, keysysContext);
                
                // Find sessions stuck for > 4 hours
                var stuckSessions = GetStuckOptimizationSessions(keysysContext);
                
                LogInfo(keysysContext, "INFO", $"Found {stuckSessions.Count} stuck sessions");
                
                foreach (var session in stuckSessions)
                {
                    LogInfo(keysysContext, "WARNING", $"Found stuck session {session.SessionId}. Attempting recovery.");
                    
                    if (session.PortalType == PortalTypes.CrossProvider)
                    {
                        await RecoverCrossProviderSession(keysysContext, session);
                    }
                    else
                    {
                        await RecoverStandardSession(keysysContext, session);
                    }
                }
            }
            catch (Exception ex)
            {
                LogInfo(keysysContext, "EXCEPTION", $"Monitor error: {ex.Message}");
            }
            finally
            {
                CleanUp(keysysContext);
            }
        }
        
        private List<OptimizationInstance> GetStuckOptimizationSessions(KeySysLambdaContext context)
        {
            var stuckSessions = new List<OptimizationInstance>();
            
            using (var conn = new SqlConnection(context.ConnectionString))
            {
                using (var cmd = new SqlCommand(@"
                    SELECT * FROM OptimizationInstance 
                    WHERE RunStatusId NOT IN (@cleaningUp, @completeSuccess, @completeErrors)
                    AND StartTime < @cutoffTime
                    AND (LastActivityTime IS NULL OR LastActivityTime < @cutoffTime)", conn))
                {
                    cmd.Parameters.AddWithValue("@cleaningUp", (int)OptimizationStatus.CleaningUp);
                    cmd.Parameters.AddWithValue("@completeSuccess", (int)OptimizationStatus.CompleteWithSuccess);
                    cmd.Parameters.AddWithValue("@completeErrors", (int)OptimizationStatus.CompleteWithErrors);
                    cmd.Parameters.AddWithValue("@cutoffTime", DateTime.UtcNow.AddHours(-4));
                    
                    conn.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            // Map reader to OptimizationInstance
                            stuckSessions.Add(MapReaderToOptimizationInstance(reader));
                        }
                    }
                }
            }
            
            return stuckSessions;
        }
        
        private async Task RecoverCrossProviderSession(KeySysLambdaContext context, OptimizationInstance session)
        {
            LogInfo(context, "INFO", $"Recovering cross-provider session {session.SessionId}");
            
            // Reset session to queued status
            using (var conn = new SqlConnection(context.ConnectionString))
            {
                using (var cmd = new SqlCommand(@"
                    UPDATE OptimizationInstance 
                    SET RunStatusId = @queuedStatus, 
                        StartTime = NULL,
                        LastActivityTime = @now
                    WHERE Id = @instanceId", conn))
                {
                    cmd.Parameters.AddWithValue("@queuedStatus", (int)OptimizationStatus.Queued);
                    cmd.Parameters.AddWithValue("@now", DateTime.UtcNow);
                    cmd.Parameters.AddWithValue("@instanceId", session.Id);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            
            // Re-queue for processing
            await RequeueOptimizationSession(context, session);
        }
        
        private async Task RecoverStandardSession(KeySysLambdaContext context, OptimizationInstance session)
        {
            LogInfo(context, "INFO", $"Recovering standard session {session.SessionId}");
            
            // Similar recovery logic for standard sessions
            await RecoverCrossProviderSession(context, session);
        }
    }
}
```

## 🚀 Implementation Plan

### Immediate Actions (Priority 1)

1. **Deploy Redis Connection Resilience**
   - Update `AltaworxSimCardCostOptimizer.cs` with graceful degradation
   - Test Redis failure scenarios

2. **Implement Session Recovery**
   - Add `TryRecoverStuckSession` method
   - Deploy session monitoring lambda

3. **Fix Session Visibility**
   - Update cleanup logic to preserve session visibility
   - Add `ShouldKeepSessionVisible` logic

### Short-term Actions (Priority 2)

4. **Enhanced Cross-Provider Logic**
   - Update cross-provider optimization handling
   - Add fallback processing modes

5. **Timeout Handling**
   - Implement `HandleLambdaTimeout` mechanism
   - Add retry logic for failed sessions

### Long-term Actions (Priority 3)

6. **Monitoring Dashboard**
   - Create CloudWatch dashboards for session monitoring
   - Set up alerts for stuck sessions

## ⚙️ Configuration Changes

**Environment Variables to Add:**
```json
{
  "SanityCheckTimeLimit": 300,
  "SessionVisibilityHours": 24,
  "StuckSessionThresholdHours": 4,
  "EnableSessionRecovery": true,
  "RedisFailureGracefulDegradation": true,
  "MaxRetryAttempts": 3,
  "RetryDelayMinutes": 5
}
```

**CloudWatch Events Rule for Monitoring:**
```json
{
  "Rules": [
    {
      "Name": "OptimizationSessionMonitor",
      "ScheduleExpression": "rate(30 minutes)",
      "State": "ENABLED",
      "Targets": [
        {
          "Id": "1",
          "Arn": "arn:aws:lambda:region:account:function:AltaworxSimCardCostOptimizerMonitor"
        }
      ]
    }
  ]
}
```

## 🔍 Testing Strategy

### Test Scenarios

1. **Redis Failure Simulation**
   - Disconnect Redis during optimization
   - Verify graceful degradation

2. **Large Optimization Testing**
   - Test AT&T Telegence with 10,000+ SIMs
   - Verify session chaining works

3. **Timeout Recovery Testing**
   - Force lambda timeout during optimization
   - Verify recovery mechanism

4. **Session Visibility Testing**
   - Complete optimization and verify 24-hour visibility
   - Test session list persistence

## 📊 Success Metrics

- **Zero stuck sessions** for > 4 hours
- **100% session visibility** retention for 24 hours post-completion
- **< 5% optimization failures** due to Redis issues
- **Automatic recovery** of 95%+ stuck sessions
- **Email notifications** only after true completion

## 🎯 Expected Outcomes

After implementing these fixes:

1. **AT&T Telegence optimizations will not get stuck** due to Redis failures
2. **Sessions will remain visible** in the user interface for 24 hours
3. **Automatic recovery** will handle timeout scenarios
4. **Email notifications** will only be sent after true optimization completion
5. **Cross-provider optimizations** will have robust fallback mechanisms

---

**Document Version**: 1.0  
**Last Updated**: December 2024  
**Author**: AI Assistant  
**Review Required**: Development Team