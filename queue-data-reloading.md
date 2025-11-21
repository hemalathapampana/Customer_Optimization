# Queue And Data Reloading Behavior

## 1. Why does Lambda 2 reload customer/device/plan data?
- Lambda 1 (the customer optimization lambda) only stages the customer snapshot while it is grouping rate plans. After it fetches the SIM cards, it writes the projected usage snapshot to `OptimizationDevice` (and, when possible, to Redis) so the downstream optimizer can reread the same snapshot later; once Lambda 1 exits, its in-memory collections vanish.
- Because Lambda 2 (the optimizer lambda) executes much later and on different compute, it must rehydrate that snapshot from a durable source (Redis if populated, otherwise SQL) before it can process the queue message. That is why it appears to “refetch” the same data set.

```2044:2051:AWSFunctionBase.cs
var simCards = ProjectDataUsageAndSaveDeviceByPortalType(context, billingPeriod, instanceId, simList, autoChangeRatePlan: true, commPlanGroupId);
//also save to cache for faster query on optimizer lambda
var isUsingRedisCache = context.TestRedisConnection();
if (isUsingRedisCache)
{
    ProjectDataUsageAndSaveDevicesToCache(context, instanceId, simList, billingPeriod, commPlanGroupId);
}
```

```216:224:AltaworxSimCardCostOptimizer.cs
if (IsUsingRedisCache)
{
    simCards = RedisCacheHelper.GetSimCardsFromCache(context, instance.Id, commPlans, commPlanGroupId,
                                () => GetSimCardsByPortalType(context, instance, queue.ServiceProviderId, billingPeriod, instance.PortalType, commPlanGroupId, commPlans, optimizationGroups));
}
else
{
    simCards = GetSimCardsByPortalType(context, instance, queue.ServiceProviderId, billingPeriod, instance.PortalType, commPlanGroupId, commPlans, optimizationGroups);
}
```

## 2. Functional purpose of the second fetch
- Lambda 1 generates all queue permutations for the customer/device set and persists both the queue metadata and the SIM snapshot; Lambda 2 is the worker that actually runs the heavy optimization permutations for each queue using `RatePoolAssigner`, so it must pull the data set that matches the queue ID it just dequeued.
- This second fetch guarantees that every optimizer run sees the latest staged snapshot, supports SQS at-least-once delivery (a queue may be replayed hours later), and allows Lambda 2 to re-run permutations with multiple grouping strategies until it finds the best cost.

```630:659:AltaworxSimCardCostQueueCustomerOptimization.cs
foreach (var ratePoolSequence in ratePoolSequences)
{
    // add queue for rate plan permutation
    var queueId = CreateQueue(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId, usesProration);

    // add rate plans to queue
    var dtQueueRatePlanTemp = AddRatePlansToQueue(queueId, ratePoolSequence, commGroupRatePlanTable);
    ...
}
CreateQueueRatePlans(context, dtQueueRatePlan);
```

```248:268:AltaworxSimCardCostOptimizer.cs
var assigner = new RatePoolAssigner(string.Empty, ratePoolCollection, simCards, context.logger, SanityCheckTimeLimit, context.LambdaContext, IsUsingRedisCache,
    instance.PortalType,
    shouldFilterByRatePlanType,
    shouldPoolUsageBetweenRatePlans);
assigner.AssignSimCards(GetSimCardGroupingByPortalType(instance.PortalType, instance.IsCustomerOptimization),
                            context.OptimizationSettings.BillingTimeZone,
                            false,
                            false,
                            ratePoolSequences);

await WrapUpCurrentInstance(context, queueIds, skipLowerCostCheck, chargeType, amopCustomerId, accountNumber, commPlanGroupId, assigner);
```

## 3. Why still hit SQL when Redis is available?
- Redis is only used as a best-effort cache. Lambda 1 populates it only if the connection test succeeds; any failure, eviction, or timeout leaves the optimizer with no cached payload, so Lambda 2 must fall back to the authoritative SQL read to stay correct.
- Even when Redis is enabled, `GetSimCardsFromCache` is invoked with a database callback so a cache miss immediately repopulates the cache from SQL before the optimizer proceeds. This design prevents stale or missing data from breaking queue processing.

```2046:2051:AWSFunctionBase.cs
//also save to cache for faster query on optimizer lambda
var isUsingRedisCache = context.TestRedisConnection();
if (isUsingRedisCache)
{
    ProjectDataUsageAndSaveDevicesToCache(context, instanceId, simList, billingPeriod, commPlanGroupId);
}
```

```216:224:AltaworxSimCardCostOptimizer.cs
if (IsUsingRedisCache)
{
    simCards = RedisCacheHelper.GetSimCardsFromCache(...,
                                () => GetSimCardsByPortalType(...));
}
else
{
    simCards = GetSimCardsByPortalType(...);
}
```
