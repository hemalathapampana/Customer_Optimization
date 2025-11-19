## Customer Optimization Flow

### 1. High-Level Flow (Key Functions)
- `ProcessDevicesByCustomerRatePlans` (in `AltaworxSimCardCostMobilityCustomerOptimization.cs`): prepares the SIM list, removes auto-change-disabled plans, and splits the remaining devices on `CustomerRatePoolId` (`simCardsByRatePoolIds = optimizationSimCards.GroupBy(...)`). Each group then routes to either the rate-pool flow or the legacy plan-name flow.
- `ProcessRatePoolGroup` (in `AWSFunctionBase.cs`): handles devices tied to a specific customer rate pool by building pooling-aware rate plan permutations and enqueuing optimizer runs per pool.
- `ProcessPlanNameGroup` (in `AltaworxSimCardCostMobilityCustomerOptimization.cs`): covers devices with no `CustomerRatePoolId` by grouping rate plans purely by name and running the same base assignment + permutation workflow, but without the pool metadata.

### 2. Low-Level Flow (Step-by-Step)

#### `ProcessDevicesByCustomerRatePlans`
- Fetches all candidate SIMs via `GetOptimizationSimCardsByPortalType`, then filters out devices lacking customer rate plan codes when running per-customer (REV/AMOP) jobs.
- Splits rate plans into two tracks: ones with `AutoChangeRatePlan == false` get processed immediately via `ProcessDevicesWithAutoChangeDisabledRatePlans` (after `CheckZeroValueRatePlans` validation). The returned SIM list excludes devices handled in that shortcut flow.
- The highlighted line groups the remaining devices by `CustomerRatePoolId`; each group yields a distinct path:
  - **Has rate pool id** → derive the matching rate plans (`ratePlansForPool`) and call `ProcessRatePoolGroup`.
  - **No rate pool id** → log the fallback, regroup rate plans by plan code where `AutoChangeRatePlan` is enabled, and call `ProcessPlanNameGroup` for each code.
- If any downstream call returns `true` (error condition), the method bubbles that up to stop the optimization instance.

#### `ProcessRatePoolGroup`
- Iterates through the pool’s rate plans grouped by `AllowsSimPooling`, logging the pool identifier and the pooling flag.
- Runs `CheckZeroValueRatePlans`; if any plan has invalid economics (zero overage/charge), the whole instance is stopped early.
- Skips empty SIM groups; otherwise:
  1. Creates a communication plan group (`CreateCommPlanGroup`).
  2. Builds calculated rate plans via `RatePoolCalculator.CalculateMaxAvgUsage`, then materializes pool-aware objects with `RatePoolFactory.CreateRatePools` and `RatePoolCollectionFactory.CreateRatePoolCollection` (providing the `ratePoolId` so permutations stay pool-scoped).
  3. Executes `BaseDeviceAssignment`, which projects usage (`ProjectDataUsage...`), seeds queues, and records a baseline cost for the SIM set.
  4. Persists rate plan metadata by calling `AddCustomerRatePlansToCommPlanGroup` and linking the `ratePoolId` through `optimizationRepository.AddCustomerRatePoolToCommGroup`.
- Only when more than `OptimizationConstant.BaseAssignedDeviceLimit` SIMs were assigned does it pursue permutations:
  - Enforces the configured max/min rate plan bounds (`CustomerOptimizationPoolRatePlanLimit`, `RatePlanMinimumLimit`).
  - Calls `GeneratePermutationQueueRatePlans`, which in turn creates queues per permutation and maps rate plans into those queues.
  - Enqueues the generated communication plan group for optimization via `EnqueueOptimizationRunsAsync` with `skipLowerCostCheck: true` to prioritize customer-specific pooling scenarios.
- If the SIM count is too low, it simply logs that permutations were skipped.

#### `ProcessPlanNameGroup`
- Starts by regrouping the provided `planNameGroup` into subgroups keyed by `AllowsSimPooling`, mirroring the pool flow but without a concrete `ratePoolId`.
- Validates rate plans with `CheckZeroValueRatePlans`; logs and continues if no devices remain to process.
- For each subgroup:
  1. Allocates a fresh communication plan group (`CreateCommPlanGroup`).
  2. Computes rate pool structures through `RatePoolCalculator`, `RatePoolFactory`, and `RatePoolCollectionFactory`.
  3. Runs `BaseDeviceAssignment` to assign devices, persist projections, and capture the base result in the database.
  4. Calls `AddCustomerRatePlansToCommPlanGroup` so permutations know which plans belong to the comm group.
- Applies the same thresholds as the pool flow: if the assigned SIM count exceeds `OptimizationConstant.BaseAssignedDeviceLimit`, it ensures the rate plan count is within `[RatePlanMinimumLimit, RatePlanLimit]`, generates permutations (`GeneratePermutationQueueRatePlans`), and enqueues them via `EnqueueOptimizationRunsAsync`.
- When the device count is below the threshold or too few plans exist, it logs and skips permutation, leaving the base assignment as the final outcome.
