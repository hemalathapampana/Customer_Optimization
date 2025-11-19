## Queue Customer Optimization Flow

This summary follows the runtime order highlighted by the user and records the internal helper methods each stage invokes inside `AltaworxSimCardCostQueueCustomerOptimization.Function`.

### FunctionHandler
- Entry: invoked by AWS Lambda (`SQSEvent`, `ILambdaContext`).
- Internal calls: `BaseFunctionHandler`, `InitializeRepositories`, `KeySysLambdaContext.TestRedisConnection`, `ProcessEvent`, `CleanUp`.

### ProcessEvent
- Validates the single-record expectation for each Lambda trigger.
- Internal calls: `LogInfo`, `ProcessEventRecord`.

### ProcessEventRecord
- Parses SQS attributes, determines portal type, and dispatches.
- Internal calls: `LogInfo`, `ProcessCustomerOptimizationByPortalType`, `ProcessCrossProviderCustomerOptimization`.

### ProcessCustomerOptimizationByPortalType
- Handles M2M portal records, extracts identifiers, billing context, and routes to the right customer handler.
- Internal calls: `LogInfo`, `GetServiceProviderId`, `GetServiceProviderIdFromBillingPeriod`, `ProcessCustomerId`, `ProcessAMOPCustomerId`, `optimizationRepository.MarkProcessedOptimizationInstanceTrackingRecord`.

### ProcessCrossProviderCustomerOptimization
- Handles non-M2M portal records (Cross-Provider mode).
- Internal calls: `LogInfo`, `SetPortalType`, `LogVariableValue`, `ProcessCrossProviderCustomerOptimization` (current scope), `RunCrossProviderCustomerOptimization`, `optimizationRepository.MarkProcessedOptimizationInstanceTrackingRecord`.

### ProcessCustomerId
- Runs the M2M optimization lifecycle for Rev/M2M customers keyed by GUID.
- Internal calls: `LogInfo`, `GetRevAccountNumber`, `GetCustomerRatePlans`, `GetBillingPeriod`, `GetNextBillingPeriod`, `StartOptimizationInstanceWithBillingPeriod`, `StartOptimizationInstance`, `LogAndSendConfigurationIssueEmailAsync`, `ProcessDevicesByCustomerRatePlans`, `EnqueueCleanup`, `UpdateCustomerOptimization`, `StopOptimizationInstance`, `OptimizationAmopApiTrigger.SendResponseToAMOP20`, `ProcessNoRatePlanDevices`.

### ProcessAMOPCustomerId
- Variant of the customer orchestration for AMOP integer identifiers.
- Internal calls: `LogInfo`, `GetCustomerRatePlans`, `GetBillingPeriod`, `GetNextBillingPeriod`, `StartOptimizationInstanceWithBillingPeriod`, `StartOptimizationInstance`, `LogAndSendConfigurationIssueEmailAsync`, `ProcessDevicesByCustomerRatePlans`, `EnqueueCleanup`, `UpdateCustomerOptimization`, `StopOptimizationInstance`, `OptimizationAmopApiTrigger.SendResponseToAMOP20`, `ProcessNoRatePlanDevices`.

### ProcessDevicesByCustomerRatePlans
- Core optimizer that groups SIMs by pooling context and triggers pooled vs. auto-change flows.
- Internal calls: `GetOptimizationSimCards`, `CheckZeroValueRatePlans`, `ProcessDevicesWithAutoChangeDisabledRatePlans`, `ProcessRatePoolGroup`, `ProcessPlanNameGroup`.

### ProcessPlanNameGroup
- Handles auto-change logic per plan name and SIM pooling flag.
- Internal calls: `LogInfo`, `CreateCommPlanGroup`, `RatePoolCalculator.CalculateMaxAvgUsage`, `RatePoolFactory.CreateRatePools`, `RatePoolCollectionFactory.CreateRatePoolCollection`, `BaseDeviceAssignment`, `AddCustomerRatePlansToCommPlanGroup`, `GeneratePermutationQueueRatePlans`, `EnqueueOptimizationRunsAsync`.

### GeneratePermutationQueueRatePlans
- Builds queue records for permutation workloads.
- Internal calls: `LogInfo`, `RatePoolAssigner.GenerateRatePoolSequences`, `CreateQueue`, `AddRatePlansToQueue`, `CreateQueueRatePlans`.

### ProcessNoRatePlanDevices
- Records cost/usage for SIMs lacking rate plans to keep analytics consistent.
- Internal calls: `GetOptimizationSimCards`, `CreateCommPlanGroup`, `CreateQueue`, `StartQueue`, `ProjectDataUsageAndSaveDevices`, `OptimizationResultDbWriter.RecordRatePool`, `OptimizationResultDbWriter.RecordTotalCost`, `StopQueue`.

### RunCrossProviderCustomerOptimization
- Cross-provider orchestration bridging customer metadata, rate plans, and device workflows.
- Internal calls: `LogInfo`, `crossProviderOptimizationRepository.GetOptimizationCustomer`, `crossProviderOptimizationRepository.GetBillingPeriod`, `customerRatePlanRepository.GetCrossProviderCustomerRatePlans`, `crossProviderOptimizationRepository.StartCrossProviderOptimizationInstance`, `GetChargeType`, `CheckRedisCache`, `ProcessCrossProviderDevicesByCustomerRatePlans`, `EnqueueCleanup`, `crossProviderOptimizationRepository.UpdateProcessingCustomerOptimizationInstance`, `StopOptimizationInstance`, `OptimizationAmopApiTrigger.SendResponseToAMOP20`, `ProcessNoRatePlanCrossProviderDevices`.

### ProcessCrossProviderDevicesByCustomerRatePlans
- Cross-provider equivalent of the core optimizer (multicarrier inputs).
- Internal calls: `crossProviderOptimizationRepository.GetCrossProviderCustomerSimCards`, `CheckZeroValueRatePlans`, `ProcessDevicesWithAutoChangeDisabledRatePlans`, `ProcessRatePoolGroup`, `ProcessPlanNameGroup`.

### ProcessNoRatePlanCrossProviderDevices
- Handles cross-provider SIMs without rate plans.
- Internal calls: `crossProviderOptimizationRepository.GetCrossProviderCustomerSimCards`, `CreateCommPlanGroup`, `CreateQueue`, `StartQueue`, `ProjectDataUsageAndSaveDevices`, `OptimizationResultDbWriter.RecordCrossProviderRatePool`, `OptimizationResultDbWriter.RecordTotalCost`, `StopQueue`.
