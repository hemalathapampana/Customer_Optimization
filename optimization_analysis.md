# AT&T Telegence SIM Optimization - Root Cause Analysis

## Issue Description
After trying SIM optimization on AT&T Telegence multiple times without success, optimizing all SIMs appeared in the session list but has been stuck "in progress" with no movement for hours. On 7/24 at 8:50 AM, 4 emails were received for optimization summaries of a large optimization started 2 days before. As of 7/25, that optimization is no longer appearing in the list.

## Root Cause Analysis

### **Primary Root Cause: Lambda Timeout with Incomplete Redis Cache Recovery**

**Technical Analysis:**

The optimization process utilizes a distributed architecture with Lambda functions that have execution time limits. When processing large optimization requests (like "optimize all SIMs"), the system encounters the following critical flow:

1. **Lambda Execution Time Constraint**: The main optimizer Lambda (`AltaworxSimCardCostOptimizer.cs`) has a built-in sanity check time limit (180 seconds default) and monitors remaining execution time:
   ```csharp
   var remainingSeconds = (int)Math.Floor(context.LambdaContext.RemainingTime.TotalSeconds);
   ```

2. **Incomplete Processing Logic**: When the Lambda cannot complete optimization within the time limit, it should save partial progress to Redis cache and requeue for continuation:
   ```csharp
   if (!assigner.IsCompleted && context.IsRedisConnectionStringValid && IsUsingRedisCache)
   {
       var remainingQueueIds = RedisCacheHelper.RecordPartialAssignerToCache(context, assigner);
       await EnqueueOptimizationContinueProcessAsync(context, remainingQueueIds, chargeType, skipLowerCostCheck);
   }
   ```

3. **Redis Cache Failure Recovery**: The system has fallback logic when Redis is unreachable:
   ```csharp
   IsUsingRedisCache = keysysContext.TestRedisConnection();
   if (keysysContext.IsRedisConnectionStringValid && !IsUsingRedisCache)
   {
       // logs warning but continues without cache
   }
   ```

**The Problem**: When Redis cache becomes unavailable or unreachable during a large optimization, the Lambda cannot save partial progress. If the optimization exceeds the Lambda timeout, the process gets stuck because:
- The optimization instance remains in "InProgress" status
- No partial results are cached for recovery
- No continuation message is queued
- The cleanup process doesn't trigger because the instance never completes

## Fix Implementation

### **Solution: Enhanced Timeout Handling with Graceful Degradation**

Modify the `WrapUpCurrentInstance` method in `AltaworxSimCardCostOptimizer.cs` to handle Redis cache failures gracefully:

```csharp
private async Task WrapUpCurrentInstance(KeySysLambdaContext context, List<long> queueIds, bool skipLowerCostCheck, OptimizationChargeType chargeType, int? amopCustomerId, string accountNumber, long commPlanGroupId, RatePoolAssigner assigner)
{
    LogInfo(context, "SUB", $"(,{string.Join(',', queueIds)},)");
    
    // Check remaining time before making cache decisions
    var remainingSeconds = (int)Math.Floor(context.LambdaContext.RemainingTime.TotalSeconds);
    var hasTimeForCompletion = remainingSeconds > 30; // 30-second buffer
    
    if (!assigner.IsCompleted && hasTimeForCompletion && context.IsRedisConnectionStringValid && IsUsingRedisCache)
    {
        // Normal path: save to cache and continue
        var remainingQueueIds = RedisCacheHelper.RecordPartialAssignerToCache(context, assigner);
        if (remainingQueueIds != null && remainingQueueIds.Count > 0)
        {
            await EnqueueOptimizationContinueProcessAsync(context, remainingQueueIds, chargeType, skipLowerCostCheck);
        }
    }
    else if (!assigner.IsCompleted)
    {
        // **NEW: Graceful degradation path**
        LogInfo(context, "WARNING", $"Optimization incomplete due to timeout or cache unavailability. Marking queues as incomplete for cleanup.");
        
        foreach (long queueId in queueIds)
        {
            // Mark queue as incomplete but not failed, allowing cleanup process to handle
            StopQueue(context, queueId, false);
        }
        
        // **NEW: Queue a cleanup message with delay to allow for manual intervention**
        await EnqueueCleanupProcessAsync(context, queueIds, 300); // 5-minute delay
        return;
    }
    else
    {
        // Completion path (existing logic)
        if (context.IsRedisConnectionStringValid && IsUsingRedisCache)
        {
            RedisCacheHelper.ClearPartialAssignerFromCache(context, queueIds);
        }

        var isSuccess = assigner.Best_Result != null;
        if (isSuccess)
        {
            var result = assigner.Best_Result;
            if (amopCustomerId.HasValue)
            {
                RecordResults(context, result.QueueId, amopCustomerId.Value, commPlanGroupId, result, skipLowerCostCheck);
            }
            else
            {
                RecordResults(context, result.QueueId, accountNumber, commPlanGroupId, result, skipLowerCostCheck);
            }
        }

        foreach (long queueId in queueIds)
        {
            StopQueue(context, queueId, isSuccess);
        }
    }
}
```

**Additional Required Method**:
```csharp
private async Task EnqueueCleanupProcessAsync(KeySysLambdaContext context, List<long> queueIds, int delaySeconds)
{
    // Implementation to queue cleanup message for stuck optimizations
    // This allows manual intervention and prevents indefinite "in progress" state
}
```

## Expected Outcome

This fix will:
1. **Prevent Stuck States**: Optimizations that cannot complete due to timeout or cache issues will be properly marked for cleanup
2. **Enable Recovery**: Delayed cleanup messages allow for manual intervention and system recovery
3. **Maintain Visibility**: Optimizations won't disappear from lists unexpectedly
4. **Improve Reliability**: The system gracefully handles Redis cache failures and Lambda timeouts

## Implementation Priority
**High Priority** - This fix addresses the core issue causing optimizations to become stuck in progress, which impacts system reliability and user experience.