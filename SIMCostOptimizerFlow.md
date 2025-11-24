# Lambda SIM Cost Optimizer Flow

The execution path in `AltaworxSimCardCostOptimizer` matches the flow you outlined. Use the table below to jump to the relevant code for each stage.

| Stage | What Happens | Implementation Reference |
| --- | --- | --- |
| **SQS Message** | Lambda entrypoint receives an `SQSEvent`, enforces a single record, and extracts message attributes (`QueueIds`, `SkipLowerCostCheck`, `ChargeType`, chaining flags). | `AltaworxSimCardCostOptimizer.cs` → `Function.Handler`, `ProcessEvent`, `ProcessEventRecord`. |
| **Initialize & Validate** | `BaseFunctionHandler` loads context, env vars, and logging. The handler sanity-checks timeouts, initializes repositories, and tests Redis connectivity (falling back if unreachable). | `AltaworxSimCardCostOptimizer.cs` lines 43‑66 and 54‑65. |
| **Load Data** | `ProcessQueues` pulls each queue/instance, validates statuses, captures billing period + customer metadata, and loads SIM data via Redis cache or DB fallback. | `AltaworxSimCardCostOptimizer.cs` lines 135‑269. |
| **Determine Mode** | Portal type (`M2M`, `Mobility`, `CrossProvider`) and customer optimization flags decide which repositories and groupings (`GetSimCardsByPortalType`, `GetSimCardGroupingByPortalType`) feed the optimizer. | `AltaworxSimCardCostOptimizer.cs` lines 176‑305. |
| **Apply Rate Plan Strategies** | Pulls queue-specific rate plans, records execution order (`RatePlanSequence`), computes average usage, and builds `RatePoolCollection` with pooling and proration flags. | `AltaworxSimCardCostOptimizer.cs` lines 188‑238. |
| **Calculate Costs** | Instantiates `RatePoolAssigner` with portal-specific flags (filtering, pooling) and context clocks. | `AltaworxSimCardCostOptimizer.cs` lines 256‑263. |
| **Optimize & Reassign** | `assigner.AssignSimCards` runs up to four strategy permutations (grouping/order) unless Redis chaining breaks early; `AssignSimCardsContinue` resumes chained work. | `AltaworxSimCardCostOptimizer.cs` lines 250‑267 and 308‑362. |
| **Redis Checkpointing (if needed)** | `WrapUpCurrentInstance` persists the assigner to Redis and re-queues unfinished work via `EnqueueOptimizationContinueProcessAsync`. | `AltaworxSimCardCostOptimizer.cs` lines 365‑379; `AWSFunctionBase.cs` lines 2368‑2374. |
| **Save Results to DB** | When optimization succeeds, `RecordResults` writes queue summaries and per-SIM assignments via `OptimizationResultDbWriter`, covering both AMOP customer IDs and Rev account numbers. | `AltaworxSimCardCostOptimizer.cs` lines 387‑399; `OptimizationResultDbWriter.cs` lines 16‑40, 92‑205. |
| **Trigger Notifications (RatePlanChange / Cleanup / Email)** | After queues finish, the cleanup lambda (`AltaworxSimCardCostOptimizerCleanup`) monitors completed instances, queues rate-plan changes (`QueueRatePlanUpdates`), and sends go/no-go or configuration emails via `SendGoForRatePlanUpdatesEmail`, `SendNoGo…`, and `LogAndSendConfigurationIssueEmailAsync`. | `AltaworxSimCardCostOptimizerCleanup.cs` lines 401‑419, 1634‑1695; `AWSFunctionBase.cs` lines 2690‑2701. |

## Key Code References

```43:132:AltaworxSimCardCostOptimizer.cs
        public async Task Handler(SQSEvent sqsEvent, ILambdaContext context)
        {
            KeySysLambdaContext keysysContext = null;
            try
            {
                keysysContext = BaseFunctionHandler(context);
                if (SanityCheckTimeLimit == 0)
                {
                    SanityCheckTimeLimit = DEFAULT_SANITY_CHECK_TIME_LIMIT;
                }

                IsUsingRedisCache = keysysContext.TestRedisConnection();
                if (keysysContext.IsRedisConnectionStringValid && !IsUsingRedisCache)
                {
                    var errorMessage = "Redis cache is configured but not reachable. Proceeding without cache.";
                    LogInfo(keysysContext, "EXCEPTION", errorMessage);
                }

                InitializeRepositories(context, keysysContext);
                await ProcessEvent(keysysContext, sqsEvent);
```

```365:407:AltaworxSimCardCostOptimizer.cs
        private async Task WrapUpCurrentInstance(KeySysLambdaContext context, List<long> queueIds, bool skipLowerCostCheck, OptimizationChargeType chargeType, int? amopCustomerId, string accountNumber, long commPlanGroupId, RatePoolAssigner assigner)
        {
            if (!assigner.IsCompleted && context.IsRedisConnectionStringValid && IsUsingRedisCache)
            {
                var remainingQueueIds = RedisCacheHelper.RecordPartialAssignerToCache(context, assigner);
                if (remainingQueueIds != null && remainingQueueIds.Count > 0)
                {
                    await EnqueueOptimizationContinueProcessAsync(context, remainingQueueIds, chargeType, skipLowerCostCheck);
                }
            }
            else
            {
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
```

```16:40:OptimizationResultDbWriter.cs
        public static List<LogMessage> RecordResults(KeySysLambdaContext context, string connectionString, long queueId, string revAccountNumber, OptimizationResult optimizationResult)
        {
            context.LogInfo("SUB", $"RecordResults(,,{queueId},{revAccountNumber},[OptimizationResult])");
            List<LogMessage> results = RecordRatePoolAssignments(context, connectionString, queueId, revAccountNumber, optimizationResult);
            RecordTotalCost(context, connectionString, queueId, optimizationResult);
            return results;
        }

        public static List<LogMessage> RecordResults(KeySysLambdaContext context, string connectionString, long queueId, int amopCustomerId, OptimizationResult optimizationResult)
        {
            context.LogInfo("SUB", $"RecordResults(,,{queueId},amopCustomerId:{amopCustomerId},[OptimizationResult])");
            List<LogMessage> results = RecordRatePoolAssignments(context, connectionString, queueId, amopCustomerId, optimizationResult);
            RecordTotalCost(context, connectionString, queueId, optimizationResult);
            return results;
        }
```

```1737:1760:AWSFunctionBase.cs
        public void StopQueue(KeySysLambdaContext context, long queueId, bool isSuccess = true)
        {
            LogInfo(context, "SUB", $"StopQueue({queueId})");
            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            sqlRetryPolicy.Execute(() =>
            {
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("UPDATE OptimizationQueue SET RunStatusId = @runStatusId, RunEndTime = GETUTCDATE() WHERE Id = @id", conn))
                    {
                        cmd.Parameters.AddWithValue("@id", queueId);
                        cmd.Parameters.AddWithValue("@runStatusId", isSuccess ? OptimizationStatus.CompleteWithSuccess : OptimizationStatus.CompleteWithErrors);
                        conn.Open();
                        cmd.ExecuteNonQuery();
                        conn.Close();
                    }
                }
            });
        }
```

```401:419:AltaworxSimCardCostOptimizerCleanup.cs
                if (DoesHaveTimeToProcessRatePlanUpdates(instance, ratePlansToUpdateCount, connectionString,
                    logger, billingTimeZone, currentSystemTimeUtc))
                {
                    QueueRatePlanUpdates(context, instance.Id, instance.TenantId);
                    SendGoForRatePlanUpdatesEmail(context, instance, billingTimeZone);
                }
                else
                {
                    SendNoGoForRatePlanUpdatesEmail(context, instance, billingTimeZone);
                }
```

## Final Output & Monitoring

- **Optimization summary stored:** `OptimizationResultDbWriter.RecordResults` persists both queue-level totals and per-SIM assignments to staging tables for downstream reporting.
- **Queue marked completed:** `StopQueue` updates `OptimizationQueue.RunStatusId` and timestamps, which downstream jobs (cleanup lambda, dashboards) monitor.
- **Logs for monitoring:** Every stage routes through `LogInfo`, providing SUB/INFO/EXCEPTION events tied to the `KeySysLambdaContext`, so CloudWatch/ELK dashboards can track queue progress.

Refer to this file whenever you need to review the full lambda flow end-to-end.
