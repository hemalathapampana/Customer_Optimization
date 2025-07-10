# Cross-Provider Auto Change Logic

## Overview
This document provides detailed analysis of the cross-provider auto change logic, explaining how optimization is enabled within individual providers, across different providers, through provider-specific rate pool collections, and with cross-provider compatibility validation.

## 1. Provider-Specific Auto Change: Enables Optimization Within Individual Providers

### What
Provider-specific auto change enables automatic rate plan optimization within a single service provider's ecosystem, allowing seamless rate plan switching without cross-provider complexity.

### Why
- **Simplified Operations**: Reduces complexity by focusing on single-provider optimization
- **Provider Compliance**: Ensures adherence to individual provider-specific business rules
- **Faster Processing**: Eliminates cross-provider coordination overhead
- **Risk Mitigation**: Minimizes potential conflicts between different provider systems

### How
Single-provider auto change processing through provider-isolated optimization logic:

#### Algorithm:
```
1. IDENTIFY rate plans with AutoChangeRatePlan = true within single provider
2. SEPARATE rate plans by auto change capability:
   a. autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan)
   b. ratePlansByCustomerRatePool = ratePlans.Where(ratePlan => !ratePlan.AutoChangeRatePlan)
3. PROCESS provider-specific auto change plans:
   a. GROUP auto change plans by PlanName within provider:
      ratePlansByCodes = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan && ratePlanCodes.Contains(ratePlan.PlanName)).GroupBy(x => x.PlanName)
   b. FOR each plan name group:
      i. VALIDATE provider-specific constraints
      ii. CALL ProcessPlanNameGroup() for single-provider optimization
4. APPLY provider-specific pooling logic:
   a. GROUP by AllowsSimPooling capability
   b. VALIDATE SIM pooling support within provider
   c. CREATE provider-specific rate pool collections
5. GENERATE permutation queues for provider:
   a. CALCULATE rate pool sequences within provider scope
   b. CREATE optimization queues for single provider
   c. EXECUTE provider-specific optimization algorithm
6. VALIDATE provider-specific thresholds and limits
7. RECORD results within provider context
```

#### Code Locations:
```518:518:AltaworxSimCardCostQueueCustomerOptimization.cs
var ratePlansByCustomerRatePool = ratePlans.Where(ratePlan => !ratePlan.AutoChangeRatePlan).ToList();
```

```549:552:AltaworxSimCardCostQueueCustomerOptimization.cs
var ratePlansByCodes = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan && ratePlanCodes.Contains(ratePlan.PlanName)).GroupBy(x => x.PlanName);
foreach (var ratePlansByCode in ratePlansByCodes)
{
    isError = await ProcessPlanNameGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, billingPeriod, instanceId, chargeType, ratePlansByCode, simCardsByRatePoolId.ToList());
}
```

```567:569:AltaworxSimCardCostQueueCustomerOptimization.cs
foreach (var ratePlanGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling))
{
    LogInfo(context, LogTypeConstant.Info, $"Allows SIM Pooling: {ratePlanGroup.Key}");
```

## 2. Cross-Provider Auto Change: Allows Optimization Across Different Providers

### What
Cross-provider auto change enables automatic rate plan optimization that spans multiple service providers, allowing customers to switch between providers for optimal cost savings.

### Why
- **Maximum Cost Savings**: Leverages best rates across entire provider ecosystem
- **Comprehensive Optimization**: Considers all available provider options
- **Customer Flexibility**: Enables multi-provider relationships for optimal outcomes
- **Competitive Advantage**: Provides access to best-in-market pricing

### How
Multi-provider auto change coordination through unified optimization engine:

#### Algorithm:
```
1. EXTRACT serviceProviderIds from optimization context
2. VALIDATE cross-provider auto change eligibility:
   a. autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan)
   b. IF autoChangeRatePlans.Any() AND serviceProviderIds specified THEN
      i. PARSE serviceProviderIdList from comma-separated string
      ii. FILTER compatible plans: autoChangeRatePlans.Where(x => x.ServiceProviderIds.Split().ContainsAllItems(serviceProviderIdList))
3. VALIDATE cross-provider compatibility:
   a. ENSURE at least one compatible plan exists across providers
   b. CHECK billing period alignment across providers
   c. VALIDATE authentication credentials for all providers
4. COORDINATE cross-provider optimization:
   a. CREATE unified optimization instance with PortalTypes.CrossProvider
   b. CALL ProcessCrossProviderDevicesByCustomerRatePlans()
   c. HANDLE provider-specific constraints within unified framework
5. GENERATE cross-provider permutations:
   a. CREATE rate pool collections spanning multiple providers
   b. GENERATE optimization sequences across provider boundaries
   c. COORDINATE result aggregation from multiple providers
6. EXECUTE cross-provider optimization:
   a. PROCESS devices across all compatible providers
   b. APPLY cross-provider business rules and constraints
   c. OPTIMIZE for best cost across entire provider portfolio
7. VALIDATE and RECORD cross-provider results
```

#### Code Locations:
```806:813:AltaworxSimCardCostQueueCustomerOptimization.cs
var autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan);
if (autoChangeRatePlans.Any() && !string.IsNullOrWhiteSpace(serviceProviderIds))
{
    var serviceProviderIdList = serviceProviderIds.Replace(" ", "").Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList();
    autoChangeRatePlans = autoChangeRatePlans.Where(x => x.ServiceProviderIds.Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList().ContainsAllItems(serviceProviderIdList)).ToList();
    LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.CROSS_PROVIDER_CUSTOMER_RATE_PLAN_FOUND, serviceProviderIds));
}
```

```725:727:AltaworxSimCardCostQueueCustomerOptimization.cs
var instanceId = crossProviderOptimizationRepository.StartCrossProviderOptimizationInstance(ParameterizedLog(context), tenantId, messageId,
    customer, PortalTypes.CrossProvider, optimizationSessionId,
    useBillInAdvance, billingPeriod, nextBillingPeriod, serviceProviderIds);
```

```783:784:AltaworxSimCardCostQueueCustomerOptimization.cs
private async Task<bool> ProcessCrossProviderDevicesByCustomerRatePlans(KeySysLambdaContext context, string serviceProviderIds, bool usesProration, List<RatePlan> ratePlans, BillingPeriod billingPeriod, BillingPeriod nextBillingPeriod, long instanceId, OptimizationChargeType chargeType, OptimizationCustomer customer, int tenantId)
```

## 3. Provider Rate Pool Collections: Creates Provider-Specific Rate Plan Groupings

### What
Provider rate pool collections organize rate plans into logical groupings based on provider-specific characteristics, pooling capabilities, and optimization strategies.

### Why
- **Organized Processing**: Structures rate plans for efficient optimization algorithms
- **Provider Isolation**: Maintains provider-specific groupings and constraints
- **Optimization Efficiency**: Enables targeted optimization strategies per group
- **Resource Management**: Optimizes memory and processing resources

### How
Systematic rate pool collection creation and management:

#### Algorithm:
```
1. CREATE communication plan groups for optimization:
   a. commPlanGroupId = CreateCommPlanGroup(context, instanceId)
   b. ASSOCIATE group with specific provider context
2. CALCULATE rate pool specifications:
   a. calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null)
   b. APPLY provider-specific usage calculations
   c. VALIDATE calculation accuracy and constraints
3. CREATE provider-specific rate pools:
   a. ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType)
   b. APPLY provider-specific rate pool configurations
   c. SET appropriate charge types per provider
4. ASSEMBLE rate pool collections:
   a. ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools)
   b. ORGANIZE pools by provider and capability
   c. VALIDATE collection integrity and completeness
5. PERFORM base device assignment:
   a. baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, 
      billingPeriod.ServiceProviderId, revAccountNumber, integrationAuthenticationId, 
      null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId)
   b. VALIDATE assignment within provider constraints
6. GENERATE permutation sequences:
   a. IF baseAssignedSimCardsCount > OptimizationConstant.BaseAssignedDeviceLimit THEN
      i. CREATE rate pool sequences for optimization
      ii. GENERATE permutation queues
      iii. COORDINATE optimization execution
7. VALIDATE collection completeness and execute optimization
```

#### Code Locations:
```590:595:AltaworxSimCardCostQueueCustomerOptimization.cs
var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);

var baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId,
    revAccountNumber, integrationAuthenticationId, null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId);
```

```616:616:AltaworxSimCardCostQueueCustomerOptimization.cs
GeneratePermutationQueueRatePlans(context, usesProration, billingPeriod, instanceId, commPlanGroupId, ratePoolCollection, commGroupRatePlanTable);
```

```629:633:AltaworxSimCardCostQueueCustomerOptimization.cs
private void GeneratePermutationQueueRatePlans(KeySysLambdaContext context, bool usesProration, BillingPeriod billingPeriod, long instanceId, long commPlanGroupId, RatePoolCollection ratePoolCollection, DataTable commGroupRatePlanTable)
{
    LogInfo(context, LogTypeConstant.Sub, detail: $"Start GenerateRatePoolSequences for {ratePoolCollection.RatePools.Count} Rate Plans");
    var ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools);
    LogInfo(context, LogTypeConstant.Sub, "End GenerateRatePoolSequences");
```

## 4. Cross-Provider Compatibility: Ensures Rate Plans Work Across Provider Boundaries

### What
Cross-provider compatibility validation ensures that rate plans from different providers can work together seamlessly in a unified optimization framework without conflicts or data integrity issues.

### Why
- **Data Integrity**: Prevents conflicts between different provider data structures
- **Optimization Accuracy**: Ensures valid comparisons across provider boundaries
- **System Reliability**: Maintains system stability with multi-provider operations
- **Regulatory Compliance**: Ensures all provider combinations meet regulatory requirements

### How
Comprehensive compatibility validation across provider ecosystems:

#### Algorithm:
```
1. VALIDATE provider ID compatibility:
   a. PARSE serviceProviderIds and extract individual provider IDs
   b. FOR each provider in serviceProviderIdList:
      i. VALIDATE provider exists and is active
      ii. CHECK provider supports cross-provider operations
      iii. VERIFY customer has valid relationship with provider
2. VALIDATE rate plan structural compatibility:
   a. FOR each rate plan across providers:
      i. CHECK DataPerOverageCharge != 0.0M across all providers
      ii. CHECK OverageRate != 0.0M across all providers
      iii. VALIDATE currency and rate structures are comparable
3. VALIDATE billing period alignment:
   a. ENSURE billing periods are synchronized across providers
   b. CHECK billing cycle compatibility
   c. VALIDATE proration support across all providers
4. VALIDATE integration compatibility:
   a. CHECK authentication mechanisms across providers
   b. VERIFY API compatibility and version alignment
   c. VALIDATE data exchange formats and protocols
5. VALIDATE business rule compatibility:
   a. CHECK conflicting business rules between providers
   b. VALIDATE rate plan change policies across providers
   c. ENSURE pooling capabilities are compatible
6. VALIDATE optimization threshold compatibility:
   a. CHECK rate plan count limits across providers:
      calculatedPlans.Count <= OptimizationConstant.RatePlanLimit (15)
   b. VALIDATE device count thresholds are met
   c. ENSURE optimization algorithms are compatible
7. EXECUTE compatibility resolution:
   a. IF incompatibilities found THEN
      i. LOG detailed compatibility errors
      ii. EXCLUDE incompatible combinations
      iii. CONTINUE with compatible subset
   b. ELSE proceed with full cross-provider optimization
```

#### Code Locations:
```573:577:AltaworxSimCardCostQueueCustomerOptimization.cs
var zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
if (zeroValueRatePlans.Count > 0)
{
    LogInfo(context, LogTypeConstant.Exception, $"The following rate plans in '{planNameGroup.Key}' has Data per Overage Charge or Overage Rate of 0. Please update to a non-zero value.{Environment.NewLine} {string.Join(',', zeroValueRatePlans.Select(ratePlan => ratePlan.PlanDisplayName))}");
    return true;
}
```

```608:616:AltaworxSimCardCostQueueCustomerOptimization.cs
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
```

```811:815:AltaworxSimCardCostQueueCustomerOptimization.cs
if (!autoChangeRatePlans.Any())
{
    LogInfo(context, CommonConstants.ERROR, string.Format(LogCommonStrings.NO_VALID_CROSS_PROVIDER_CUSTOMER_RATE_PLAN_FOUND, serviceProviderIds));
    return true;
}
```

```642:660:AltaworxSimCardCostQueueCustomerOptimization.cs
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
```

## Implementation Architecture

### Auto Change Processing Flow
```
Rate Plan Classification → Provider-Specific Processing OR Cross-Provider Processing → 
Rate Pool Collection Creation → Compatibility Validation → 
Permutation Generation → Optimization Execution → Result Validation
```

### Provider Processing Strategy
- **Single Provider**: Simplified optimization within provider boundaries
- **Multi-Provider**: Coordinated optimization across provider ecosystem
- **Hybrid**: Combination of single and multi-provider optimizations based on customer needs

### Rate Pool Collection Management
1. **Provider-Specific Collections**: Isolated collections per provider
2. **Cross-Provider Collections**: Unified collections spanning multiple providers
3. **Hybrid Collections**: Mixed collections with provider-specific and cross-provider elements
4. **Dynamic Collections**: Collections that adapt based on optimization requirements

### Compatibility Validation Framework
- **Structural Validation**: Rate plan data structure compatibility
- **Business Rule Validation**: Provider-specific business rule compatibility
- **Integration Validation**: Technical integration compatibility
- **Optimization Validation**: Algorithm and threshold compatibility

### Error Handling and Recovery
- **Provider-Specific Errors**: Isolated error handling per provider
- **Cross-Provider Errors**: Coordinated error handling across providers
- **Compatibility Errors**: Detailed logging and graceful degradation
- **Recovery Strategies**: Fallback to single-provider optimization when cross-provider fails

This auto change logic framework ensures efficient, accurate, and reliable optimization both within individual providers and across multiple provider boundaries while maintaining data integrity and system performance.