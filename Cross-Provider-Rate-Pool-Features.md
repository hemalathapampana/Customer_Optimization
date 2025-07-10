# Cross-Provider Rate Pool Features

## Overview
This document provides detailed analysis of cross-provider rate pool features, explaining how devices are linked to rate pools spanning multiple providers, filtered by provider-specific customer rate plan codes, enabled for usage sharing across providers, and organized into multi-provider optimization groups with cross-provider constraints.

## 1. Cross-Provider Rate Pool ID: Links Devices to Rate Pools Spanning Multiple Providers

### What
Cross-Provider Rate Pool ID uses `CustomerRatePoolId` to link devices to rate pools that span multiple service providers, creating unified optimization groups that can process devices from different carriers (Verizon, AT&T, T-Mobile) within the same pool for consistent cost optimization.

### Why
- **Unified Optimization**: Enables devices from different providers to be optimized together as a single pool
- **Cross-Provider Consistency**: Ensures uniform rate pooling logic across multiple carriers
- **Resource Efficiency**: Reduces processing overhead by grouping related devices regardless of provider
- **Cost Optimization**: Allows for more comprehensive optimization strategies across provider boundaries
- **Data Integrity**: Maintains consistent rate pool associations across provider boundaries

### How
The system groups optimization devices by their `CustomerRatePoolId` and processes each group independently using provider-specific validation while maintaining unified optimization logic across all supported providers.

### Algorithm: CrossProviderRatePoolIdLinking()

**INPUT**: List<vwOptimizationSimCard> optimizationSimCards, List<RatePlan> ratePlans
**OUTPUT**: Processed rate pool groups with cross-provider optimization

1. **GROUP devices by CustomerRatePoolId**:
   ```
   a. simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct()
   ```

2. **FOR each ratePoolGroup in simCardsByRatePoolIds**:
   ```
   a. LOG rate pool ID for tracking
   b. ratePlanCodes = ratePoolGroup.Select(x => x.CustomerRatePlanCode).Distinct()
   c. IF ratePoolGroup.Key != null (has valid rate pool ID):
      i. GET ratePlansForPool = ratePlans.Where(x => ratePlanCodes.Contains(x.PlanName))
      ii. PROCESS cross-provider rate pool group
   d. ELSE (no rate pool ID):
      i. GROUP by rate plan codes and process auto change logic
   ```

3. **VALIDATE cross-provider compatibility**:
   ```
   a. CHECK ServiceProviderIds compatibility
   b. VERIFY rate plan availability across providers
   c. ENSURE proper authentication for each provider
   ```

### Code Locations

**Rate Pool ID Grouping:**
```532:532:AltaworxSimCardCostQueueCustomerOptimization.cs
var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();
```

**Cross-Provider Rate Pool Processing:**
```532:545:AltaworxSimCardCostQueueCustomerOptimization.cs
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
        isError = await ProcessRatePoolGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, billingPeriod, instanceId, chargeType, ratePlansForPool, simCardsByRatePoolId.ToList(), simCardsByRatePoolId?.Key, queuesPerInstance: QueuesPerInstance);
    }
```

**Cross-Provider Rate Pool Recording:**
```865:865:AltaworxSimCardCostQueueCustomerOptimization.cs
OptimizationResultDbWriter.RecordCrossProviderRatePool(context, context.ConnectionString, unusedQueueId, simsWithNoRatePlanCodes, customerBillingPeriod.Id);
```

## 2. Multi-Provider Filtering: Filters Devices by Provider-Specific Customer Rate Plan Codes

### What
Multi-Provider Filtering applies provider-specific filtering logic using `CustomerRatePlanCode` to ensure only devices with valid rate plan associations are processed for optimization, while maintaining compatibility across different service providers.

### Why
- **Data Quality**: Ensures only devices with valid rate plan codes are processed
- **Provider Compliance**: Maintains provider-specific rate plan validation requirements
- **Optimization Accuracy**: Prevents processing of devices without proper rate plan associations
- **Error Prevention**: Reduces optimization failures due to invalid device configurations
- **Cross-Provider Consistency**: Applies uniform filtering logic across all providers

### How
The system filters optimization devices using `CustomerRatePlanCode` validation, ensuring only devices with valid rate plan associations proceed to the optimization process while maintaining provider-specific requirements.

### Algorithm: MultiProviderFiltering()

**INPUT**: List<vwOptimizationSimCard> optimizationSimCards
**OUTPUT**: Filtered list of optimization-eligible devices

1. **APPLY provider-specific filtering**:
   ```
   a. IF (revAccountNumber != null OR AMOPCustomerId != null):
      i. optimizationSimCards = optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList()
   ```

2. **EXTRACT rate plan codes from filtered devices**:
   ```
   a. FOR each device group by rate pool:
      i. ratePlanCodes = simCardsByRatePoolId.Select(x => x.CustomerRatePlanCode).Distinct()
   ```

3. **VALIDATE cross-provider compatibility**:
   ```
   a. CHECK rate plan code validity across providers
   b. ENSURE consistent rate plan associations
   c. FILTER out devices with invalid configurations
   ```

### Code Locations

**Rev/AMOP Customer Filtering:**
```514:514:AltaworxSimCardCostQueueCustomerOptimization.cs
optimizationSimCards = optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList();
```

**Cross-Provider Customer Filtering:**
```788:788:AltaworxSimCardCostQueueCustomerOptimization.cs
optimizationSimCards = optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList();
```

**Rate Plan Code Extraction:**
```538:538:AltaworxSimCardCostQueueCustomerOptimization.cs
var ratePlanCodes = simCardsByRatePoolId.Select(x => x.CustomerRatePlanCode).Distinct();
```

## 3. Cross-Provider Pooled Usage: Allows Usage Sharing Across Customer Devices and Providers

### What
Cross-Provider Pooled Usage enables usage sharing across customer devices and providers through `AllowsSimPooling` configuration, allowing data allowances and costs to be shared across devices from different service providers within the same customer account.

### Why
- **Cost Optimization**: Enables more efficient utilization of data allowances across providers
- **Resource Sharing**: Allows unused data from one provider to benefit devices on another provider
- **Customer Flexibility**: Provides unified data management across multiple carriers
- **Billing Efficiency**: Simplifies billing through consolidated usage tracking
- **Optimization Accuracy**: Enables more comprehensive cost optimization strategies

### How
The system uses `AllowsSimPooling` flag to determine pooling capabilities and groups devices accordingly, enabling cross-provider data sharing and unified optimization strategies across multiple service providers.

### Algorithm: CrossProviderPooledUsage()

**INPUT**: List<RatePlan> ratePlans, List<vwOptimizationSimCard> optimizationSimCards
**OUTPUT**: Pooled optimization groups with cross-provider usage sharing

1. **GROUP rate plans by SIM pooling capability**:
   ```
   a. FOR each planNameGroup in ratePlans.GroupBy(x => x.PlanName):
      i. FOR each ratePlanGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling):
         - LOG pooling capability
         - CREATE rate pool collection with pooling enabled
   ```

2. **ENABLE cross-provider pooling**:
   ```
   a. shouldPoolByOptimizationGroup = (PortalType == Mobility OR IsCustomerOptimization) AND ratePools.Any(x => x.RatePlan.AllowsSimPooling)
   b. ratePoolCollection = CreateRatePoolCollection(ratePools, shouldPoolByOptimizationGroup, customerRatePoolId)
   ```

3. **PROCESS pooled optimization**:
   ```
   a. CALCULATE pooled usage across providers
   b. APPLY unified optimization strategies
   c. RECORD cross-provider pooled results
   ```

### Code Locations

**SIM Pooling Group Processing:**
```567:567:AltaworxSimCardCostQueueCustomerOptimization.cs
foreach (var ratePlanGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling))
```

**Pooling Capability Logging:**
```569:569:AltaworxSimCardCostQueueCustomerOptimization.cs
LogInfo(context, LogTypeConstant.Info, $"Allows SIM Pooling: {ratePlanGroup.Key}");
```

**Rate Pool Collection Creation:**
```234:235:AltaworxSimCardCostOptimizer.cs
var shouldPoolByOptimizationGroup = (instance.PortalType == PortalTypes.Mobility || instance.IsCustomerOptimization) && ratePools.Any(x => x.RatePlan.AllowsSimPooling); ;
ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools, shouldPoolByOptimizationGroup, customerRatePoolId);
```

## 4. Multi-Provider Optimization Groups: Uses Cross-Provider Grouping Logic and Constraints

### What
Multi-Provider Optimization Groups implement cross-provider grouping logic and constraints through communication plan groups (`CommPlanGroupId`) and optimization instances, coordinating optimization across multiple service providers while respecting individual provider constraints and capabilities.

### Why
- **Coordinated Optimization**: Ensures synchronized optimization across multiple providers
- **Constraint Management**: Applies provider-specific business rules and limitations
- **Performance Optimization**: Enables parallel processing across provider groups
- **Resource Management**: Optimizes system resources through intelligent grouping
- **Scalability**: Supports growing numbers of providers and devices

### How
The system creates communication plan groups for each optimization scenario, applies provider-specific constraints, and coordinates optimization execution across multiple providers using queue-based processing and instance management.

### Algorithm: MultiProviderOptimizationGroups()

**INPUT**: List<RatePlan> groupRatePlans, List<vwOptimizationSimCard> optimizationSimCards, Long instanceId
**OUTPUT**: Coordinated multi-provider optimization execution

1. **CREATE communication plan group**:
   ```
   a. commPlanGroupId = CreateCommPlanGroup(context, instanceId)
   b. calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null)
   c. ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType)
   ```

2. **APPLY provider-specific constraints**:
   ```
   a. IF calculatedPlans.Count > OptimizationConstant.RatePlanLimit:
      i. LOG rate plan limit exceeded error
      ii. SKIP optimization for this group
   b. IF calculatedPlans.Count <= OptimizationConstant.RatePlanMinimumLimit:
      i. LOG minimum rate plan limit reached
      ii. SKIP optimization for this group
   ```

3. **COORDINATE multi-provider processing**:
   ```
   a. GeneratePermutationQueueRatePlans(context, usesProration, billingPeriod, instanceId, commPlanGroupId, ratePoolCollection, commGroupRatePlanTable)
   b. EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true)
   ```

4. **MANAGE cross-provider constraints**:
   ```
   a. CHECK zero-value rate plans
   b. VALIDATE cross-provider compatibility
   c. ENFORCE provider-specific limitations
   d. COORDINATE parallel processing
   ```

### Code Locations

**Communication Plan Group Creation:**
```587:587:AltaworxSimCardCostQueueCustomerOptimization.cs
var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
```

**Rate Pool Calculation and Creation:**
```588:590:AltaworxSimCardCostQueueCustomerOptimization.cs
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);
```

**Provider Constraint Enforcement:**
```602:609:AltaworxSimCardCostQueueCustomerOptimization.cs
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

**Multi-Provider Queue Coordination:**
```619:619:AltaworxSimCardCostQueueCustomerOptimization.cs
await EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true);
```

## Summary

Cross-Provider Rate Pool Features provide a comprehensive framework for:

1. **Rate Pool ID Linking** creates provider-agnostic device groupings for unified optimization
2. **Multi-Provider Filtering** ensures data quality through provider-specific validation
3. **Pooled Usage** enables cross-provider data sharing and resource optimization
4. **Optimization Groups** coordinate complex multi-provider optimization scenarios

This framework ensures efficient, accurate, and scalable optimization while maintaining the integrity of customer-specific rate pool configurations across multiple service providers. The system balances unified optimization capabilities with provider-specific constraints to deliver optimal cost management across diverse carrier environments.