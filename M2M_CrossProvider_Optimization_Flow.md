# Altaworx SimCard Cost Queue Customer Optimization Flow

This document maps the method-by-method execution flow for the **AltaworxSimCardCostQueueCustomerOptimization** Lambda when handling **M2M portal type** optimization and the **Cross-Provider Customer Optimization** path. It cross-references the supporting behaviors implemented in `AWSFunctionBase.cs` and `OptimizationResultDbWriter.cs`.

---

## M2M Portal Type – High-Level Flow

| Order | Method (File) | Responsibility |
| --- | --- | --- |
| 1 | `FunctionHandler` (`AltaworxSimCardCostQueueCustomerOptimization`) | Entry point; bootstraps Lambda context and dispatches SQS messages. |
| 2 | `BaseFunctionHandler` (`AWSFunctionBase`) | Produces `KeySysLambdaContext` (DB connections, logging, env). |
| 3 | `InitializeRepositories` (`AWSFunctionBase`) | Wires repositories (service provider, rate plan, cross-provider). |
| 4 | `ProcessEvent` (`AltaworxSimCardCostQueueCustomerOptimization`) | Validates that only a single SQS record is processed. |
| 5 | `ProcessEventRecord` (`AltaworxSimCardCostQueueCustomerOptimization`) | Extracts SQS attributes, decides portal type, routes work. |
| 6 | `ProcessCustomerOptimizationByPortalType` | Validates customer/billing data and routes to Rev or AMOP handler. |
| 7a | `ProcessCustomerId` | Handles Rev/Keysys GUID customers end-to-end. |
| 7b | `ProcessAMOPCustomerId` | Handles AMOP customer id flows. |
| 8 | `GetRevAccountNumber`, `GetCustomerRatePlans`, `GetBillingPeriod`, `GetNextBillingPeriod` (`AWSFunctionBase`) | Fetch dependent data for both branches. |
| 9 | `StartOptimizationInstanceWithBillingPeriod` / `StartOptimizationInstance` (`AWSFunctionBase`) | Persists OptimizationInstance records plus bill-in-advance metadata. |
| 10 | `ProcessDevicesByCustomerRatePlans` | Performs pooled vs auto-change optimization sequencing. |
| 11 | `ProcessDevicesWithAutoChangeDisabledRatePlans`, `ProcessRatePoolGroup`, `ProcessPlanNameGroup`, `GeneratePermutationQueueRatePlans`, `EnqueueOptimizationRunsAsync` | Detailed rate-pool processing, queue creation, SQS fan-out. |
| 12 | `ProcessNoRatePlanDevices` | Writes unassigned SIM charges using `OptimizationResultDbWriter`. |
| 13 | `EnqueueCleanup` (`AWSFunctionBase`) | Sends cleanup instruction via SQS for instance finalization. |
| 14 | Error paths: `UpdateCustomerOptimization`, `StopOptimizationInstance`, `OptimizationAmopApiTrigger.SendResponseToAMOP20` | Persist errors and notify AMOP 2.0. |

### M2M Detailed Method Notes

#### 1. `FunctionHandler`
- Creates `KeySysLambdaContext` via `BaseFunctionHandler`, wires repositories, enforces default queue counts, tests Redis connectivity, and forwards to `ProcessEvent`. Cleans up context even on exception.

#### 2. `BaseFunctionHandler` (AWSFunctionBase)
- Wraps the raw `ILambdaContext` with database connections, settings, logging configuration, and helper repositories the rest of the flow depends on.

#### 3. `InitializeRepositories` (AWSFunctionBase)
- Instantiates `ServiceProviderRepository`, `CarrierRatePlanRepository`, `CustomerRatePlanRepository`, and `CrossProviderOptimizationRepository` with the Lambda/environment dependencies. This ensures later DB operations (rate plans, billing periods, etc.) are available without reconfiguration.

#### 4. `ProcessEvent`
- Enforces a single SQS record per invocation to avoid interleaving customer runs; logs and exits if batch size is unexpected.

#### 5. `ProcessEventRecord`
- Extracts all SQS attributes (tenant, customer type, session, portal type, service provider data) and selects either M2M (`ProcessCustomerOptimizationByPortalType`) or Cross-Provider (`ProcessCrossProviderCustomerOptimization`). Builds the AMOP 2.0 additional-data payload for error callbacks. Determines Redis proration and portal type flags that downstream components need.

#### 6. `ProcessCustomerOptimizationByPortalType`
- Validates presence of Rev GUID / AMOP customer id and billing period metadata. Retrieves optional service provider id from message or DB. Wraps execution in `optimizationRepository.MarkProcessedOptimizationInstanceTrackingRecord` to guarantee state marking in a `finally`.
- Routes to `ProcessCustomerId` (Rev) if `SiteTypes.Rev`, otherwise to `ProcessAMOPCustomerId`.

#### 7a. `ProcessCustomerId`
- Loads Rev account number, rate plans, and billing period / next billing period via AWSFunctionBase helpers.
- Calculates bill-in-advance eligibility and required future billing period, aborting early if prerequisites fail.
- Starts an optimization instance with `StartOptimizationInstanceWithBillingPeriod`; selects charge type based on bill-in-advance flag.
- Calls `LogAndSendConfigurationIssueEmailAsync` when Redis is configured but unreachable.
- Runs `ProcessDevicesByCustomerRatePlans`. On success, enqueues cleanup with `EnqueueCleanup`; on failure, updates customer optimization record, stops the instance, and notifies AMOP 2.0.
- Always records devices without rate plans through `ProcessNoRatePlanDevices` (which leverages `OptimizationResultDbWriter` to persist queue totals).

#### 7b. `ProcessAMOPCustomerId`
- Mirrors Rev logic but works with AMOP integer identifiers. Starts the optimization instance with AMOP id, runs device processing, logs Redis issues, enqueues cleanup, and handles errors identically. The “no rate plan” path writes results with AMOP id context.

#### 8. Data Retrieval Helpers (AWSFunctionBase)
- `GetRevAccountNumber` fetches the billing identifier for Rev customers.
- `GetCustomerRatePlans` pulls customer rate plans filtered by tenant, service provider, and billing period.
- `GetBillingPeriod` and `GetNextBillingPeriod` resolve the working and bill-in-advance periods. All four rely on retry-wrapped SQL calls.

#### 9. Instance Management (AWSFunctionBase)
- `StartOptimizationInstanceWithBillingPeriod` (and the `StartOptimizationInstance` fallback) persist `OptimizationInstance` rows with run metadata, tenant, portal type, and bill-in-advance state so that device queues can reference a consistent instance id.

#### 10. `ProcessDevicesByCustomerRatePlans`
- Pulls SIM cards via `GetOptimizationSimCards`.
- Separates rate plans into auto-change-disabled vs enabled sets.
- Calls `ProcessDevicesWithAutoChangeDisabledRatePlans` to fully process pooled/independent devices without permutations (also projecting usage and calculating charges).
- Groups remaining SIMs by rate pool id; for each pool, either:
  - Invokes `ProcessRatePoolGroup` (AWSFunctionBase) for pooled plans, creating comm plan groups, calculating permutations, and enqueuing optimization runs.
  - Or, for null pool ids, iterates through plan-name groups and calls `ProcessPlanNameGroup`.
- Terminates early if any group signals an error (e.g., zero-valued rate plans).

#### 11. Rate-Pool / Queue Helpers
- `ProcessPlanNameGroup` (file-local) validates zero-value plans, creates comm plan groups, uses `RatePoolCalculator`, `RatePoolFactory`, and `RatePoolAssigner` to build permutations, writes queue data, and calls `EnqueueOptimizationRunsAsync`.
- `GeneratePermutationQueueRatePlans` creates queue rows per permutation, ensuring the optimizer runs across all combinations.
- `ProcessRatePoolGroup` (AWSFunctionBase) handles pooled customer rate pools by adding rate plans to comm plan groups, enforcing rate-plan limits, generating permutations, and enqueuing queue runs.
- `ProcessDevicesWithAutoChangeDisabledRatePlans` (AWSFunctionBase) handles auto-change-disabled plans in three ordered steps (pooled, independent, bill-in-advance devices) and returns the remaining SIMs for algorithmic optimization.

#### 12. `ProcessNoRatePlanDevices`
- Retrieves leftover SIMs with empty customer rate plans, creates a comm plan group and queue, projects usage, and writes zero-cost assignments via `OptimizationResultDbWriter.RecordRatePool` and `RecordTotalCost`. This guarantees visibility for devices excluded from optimization.

#### 13. `EnqueueCleanup`
- Posts an SQS message to the cleanup queue with instance metadata (flags for customer optimization and last-instance), allowing the cleanup Lambda to close out the optimization instance at the appropriate time.

#### 14. Error Finalization
- `UpdateCustomerOptimization`, `StopOptimizationInstance`, and `OptimizationAmopApiTrigger.SendResponseToAMOP20` ensure that customer-facing state reflects failures and that AMOP 2.0 receives an error payload containing the optimization session id and descriptive message.

---

## Cross-Provider Customer Optimization – High-Level Flow

| Order | Method (File) | Responsibility |
| --- | --- | --- |
| 1 | `ProcessEventRecord` (`AltaworxSimCardCostQueueCustomerOptimization`) | Detects portal type `CrossProvider` and routes accordingly. |
| 2 | `SetPortalType` (`AWSFunctionBase`) | Switches base-class behavior (repositories, rate-pool logic) to Cross-Provider mode. |
| 3 | `ProcessCrossProviderCustomerOptimization` | Extracts AMOP customer id, target service providers, billing period id, then calls `RunCrossProviderCustomerOptimization`. |
| 4 | `RunCrossProviderCustomerOptimization` | Loads customer + billing period, fetches rate plans across providers, starts instance, and coordinates device processing. |
| 5 | `CheckRedisCache` (`AWSFunctionBase`) | Notifies if Redis is configured but unreachable. |
| 6 | `ProcessCrossProviderDevicesByCustomerRatePlans` | Equivalent of `ProcessDevicesByCustomerRatePlans` using cross-provider repositories. |
| 7 | `EnqueueCleanup` | Same cleanup orchestration as M2M. |
| 8 | `ProcessNoRatePlanCrossProviderDevices` | Writes results for SIMs with no rate plan via `OptimizationResultDbWriter.RecordCrossProviderRatePool`. |
| 9 | Error handling (`crossProviderOptimizationRepository.UpdateProcessingCustomerOptimizationInstance`, `StopOptimizationInstance`, AMOP 2.0 trigger). |

### Cross-Provider Detailed Method Notes

#### 1. `ProcessCrossProviderCustomerOptimization`
- Sets portal type to `CrossProvider`, extracts AMOP customer id, service provider list, and customer billing period id from SQS attributes, then calls `RunCrossProviderCustomerOptimization`. Guarantees instance-tracking records are marked in a `finally`.

#### 2. `RunCrossProviderCustomerOptimization`
- Retrieves customer definition via `crossProviderOptimizationRepository.GetOptimizationCustomer`, obtains billing period(s), and pulls eligible rate plans using `customerRatePlanRepository.GetCrossProviderCustomerRatePlans`.
- Determines bill-in-advance viability and loads the next billing period when necessary.
- Starts a cross-provider optimization instance (repository call), selects charge type via `GetChargeType`, and ensures Redis-health email is sent when required via `CheckRedisCache`.
- Calls `ProcessCrossProviderDevicesByCustomerRatePlans`; on success enqueues cleanup, otherwise updates the processing record, stops the instance, and sends AMOP 2.0 errors.
- Always runs `ProcessNoRatePlanCrossProviderDevices` to record devices lacking rate plans.

#### 3. `ProcessCrossProviderDevicesByCustomerRatePlans`
- Loads SIM cards through `crossProviderOptimizationRepository.GetCrossProviderCustomerSimCards` and filters by assigned customer rate plan codes.
- Handles auto-change-disabled plans via `ProcessDevicesWithAutoChangeDisabledRatePlans`.
- Applies service-provider filtering to auto-change-enabled plans to honor multi-provider constraints.
- Groups SIMs by rate pool id; for pooled sets, calls `ProcessRatePoolGroup`; for non-pooled groups, iterates plan-name groupings and calls `ProcessPlanNameGroup`. Errors halt optimization.

#### 4. `ProcessNoRatePlanCrossProviderDevices`
- Fetches SIMs without customer rate plans, builds comm plan groups and queues, projects usage, and persists results through `OptimizationResultDbWriter.RecordCrossProviderRatePool` plus `RecordTotalCost`. Ensures every SIM is represented in downstream reporting.

#### 5. Cross-Provider Error Handling
- Uses `crossProviderOptimizationRepository.UpdateProcessingCustomerOptimizationInstance` to stamp status/error, stops the optimization instance, and publishes AMOP 2.0 error responses with the same additional data blob used elsewhere.

---

## OptimizationResultDbWriter Touchpoints

| Method | Used By | Purpose |
| --- | --- | --- |
| `RecordRatePool` / `RecordTotalCost` (M2M) | `ProcessNoRatePlanDevices` and AWSFunctionBase `RecordResults` | Writes per-device assignments and aggregate cost figures for queues tied to M2M portal type. |
| `RecordCrossProviderRatePool` | `ProcessNoRatePlanCrossProviderDevices`, AWSFunctionBase `RecordResults` | Handles cross-provider pools, splitting by portal type and loading into the correct staging tables. |
| `RecordResults` overloads | AWSFunctionBase result-recording helpers | Bulk insert device assignments (M2M or Cross-Provider) following queue completion, leveraging SQL bulk copy utilities for performance. |

Key behaviors:
- All writers compute total cost breakdowns (base, rate, overage, SMS) before updating `OptimizationQueue`.
- When invoked for cross-provider runs, the writers group SIMs by destination portal type and push each batch to the appropriate staging table (`OptimizationDeviceResultStaging`, `OptimizationMobilityDeviceResultStaging`, or shared-pool tables).
- The helper always logs its actions through `KeySysLambdaContext` to preserve audit trails matching optimizer queues/instances.

---

## Usage Notes

1. **Redis health** – Both M2M and Cross-Provider paths will send configuration issue emails when Redis is configured but unavailable, yet optimization will continue using the database, which can affect performance.
2. **Bill-in-Advance** – The flow explicitly disables bill-in-advance calculations for M2M (per PORT-166) but preserves plumbing (next billing period lookups, charge-type selection) for future reactivation.
3. **Error propagation** – All fatal branches make sure to: update optimization state, stop the instance, enqueue cleanup only when appropriate, and notify AMOP 2.0 with contextual data so UI/API consumers stay in sync.
4. **Result visibility** – Even when SIMs cannot be optimized (missing rate plans or excluded pools), the `OptimizationResultDbWriter` pathways ensure their cost impact and status are persisted, preventing silent data gaps downstream.
