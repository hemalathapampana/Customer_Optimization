# Cross-Provider Processing Strategies

## Overview
This document provides detailed analysis of the cross-provider processing strategies, explaining how devices are grouped for unified optimization, provider-specific auto change processing, cross-provider permutation generation, and multi-provider queue creation for parallel processing.

## 1. Cross-Provider Rate Pool Processing: Groups Devices by Provider-Agnostic Rate Pool ID for Unified Optimization

### What
Cross-provider rate pool processing groups devices by their CustomerRatePoolId, enabling unified optimization across multiple service providers while maintaining rate pool-specific optimization logic.

### Why
- **Unified Optimization**: Enables consistent optimization logic across different providers
- **Resource Efficiency**: Reduces complexity by abstracting provider-specific differences
- **Scalability**: Allows handling of large customer bases across multiple carriers
- **Cost Optimization**: Maximizes savings by pooling devices with similar rate characteristics
- **Provider Independence**: Maintains optimization logic regardless of carrier-specific implementations

### How
The system groups optimization devices by their CustomerRatePoolId and processes each group independently:

```532:544:AltaworxSimCardCostQueueCustomerOptimization.cs
var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();

foreach (var simCardsByRatePoolId in simCardsByRatePoolIds)
{
    LogInfo(context, CommonConstants.INFO, $"RatePoolId: {simCardsByRatePoolId}");
    // Get all rate plan codes from the devices
    var ratePlanCodes = simCardsByRatePoolId.Select(x => x.CustomerRatePlanCode).Distinct();
    var isError = false;
    if (simCardsByRatePoolId.Key != null)
    {
        // Get all rate plans with matching rate plan codes
        var ratePlansForPool = ratePlans.Where(x => ratePlanCodes.Contains(x.PlanName));
        isError = await ProcessRatePoolGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, billingPeriod, instanceId, chargeType, ratePlansForPool, simCardsByRatePoolId.ToList(), simCardsByRatePoolId?.Key, queuesPerInstance: QueuesPerInstance);
```

### Algorithm
```
ALGORITHM: CrossProviderRatePoolProcessing()
INPUT: List<OptimizationSimCard> optimizationSimCards, List<RatePlan> ratePlans
OUTPUT: Boolean processingSuccess

1. GROUP devices by CustomerRatePoolId:
   a. simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct()
2. FOR each ratePoolGroup in simCardsByRatePoolIds:
   a. LOG rate pool ID for tracking
   b. EXTRACT rate plan codes from devices:
      i. ratePlanCodes = ratePoolGroup.Select(x => x.CustomerRatePlanCode).Distinct()
   c. IF ratePoolGroup.Key != null (has valid rate pool ID):
      i. FILTER rate plans: ratePlansForPool = ratePlans.Where(x => ratePlanCodes.Contains(x.PlanName))
      ii. PROCESS rate pool group: ProcessRatePoolGroup(...)
   d. ELSE (auto change rate plans):
      i. GROUP by plan name: ratePlansByCodes = ratePlans.Where(...).GroupBy(x => x.PlanName)
      ii. FOR each planNameGroup: ProcessPlanNameGroup(...)
   e. IF error occurs: STOP processing and RETURN error
3. RETURN processing success
```

### Code Locations

**Rate Pool ID Grouping:**
```532:532:AltaworxSimCardCostQueueCustomerOptimization.cs
var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();
```

**Cross-Provider Rate Pool Processing:**
```818:830:AltaworxSimCardCostQueueCustomerOptimization.cs
var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();

foreach (var simCardsByRatePoolId in simCardsByRatePoolIds)
{
    LogInfo(context, CommonConstants.INFO, $"RatePoolId: {simCardsByRatePoolId.Key}");
    // Get all rate plan codes from the devices
    var ratePlanCodes = simCardsByRatePoolId.Select(x => x.CustomerRatePlanCode).Distinct();
    var isError = false;
    if (simCardsByRatePoolId.Key != null)
    {
        // Get all rate plans with matching rate plan codes
        var ratePlansForPool = ratePlans.Where(x => ratePlanCodes.Contains(x.PlanName));
        isError = await ProcessRatePoolGroup(context, customer.IntegrationAuthenticationId, usesProration, customer.RevAccountNumber, customer.CustomerId, billingPeriod, instanceId, chargeType, ratePlansForPool, simCardsByRatePoolId.ToList(), simCardsByRatePoolId?.Key, queuesPerInstance: QueuesPerInstance);
```

## 2. Provider-Specific Auto Change Processing: Groups Devices by Provider and Rate Plan Code for Targeted Optimization

### What
Provider-specific auto change processing groups devices and rate plans by plan name and provider compatibility, enabling targeted optimization within specific carrier constraints and auto change capabilities.

### Why
- **Provider Compliance**: Ensures optimization respects carrier-specific business rules
- **Targeted Optimization**: Provides granular control over rate plan changes
- **Performance Optimization**: Reduces processing overhead by provider-specific grouping
- **Flexibility**: Allows different optimization strategies per provider
- **Risk Management**: Minimizes cross-provider optimization risks

### How
The system filters auto change rate plans by provider compatibility and groups by plan name:

```807:815:AltaworxSimCardCostQueueCustomerOptimization.cs
var autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan);
if (autoChangeRatePlans.Any() && !string.IsNullOrWhiteSpace(serviceProviderIds))
{
    var serviceProviderIdList = serviceProviderIds.Replace(" ", "").Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList();
    autoChangeRatePlans = autoChangeRatePlans.Where(x => x.ServiceProviderIds.Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList().ContainsAllItems(serviceProviderIdList)).ToList();
    if (!autoChangeRatePlans.Any())
    {
        LogInfo(context, CommonConstants.ERROR, string.Format(LogCommonStrings.NO_VALID_CROSS_PROVIDER_CUSTOMER_RATE_PLAN_FOUND, serviceProviderIds));
        return true;
    }
}
```

### Algorithm
```
ALGORITHM: ProviderSpecificAutoChangeProcessing()
INPUT: List<RatePlan> ratePlans, String serviceProviderIds, List<OptimizationSimCard> devices
OUTPUT: Boolean processingSuccess

1. FILTER auto change rate plans:
   a. autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan)
2. IF autoChangeRatePlans.Any() AND serviceProviderIds specified:
   a. PARSE provider list: serviceProviderIdList = serviceProviderIds.Split()
   b. FILTER compatible plans: 
      i. autoChangeRatePlans.Where(x => x.ServiceProviderIds.Split().ContainsAllItems(serviceProviderIdList))
   c. IF no compatible plans found:
      i. LOG error: NO_VALID_CROSS_PROVIDER_CUSTOMER_RATE_PLAN_FOUND
      ii. RETURN error
3. GROUP devices by rate plan codes:
   a. ratePlansByCodes = ratePlans.Where(...).GroupBy(x => x.PlanName)
4. FOR each planNameGroup in ratePlansByCodes:
   a. PROCESS plan name group: ProcessPlanNameGroup(...)
   b. IF error: RETURN error
5. RETURN success
```

### Code Locations

**Auto Change Rate Plan Filtering:**
```807:815:AltaworxSimCardCostQueueCustomerOptimization.cs
var autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan);
if (autoChangeRatePlans.Any() && !string.IsNullOrWhiteSpace(serviceProviderIds))
{
    var serviceProviderIdList = serviceProviderIds.Replace(" ", "").Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList();
    autoChangeRatePlans = autoChangeRatePlans.Where(x => x.ServiceProviderIds.Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList().ContainsAllItems(serviceProviderIdList)).ToList();
    if (!autoChangeRatePlans.Any())
    {
        LogInfo(context, CommonConstants.ERROR, string.Format(LogCommonStrings.NO_VALID_CROSS_PROVIDER_CUSTOMER_RATE_PLAN_FOUND, serviceProviderIds));
        return true;
    }
}
```

**Provider-Specific Plan Name Grouping:**
```835:838:AltaworxSimCardCostQueueCustomerOptimization.cs
// Group rate plans by rate plan code and run auto change optimization logic for this group of devices
var ratePlansByCodes = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan && ratePlanCodes.Contains(ratePlan.PlanName)).GroupBy(x => x.PlanName);
foreach (var ratePlansByCode in ratePlansByCodes)
{
    isError = await ProcessPlanNameGroup(context, customer.IntegrationAuthenticationId, usesProration, customer.RevAccountNumber, customer.CustomerId, billingPeriod, instanceId, chargeType, ratePlansByCode, optimizationSimCards);
```

## 3. Cross-Provider Permutation Generation: Creates Valid Rate Plan Combinations Across Providers

### What
Cross-provider permutation generation creates all valid rate plan combinations across multiple service providers, generating optimization sequences that respect provider constraints and compatibility requirements.

### Why
- **Comprehensive Optimization**: Explores all possible rate plan combinations for maximum savings
- **Cross-Provider Compatibility**: Ensures generated permutations work across provider boundaries
- **Algorithm Efficiency**: Optimizes computational resources through structured permutation generation
- **Quality Assurance**: Validates permutations before optimization execution
- **Scalability**: Handles complex multi-provider scenarios efficiently

### How
The system generates rate pool sequences and creates optimization queues for each permutation:

```631:658:AltaworxSimCardCostQueueCustomerOptimization.cs
LogInfo(context, LogTypeConstant.Sub, detail: $"Start GenerateRatePoolSequences for {ratePoolCollection.RatePools.Count} Rate Plans");
var ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools);
LogInfo(context, LogTypeConstant.Sub, "End GenerateRatePoolSequences");

var dtQueueRatePlan = new DataTable();
dtQueueRatePlan.Columns.Add("QueueId", typeof(long));
dtQueueRatePlan.Columns.Add("CommGroup_RatePlanId", typeof(long));
dtQueueRatePlan.Columns.Add("SequenceOrder", typeof(int));
dtQueueRatePlan.Columns.Add("CreatedBy");
dtQueueRatePlan.Columns.Add("CreatedDate", typeof(DateTime));

foreach (var ratePoolSequence in ratePoolSequences)
{
    // add queue for rate plan permutation
    var queueId = CreateQueue(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId, usesProration);

    // add rate plans to queue
    var dtQueueRatePlanTemp = AddRatePlansToQueue(queueId, ratePoolSequence, commGroupRatePlanTable);
    if (dtQueueRatePlanTemp != null && dtQueueRatePlanTemp.Rows.Count > 0)
    {
        foreach (DataRow dr in dtQueueRatePlanTemp.Rows)
        {
            dtQueueRatePlan.Rows.Add(dr.ItemArray);
        }
    }
}

CreateQueueRatePlans(context, dtQueueRatePlan);
```

### Algorithm
```
ALGORITHM: CrossProviderPermutationGeneration()
INPUT: RatePoolCollection ratePoolCollection, Long commPlanGroupId, Long instanceId
OUTPUT: List<OptimizationQueue> optimizationQueues

1. GENERATE rate pool sequences:
   a. LOG start of sequence generation
   b. ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools)
   c. LOG completion of sequence generation
2. INITIALIZE queue rate plan data table:
   a. CREATE dtQueueRatePlan with columns: QueueId, CommGroup_RatePlanId, SequenceOrder, CreatedBy, CreatedDate
3. FOR each ratePoolSequence in ratePoolSequences:
   a. CREATE optimization queue:
      i. queueId = CreateQueue(context, instanceId, commPlanGroupId, serviceProviderId, usesProration)
   b. ADD rate plans to queue:
      i. dtQueueRatePlanTemp = AddRatePlansToQueue(queueId, ratePoolSequence, commGroupRatePlanTable)
   c. IF queue rate plan data exists:
      i. FOR each DataRow: ADD to dtQueueRatePlan
4. PERSIST queue rate plans:
   a. CreateQueueRatePlans(context, dtQueueRatePlan)
5. RETURN generated queues
```

### Code Locations

**Rate Pool Sequence Generation:**
```631:633:AltaworxSimCardCostQueueCustomerOptimization.cs
LogInfo(context, LogTypeConstant.Sub, detail: $"Start GenerateRatePoolSequences for {ratePoolCollection.RatePools.Count} Rate Plans");
var ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools);
LogInfo(context, LogTypeConstant.Sub, "End GenerateRatePoolSequences");
```

**Queue Creation for Permutations:**
```642:658:AltaworxSimCardCostQueueCustomerOptimization.cs
foreach (var ratePoolSequence in ratePoolSequences)
{
    // add queue for rate plan permutation
    var queueId = CreateQueue(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId, usesProration);

    // add rate plans to queue
    var dtQueueRatePlanTemp = AddRatePlansToQueue(queueId, ratePoolSequence, commGroupRatePlanTable);
    if (dtQueueRatePlanTemp != null && dtQueueRatePlanTemp.Rows.Count > 0)
    {
        foreach (DataRow dr in dtQueueRatePlanTemp.Rows)
        {
            dtQueueRatePlan.Rows.Add(dr.ItemArray);
        }
    }
}

CreateQueueRatePlans(context, dtQueueRatePlan);
```

## 4. Multi-Provider Queue Creation: Generates Optimization Queues for Parallel Cross-Provider Processing

### What
Multi-provider queue creation generates multiple optimization queues that enable parallel processing across different service providers, maximizing computational efficiency and reducing processing time.

### Why
- **Parallel Processing**: Enables simultaneous optimization across multiple providers
- **Performance Optimization**: Reduces total processing time through parallelization
- **Resource Utilization**: Maximizes computational resource usage
- **Scalability**: Handles large-scale cross-provider optimizations efficiently
- **Fault Tolerance**: Isolates failures to individual queues without affecting others

### How
The system creates communication plan groups and generates optimization queues for parallel execution:

```589:619:AltaworxSimCardCostQueueCustomerOptimization.cs
// create new comm plan group
var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);

var baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId,
    revAccountNumber, integrationAuthenticationId, null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId);
// add rate plans to comm plan group
var commGroupRatePlanTable = AddCustomerRatePlansToCommPlanGroup(context, instanceId, commPlanGroupId, calculatedPlans);

// zero sim card => no need to run optimizer
// one sim card => swapping between rate plans would be the same as base device assignment
//              => already calculate that => no need to run optimizer
if (baseAssignedSimCardsCount > OptimizationConstant.BaseAssignedDeviceLimit)
{
    // permute rate plans
    if (calculatedPlans.Count > OptimizationConstant.RatePlanLimit)
    {
        LogInfo(context, LogTypeConstant.Exception, $"The rate plan count exceeds the limit of 15 for this Rate Plan Code {ratePlanGroup.Key}. Please cut down the options to 15 or less for this Rate Plan Code.");
        continue;
    }
    if (calculatedPlans.Count <= OptimizationConstant.RatePlanMinimumLimit)
    {

        LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.AUTO_CHANGE_MINIMUM_RATE_PLAN_LIMIT_REACHED, calculatedPlans.Count, planNameGroup.Key, ratePlanGroup.Key));
        continue;
    }
    GeneratePermutationQueueRatePlans(context, usesProration, billingPeriod, instanceId, commPlanGroupId, ratePoolCollection, commGroupRatePlanTable);

    // enqueue rate plan permutations
    await EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true);
}
```

### Algorithm
```
ALGORITHM: MultiProviderQueueCreation()
INPUT: List<RatePlan> groupRatePlans, Long instanceId, OptimizationChargeType chargeType
OUTPUT: List<OptimizationQueue> parallelQueues

1. CREATE communication plan group:
   a. commPlanGroupId = CreateCommPlanGroup(context, instanceId)
2. CALCULATE and create rate pools:
   a. calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null)
   b. ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType)
   c. ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools)
3. ASSIGN base devices:
   a. baseAssignedSimCardsCount = BaseDeviceAssignment(...)
4. ADD rate plans to communication group:
   a. commGroupRatePlanTable = AddCustomerRatePlansToCommPlanGroup(...)
5. VALIDATE optimization requirements:
   a. IF baseAssignedSimCardsCount > OptimizationConstant.BaseAssignedDeviceLimit:
      i. VALIDATE rate plan limits (max 15, min 2)
      ii. IF validation passes:
          - GENERATE permutation queues: GeneratePermutationQueueRatePlans(...)
          - ENQUEUE optimization runs: EnqueueOptimizationRunsAsync(...)
6. RETURN parallel processing queues
```

### Code Locations

**Communication Plan Group Creation:**
```589:595:AltaworxSimCardCostQueueCustomerOptimization.cs
// create new comm plan group
var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);

var baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId,
    revAccountNumber, integrationAuthenticationId, null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId);
```

**Parallel Queue Enqueuing:**
```616:619:AltaworxSimCardCostQueueCustomerOptimization.cs
GeneratePermutationQueueRatePlans(context, usesProration, billingPeriod, instanceId, commPlanGroupId, ratePoolCollection, commGroupRatePlanTable);

// enqueue rate plan permutations
await EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true);
```

**Queue Creation for No Rate Plan Devices:**
```671:678:AltaworxSimCardCostQueueCustomerOptimization.cs
var unusedCommPlanGroupId = CreateCommPlanGroup(context, instanceId);
var unusedQueueId = CreateQueue(context, instanceId, unusedCommPlanGroupId, null, usesProration);
StartQueue(context, unusedQueueId, string.Empty);
// no rate plan => already set total cost below as 0 => the sims will not participate in the algorithm
var simsWithNoRatePlanCodes = ProjectDataUsageAndSaveDevices(context, instanceId, noRatePlanCodes, billingPeriod, false);
OptimizationResultDbWriter.RecordRatePool(context, context.ConnectionString, unusedQueueId, billingPeriodId.Value, simsWithNoRatePlanCodes);
OptimizationResultDbWriter.RecordTotalCost(context, context.ConnectionString, unusedQueueId, OptimizationConstant.DefaultUnassignedTotalCost);
StopQueue(context, unusedQueueId);
```

## Processing Strategy Integration

The four cross-provider processing strategies work together to provide comprehensive optimization:

1. **Rate Pool Processing** provides the foundational grouping mechanism
2. **Auto Change Processing** enables provider-specific optimizations
3. **Permutation Generation** creates comprehensive optimization scenarios
4. **Queue Creation** enables parallel execution for maximum performance

This integrated approach ensures efficient, scalable, and comprehensive cross-provider optimization while maintaining provider-specific compliance and business rule enforcement.