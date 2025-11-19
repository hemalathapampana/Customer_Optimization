## QueueCustomerPlanOptimization (M2M) Flow

### High-Level Flow (Function Order)
- `FunctionHandler` → entry point invoked by AWS Lambda for M2M queue events.
- `ProcessEvent` → validates single-record expectation and routes to record processor.
- `ProcessEventRecord` → parses SQS attributes, decides portal type, and delegates to portal-specific optimization.
- `ProcessCustomerOptimizationByPortalType` → handles M2M portal logic, figures out identifiers, billing context, and dispatches to `ProcessCustomerId` or `ProcessAMOPCustomerId`.
- `RunCrossProviderCustomerOptimization` (via `ProcessCrossProviderCustomerOptimization`) → handles non-M2M portal logic.
- `ProcessCustomerId` / `ProcessAMOPCustomerId` → orchestrate M2M optimization instance lifecycle, rate-plan lookup, device processing, cleanup queueing, and error propagation.
- `ProcessDevicesByCustomerRatePlans` → core optimizer that groups SIMs, runs pooled/non-pooled logic, and triggers permutation workloads (`ProcessPlanNameGroup`, `GeneratePermutationQueueRatePlans`).
- `ProcessNoRatePlanDevices` → records usage/costs for leftover SIMs lacking rate plans.
- `ProcessCrossProviderDevicesByCustomerRatePlans` + `ProcessNoRatePlanCrossProviderDevices` → cross-provider counterparts handling multi-carrier inputs.

### Low-Level Flow (Per Method)

**`FunctionHandler`**
- Creates `KeySysLambdaContext` via `BaseFunctionHandler`, wires repositories, and reads dynamic settings (`QueuesPerInstance`, error email).
- Tests Redis availability; falls back silently if unreachable.
- Awaits `ProcessEvent`, wraps everything in try/catch, logs exceptions, and always calls `CleanUp`.

**`ProcessEvent`**
- Logs entry, enforces that only one SQS message is processed; logs warning if batch >1.
- Hands the single record to `ProcessEventRecord`.

**`ProcessEventRecord`**
- Confirms required SQS attributes (`CustomerType`, etc.) and optional flags (e.g., `IsLastInstance`).
- Builds default `additionalData` payload, determines portal (`PortalTypes`), proration flag, tenant/customer context.
- Routes to `ProcessCustomerOptimizationByPortalType` for M2M traffic or `ProcessCrossProviderCustomerOptimization` otherwise.

**`ProcessCustomerOptimizationByPortalType`**
- Validates customer identifiers and billing period attributes (supports REV and AMOP flows).
- Extracts GUID `CustomerId` or integer `AMOPCustomerId`, determines `serviceProviderId` either from message or DB lookup.
- Uses `optimizationRepository.MarkProcessedOptimizationInstanceTrackingRecord` in `finally` to ensure tracking.
- Calls `ProcessCustomerId` for REV customers or `ProcessAMOPCustomerId` for AMOP-only customers.

**`ProcessCrossProviderCustomerOptimization`**
- Sets portal context to Cross-Provider, validates AMOP customer id, service provider ids, and billing period id.
- Calls `RunCrossProviderCustomerOptimization` and, in `finally`, marks tracking records.

**`GetServiceProviderIdFromBillingPeriod`**
- Simple SQL query to map billing period id to service provider id when not provided in message attributes.

**`ProcessCustomerId`**
- Retrieves REV account number, rate plans, billing periods, and calculates bill-in-advance flags.
- Starts optimization instance (and optionally next billing period), chooses `OptimizationChargeType`, checks Redis health.
- Calls `ProcessDevicesByCustomerRatePlans` for main optimization, enqueues cleanup on success, or logs errors/updates customer optimization on failure (including AMOP2.0 notification).
- Handles bill-in-advance placeholder note and invokes `ProcessNoRatePlanDevices` to record unassigned SIMs.

**`ProcessAMOPCustomerId`**
- Mirrors `ProcessCustomerId` but keyed on numeric AMOP customer id instead of GUID; instance start call includes AMOP id.
- Same charge-type selection, Redis check, device processing, cleanup enqueue, error handling, and no-rate-plan processing logic.

**`ProcessDevicesByCustomerRatePlans`**
- Pulls SIM cards for the billing period, filters out ones lacking plan codes if customer id present.
- Splits rate plans into non-auto-change (pooled) and auto-change groups.
- For non-auto-change plans: validates charge values (`CheckZeroValueRatePlans`), assigns devices via `ProcessDevicesWithAutoChangeDisabledRatePlans`.
- Groups remaining SIMs by `CustomerRatePoolId`; if pool id exists runs `ProcessRatePoolGroup`, otherwise groups by plan code and invokes `ProcessPlanNameGroup`.
- Returns flag signaling whether any error occurred to upstream callers.

**`ProcessPlanNameGroup`**
- Iterates rate plans grouped by `AllowsSimPooling`; validates non-zero pricing parameters.
- Creates communication plan group, calculates rate pools (`RatePoolCalculator`, `RatePoolFactory`, `RatePoolCollectionFactory`), assigns devices, and records rate plans in group table.
- Determines whether to trigger permutation optimizer (`GeneratePermutationQueueRatePlans` + `EnqueueOptimizationRunsAsync`) based on device count and rate plan limits; otherwise logs why permutations skipped.

**`GeneratePermutationQueueRatePlans`**
- Produces all rate-pool sequences via `RatePoolAssigner`, creates queues per sequence, adds ordered rate plans to queues, and persists queue-rate-plan mappings.

**`ProcessNoRatePlanDevices`**
- Retrieves SIMs without customer rate plan codes, creates dedicated comm plan group + queue, projects usage, records default costs via `OptimizationResultDbWriter`, and stops the queue to finalize bookkeeping.

**`RunCrossProviderCustomerOptimization`**
- Loads cross-provider customer metadata, obtains billing periods and rate plans across providers, handles bill-in-advance gating.
- Starts cross-provider instance via repository, determines charge type, checks Redis, and runs `ProcessCrossProviderDevicesByCustomerRatePlans`.
- Enqueues cleanup or raises errors + AMOP2.0 updates analogous to M2M path, and records no-rate-plan devices using cross-provider helpers.

**`CheckRedisCache`**
- Sends configuration email if Redis is configured but unreachable, ensuring ops visibility.

**`ProcessCrossProviderDevicesByCustomerRatePlans`**
- Retrieves SIMs scoped to customer/service providers, filters devices with plan codes, and repeats pooled vs auto-change handling similar to M2M path.
- For auto-change, ensures rate plans align with requested provider list.
- Runs `ProcessRatePoolGroup` or `ProcessPlanNameGroup` accordingly; signals error status upward.

**`ProcessNoRatePlanCrossProviderDevices`**
- Same as `ProcessNoRatePlanDevices` but uses cross-provider repositories and writers to capture orphan SIM costs per billing period.
