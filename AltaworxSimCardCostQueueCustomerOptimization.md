## Altaworx SimCard Cost Queue Customer Optimization – Internal Flow

This document captures the method-by-method flow for the `AltaworxSimCardCostQueueCustomerOptimization` Lambda. It focuses on the M2M portal execution path and highlights how supporting behaviors from `AWSFunctionBase` and `OptimizationResultDbWriter` plug into each step.

---

### FunctionHandler
- Calls `BaseFunctionHandler` (from `AWSFunctionBase`) to create `KeySysLambdaContext`, wiring DB connections, logging, config, and repository dependencies.
- Runs `InitializeRepositories` to ensure service provider, rate plan, customer rate plan, and cross-provider repositories are ready.
- Sets fallback queue count (`DEFAULT_QUEUES_PER_INSTANCE`) and pulls `ErrorNotificationEmailReceiver` from Lambda environment if not set.
- Uses `KeySysLambdaContext.TestRedisConnection()` to determine if Redis can be used; failures flip `IsUsingRedisCache=false` but do not terminate the run.
- Invokes `ProcessEvent`; guarantees `CleanUp` executes even when exceptions occur.

### ProcessEvent
- Writes diagnostic log (`SUB ProcessEvent`).
- Validates exactly one SQS record per Lambda invocation; warns and exits otherwise to keep customer runs isolated.
- Forwards the single record to `ProcessEventRecord`.

### ProcessEventRecord
- Confirms the presence of `CustomerType` message attribute; logs and returns if missing.
- Extracts `TenantId`, `CustomerType`, `OptimizationSessionId`, `IsLastInstance`, defaulted `PortalType`, and builds AMOP 2.0 `additionalData` (used for later error callbacks).
- Determines whether proration applies (always false for M2M carrier optimization).
- Routes to:
  - `ProcessCustomerOptimizationByPortalType` when `PortalType == M2M`.
  - `ProcessCrossProviderCustomerOptimization` otherwise.

### ProcessCustomerOptimizationByPortalType
- Validates that either `CustomerId` (GUID) or `AMOPCustomerId` exists, plus billing period info (`BillPeriodId` or `BillYear`/`BillMonth` pair).
- Parses Rev GUID, AMOP id, and billing period, ensuring Rev customers never use empty GUIDs.
- Calls local helper `GetServiceProviderIdFromBillingPeriod` when `ServiceProviderId` is absent in the SQS attributes.
- Executes:
  - `ProcessCustomerId` for Rev (`SiteTypes.Rev`) customers, requiring `IntegrationAuthenticationId`.
  - `ProcessAMOPCustomerId` for AMOP customers.
- Always invokes `optimizationRepository.MarkProcessedOptimizationInstanceTrackingRecord` (from base class) in a `finally` block with whichever identity was used.

### ProcessCrossProviderCustomerOptimization
- Switches portal type to `PortalTypes.CrossProvider`.
- Reads AMOP customer id, optional service provider filter list, and customer billing period id; logs warnings if any are missing.
- Calls `RunCrossProviderCustomerOptimization`.
- Ensures `optimizationRepository.MarkProcessedOptimizationInstanceTrackingRecord` executes with cross-provider identifiers.

### GetServiceProviderIdFromBillingPeriod
- Runs a direct SQL query (`SELECT ServiceProviderId FROM BillingPeriod WHERE Id = @billingPeriodId`) to recover the service provider when the upstream payload is incomplete.

### ProcessCustomerId (Rev path)
1. **Data gathering**
   - `GetRevAccountNumber` (base) retrieves the Rev billing account number.
   - `GetCustomerRatePlans` (base) loads rate plans scoped by tenant, service provider, and billing period.
2. **Instance setup**
   - Evaluates bill-in-advance eligibility (currently forced `false` per PORT-166).
   - Pulls `BillingPeriod`/`NextBillingPeriod` via base helpers; aborts if future period missing when bill-in-advance would be needed.
   - Starts an optimization instance using `StartOptimizationInstanceWithBillingPeriod`.
   - Selects `OptimizationChargeType` (`RateChargeAndOverage` or `OverageOnly`) based on bill-in-advance.
   - Sends a configuration email (`LogAndSendConfigurationIssueEmailAsync`) if Redis connection string is valid but actual connection failed during `FunctionHandler`.
3. **Device processing**
   - Calls `ProcessDevicesByCustomerRatePlans`.
   - On success → `EnqueueCleanup` with a slight delay to let small runs finish faster.
   - On failure → `UpdateCustomerOptimization`, `StopOptimizationInstance`, and `OptimizationAmopApiTrigger.SendResponseToAMOP20` with error details.
4. **No-rate-plan devices**
   - Invokes `ProcessNoRatePlanDevices`, which uses `OptimizationResultDbWriter.RecordRatePool` and `RecordTotalCost` to store zero-cost queues for SIMs missing assignments.

### ProcessAMOPCustomerId (AMOP path)
- Mirrors `ProcessCustomerId` but:
  - Works with an integer `AMOPCustomerId` instead of a Rev GUID.
  - Starts the optimization instance with AMOP-specific parameters.
  - Calls `UpdateCustomerOptimization` and AMOP 2.0 triggers with AMOP identifiers.
  - Passes AMOP context into `ProcessNoRatePlanDevices`.

### ProcessDevicesByCustomerRatePlans
1. **Fetch SIM inventory**
   - Uses `GetOptimizationSimCards` (base) with tenant, provider, billing-period filters; excludes SIMs lacking `CustomerRatePlanCode` for Rev/AMOP targeted runs.
2. **Handle auto-change-disabled rate plans**
   - Splits rate plans where `AutoChangeRatePlan == false`.
   - Runs `CheckZeroValueRatePlans` (base) to ensure overage rates are non-zero; early exits on error.
   - Calls `ProcessDevicesWithAutoChangeDisabledRatePlans` (base) which processes pooled/independent queues and returns the remaining SIMs for algorithmic work.
3. **Process remaining SIMs by pool**
   - Groups leftover SIMs by `CustomerRatePoolId`.
   - For non-null pools → `ProcessRatePoolGroup` (base) builds comm plan groups, creates permutations, and invokes `EnqueueOptimizationRunsAsync`.
   - For null pools → `ProcessPlanNameGroup` (local, below) handles auto-change logic keyed by plan name.

### ProcessPlanNameGroup (auto-change logic)
- Iterates each plan-name group, further splitting by `AllowsSimPooling`.
- Validates that no rate plan in the group has zero data-overage charge or overage rate; aborts the entire flow if invalid.
- Skips groups with no SIMs (likely unassigned).
- Creates a comm plan group via `CreateCommPlanGroup`, calculates base rate-pool permutations using:
  - `RatePoolCalculator.CalculateMaxAvgUsage`
  - `RatePoolFactory.CreateRatePools`
  - `RatePoolCollectionFactory.CreateRatePoolCollection`
- Runs `BaseDeviceAssignment` to assign obvious fits; only proceeds to permutation logic when device count exceeds `OptimizationConstant.BaseAssignedDeviceLimit`.
- Enforces rate-plan limits (`OptimizationConstant.RatePlanLimit` and `RatePlanMinimumLimit`).
- Generates queue permutations (`GeneratePermutationQueueRatePlans`).
- Invokes `EnqueueOptimizationRunsAsync` with `skipLowerCostCheck=true` so every permutation fan-outs immediately.

### GeneratePermutationQueueRatePlans
- Uses `RatePoolAssigner.GenerateRatePoolSequences` to enumerate permutations.
- For each sequence:
  - Creates a queue via `CreateQueue`.
  - Calls `AddRatePlansToQueue` to build queue-rate-plan rows.
  - Collects rows into a `DataTable` and persists them through `CreateQueueRatePlans`.

### ProcessNoRatePlanDevices
- Pulls SIMs with empty `CustomerRatePlanCode` via `GetOptimizationSimCards`.
- Creates a dedicated comm plan group and queue, starts/stops the queue explicitly.
- Runs `ProjectDataUsageAndSaveDevices` to persist simulated usage.
- Writes data with `OptimizationResultDbWriter.RecordRatePool` (M2M version) and zero cost via `OptimizationResultDbWriter.RecordTotalCost`.

### RunCrossProviderCustomerOptimization
- Fetches `OptimizationCustomer` using `crossProviderOptimizationRepository.GetOptimizationCustomer`.
- Retrieves billing period and next billing period through the cross-provider repository.
- Loads rate plans using `customerRatePlanRepository.GetCrossProviderCustomerRatePlans`.
- Starts a cross-provider instance via `crossProviderOptimizationRepository.StartCrossProviderOptimizationInstance`.
- Determines charge type via base helper `GetChargeType(useBillInAdvance)`.
- Reuses Redis check via `CheckRedisCache`.
- Executes `ProcessCrossProviderDevicesByCustomerRatePlans`.
- On success → `EnqueueCleanup`; on failure → `crossProviderOptimizationRepository.UpdateProcessingCustomerOptimizationInstance`, `StopOptimizationInstance`, and AMOP 2.0 error response.
- Calls `ProcessNoRatePlanCrossProviderDevices` to record leftover SIMs.

### CheckRedisCache
- Thin wrapper that sends the configuration issue email (`LogAndSendConfigurationIssueEmailAsync`) when a valid Redis connection string was configured but `IsUsingRedisCache` is false.

### ProcessCrossProviderDevicesByCustomerRatePlans
- Pulls SIM data via `crossProviderOptimizationRepository.GetCrossProviderCustomerSimCards`.
- Filters out SIMs without rate-plan codes just like the M2M path.
- Runs the same staged processing:
  1. Auto-change-disabled plans → `CheckZeroValueRatePlans` and base helper `ProcessDevicesWithAutoChangeDisabledRatePlans`.
  2. Remaining SIMs grouped by pool id:
     - Non-null → `ProcessRatePoolGroup`.
     - Null → `ProcessPlanNameGroup`.
- Adds cross-provider-specific validation ensuring auto-change rate plans declare service provider compatibility (matching the SQS `serviceProviderIds` filter).

### ProcessNoRatePlanCrossProviderDevices
- Mirrors the M2M helper but calls `OptimizationResultDbWriter.RecordCrossProviderRatePool` before `RecordTotalCost`, supplying cross-provider billing period context.

---

## Supporting Behaviors

`AWSFunctionBase` supplies the shared operations consumed above:
- Context bootstrap (`BaseFunctionHandler`, `CleanUp`), repository initialization, and SQL helpers (`GetRevAccountNumber`, `GetCustomerRatePlans`, `GetBillingPeriod`, `GetNextBillingPeriod`, `GetOptimizationSimCards`).
- Optimization lifecycle helpers (`StartOptimizationInstanceWithBillingPeriod`, `StartOptimizationInstance`, `ProcessDevicesWithAutoChangeDisabledRatePlans`, `ProcessRatePoolGroup`, `CreateCommPlanGroup`, `CreateQueue`, `StartQueue`, `StopQueue`, `ProjectDataUsageAndSaveDevices`, `EnqueueCleanup`, `EnqueueOptimizationRunsAsync`, `AddCustomerRatePlansToCommPlanGroup`, `AddRatePlansToQueue`, `CreateQueueRatePlans`, `BaseDeviceAssignment`, `CheckZeroValueRatePlans`).
- Notification utilities (`LogAndSendConfigurationIssueEmailAsync`, `LogInfo`, `LogVariableValue`) used throughout.
- Error-reporting helpers (`UpdateCustomerOptimization`, `StopOptimizationInstance`, `optimizationRepository.MarkProcessed...`).

`OptimizationResultDbWriter` ensures device data is persisted even when the optimizer cannot run:
- `RecordRatePool` and `RecordCrossProviderRatePool` write queue/device totals for SIMs with no rate plans.
- `RecordTotalCost` sets a zero-cost entry so downstream reporting surfaces the unassigned devices.

These shared methods let the Lambda perform Rev, AMOP, and Cross-Provider optimizations using the same queuing, permutation, and persistence pipeline.
