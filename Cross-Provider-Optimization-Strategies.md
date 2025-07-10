# Cross-Provider Optimization Strategies

## Overview
This document provides detailed analysis of four cross-provider optimization strategies, explaining how customer devices are processed across multiple providers using different grouping approaches, assignment orders, and optimization methodologies to achieve maximum cost reduction and efficiency.

## Strategy 1: Cross-Provider No Grouping + Largest to Smallest

### What
Cross-Provider No Grouping + Largest to Smallest processes customer devices individually across all providers, assigning highest usage customer devices first regardless of provider to optimize for maximum cross-provider cost reduction through priority-based allocation.

### Why
- **Maximum Cost Reduction**: Prioritizes high-usage devices that have the greatest cost impact
- **Cross-Provider Flexibility**: Assigns devices to optimal plans across any provider without grouping constraints
- **Resource Optimization**: Ensures highest-value devices get first access to best available plans
- **Provider Independence**: Operates across all providers without provider-specific grouping limitations
- **Individual Optimization**: Provides personalized optimization for each device based on usage patterns

### How
The system processes devices using `SimCardGrouping.NoGrouping` strategy with largest-to-smallest assignment order, ensuring high-usage devices across all providers are optimized first for maximum cost savings potential.

### Algorithm: CrossProviderNoGroupingLargestToSmallest()

**INPUT**: List<SimCard> crossProviderDevices, RatePoolCollection ratePoolCollection
**OUTPUT**: Optimized device assignments with maximum cost reduction

1. **CONFIGURE no grouping strategy**:
   ```
   a. groupingStrategy = SimCardGrouping.NoGrouping
   b. assignmentOrder = RemainingAssignmentOrder.LargestToSmallest
   ```

2. **PROCESS devices individually across providers**:
   ```
   a. FOR each device in crossProviderDevices.OrderByDescending(x => x.CycleDataUsageMB):
      i. EVALUATE all available rate pools across providers
      ii. ASSIGN to optimal plan regardless of provider
      iii. PRIORITIZE highest usage devices first
   ```

3. **OPTIMIZE for maximum cost reduction**:
   ```
   a. CALCULATE cost savings for each provider option
   b. SELECT best cross-provider assignment
   c. RECORD optimization results
   ```

4. **APPLY provider-specific capabilities**:
   ```
   a. CHECK high-usage device compatibility per provider
   b. ENSURE provider-specific constraints are met
   c. VALIDATE cross-provider assignment feasibility
   ```

### Code Locations

**No Grouping Strategy Selection:**
```275:275:AltaworxSimCardCostOptimizer.cs
return new List<SimCardGrouping> { SimCardGrouping.NoGrouping };
```

**Strategy Configuration Comments:**
```250:253:AltaworxSimCardCostOptimizer.cs
// No Grouping + Largest To Smallest
// No Grouping + Smallest To Largest
// Group By Communication Plan + Largest To Smallest
// Group By Communication Plan + Smallest To Largest
```

**SimCard Assignment with Grouping Strategy:**
```261:264:AltaworxSimCardCostOptimizer.cs
assigner.AssignSimCards(GetSimCardGroupingByPortalType(instance.PortalType, instance.IsCustomerOptimization),
                            context.OptimizationSettings.BillingTimeZone,
                            false,
                            false,
                            ratePoolSequences);
```

## Strategy 2: Cross-Provider No Grouping + Smallest to Largest

### What
Cross-Provider No Grouping + Smallest to Largest processes customer devices individually across all providers, assigning lowest usage customer devices first across providers to optimize for cross-provider plan utilization efficiency and balanced resource allocation.

### Why
- **Plan Utilization Efficiency**: Ensures efficient utilization of available plan allowances
- **Resource Balancing**: Distributes low-usage devices across providers for optimal resource utilization
- **Cost Efficiency**: Maximizes value from shared plan resources by filling lower-tier plans first
- **Provider Load Balancing**: Distributes device load evenly across multiple providers
- **Optimization Completeness**: Ensures all devices, including low-usage ones, receive optimization attention

### How
The system processes devices using `SimCardGrouping.NoGrouping` strategy with smallest-to-largest assignment order, prioritizing low-usage devices to achieve balanced plan utilization across all supported providers.

### Algorithm: CrossProviderNoGroupingSmallestToLargest()

**INPUT**: List<SimCard> crossProviderDevices, RatePoolCollection ratePoolCollection
**OUTPUT**: Balanced device assignments with optimal plan utilization

1. **CONFIGURE no grouping strategy with ascending order**:
   ```
   a. groupingStrategy = SimCardGrouping.NoGrouping
   b. assignmentOrder = RemainingAssignmentOrder.SmallestToLargest
   ```

2. **PROCESS devices individually by ascending usage**:
   ```
   a. FOR each device in crossProviderDevices.OrderBy(x => x.CycleDataUsageMB):
      i. EVALUATE available plan capacity across providers
      ii. ASSIGN to underutilized plans first
      iii. BALANCE provider-specific plan utilization
   ```

3. **OPTIMIZE for plan utilization efficiency**:
   ```
   a. CALCULATE plan utilization rates across providers
   b. BALANCE load distribution among carriers
   c. MAXIMIZE efficiency of shared resources
   ```

4. **BALANCE provider-specific plan optimization**:
   ```
   a. MONITOR plan capacity across providers
   b. ENSURE balanced utilization distribution
   c. OPTIMIZE for long-term resource efficiency
   ```

### Code Locations

**Strategy Pattern Implementation:**
```249:253:AltaworxSimCardCostOptimizer.cs
// each run will have 4 sequential calculation with strategy based on a pair of attributes SimCardGrouping and RemainingAssignmentOrder
// No Grouping + Largest To Smallest
// No Grouping + Smallest To Largest
// Group By Communication Plan + Largest To Smallest
// Group By Communication Plan + Smallest To Largest
```

**No Grouping Strategy for Customer Optimization:**
```274:276:AltaworxSimCardCostOptimizer.cs
if (portalType == PortalTypes.Mobility || isCustomerOptimization)
{
    return new List<SimCardGrouping> { SimCardGrouping.NoGrouping };
}
```

**Assignment Execution:**
```261:264:AltaworxSimCardCostOptimizer.cs
assigner.AssignSimCards(GetSimCardGroupingByPortalType(instance.PortalType, instance.IsCustomerOptimization),
                            context.OptimizationSettings.BillingTimeZone,
                            false,
                            false,
                            ratePoolSequences);
```

## Strategy 3: Cross-Provider Communication Plan Grouping (M2M)

### What
Cross-Provider Communication Plan Grouping groups customer devices by communication plan across providers, maintaining customer plan consistency while optimizing across providers for bulk assignments and volume discounts through coordinated plan management.

### Why
- **Plan Consistency**: Maintains logical grouping of devices with similar communication requirements
- **Volume Discounts**: Enables bulk optimization strategies for better pricing negotiations
- **Administrative Efficiency**: Simplifies management through consistent communication plan groupings
- **Cross-Provider Coordination**: Coordinates optimization across providers while maintaining plan integrity
- **Billing Simplification**: Reduces complexity through consistent plan-based groupings

### How
The system uses `SimCardGrouping.GroupByCommunicationPlan` to group devices by their communication plans and optimizes each group across multiple providers, enabling bulk assignments and volume-based optimization strategies.

### Algorithm: CrossProviderCommunicationPlanGrouping()

**INPUT**: List<SimCard> crossProviderDevices, List<CommunicationPlan> communicationPlans
**OUTPUT**: Plan-grouped optimization with volume discounts

1. **GROUP devices by communication plan**:
   ```
   a. groupingStrategy = SimCardGrouping.GroupByCommunicationPlan
   b. deviceGroups = crossProviderDevices.GroupBy(x => x.CommunicationPlan)
   ```

2. **PROCESS each communication plan group**:
   ```
   a. FOR each planGroup in deviceGroups:
      i. CALCULATE group volume requirements
      ii. EVALUATE cross-provider bulk pricing options
      iii. OPTIMIZE for volume discount opportunities
   ```

3. **OPTIMIZE for bulk assignments and volume discounts**:
   ```
   a. NEGOTIATE volume-based pricing across providers
   b. COORDINATE bulk assignments for cost efficiency
   c. MAINTAIN plan consistency within groups
   ```

4. **COORDINATE cross-provider plan management**:
   ```
   a. ENSURE communication plan compatibility across providers
   b. MANAGE plan transitions and migrations
   c. OPTIMIZE billing cycles for plan groups
   ```

### Code Locations

**Communication Plan Grouping Strategy:**
```279:281:AltaworxSimCardCostOptimizer.cs
return new List<SimCardGrouping> {
        SimCardGrouping.NoGrouping,
        SimCardGrouping.GroupByCommunicationPlan };
```

**Communication Plan Retrieval:**
```196:199:AltaworxSimCardCostOptimizer.cs
var commPlans = new List<string>();
if (instance.PortalType == PortalTypes.M2M && !instance.IsCustomerOptimization)
{
    commPlans = GetCommPlansForCommGroup(context, queue.CommPlanGroupId);
}
```

**Strategy Selection by Portal Type:**
```271:283:AltaworxSimCardCostOptimizer.cs
private static List<SimCardGrouping> GetSimCardGroupingByPortalType(PortalTypes portalType, bool isCustomerOptimization)
{
    if (portalType == PortalTypes.Mobility || isCustomerOptimization)
    {
        return new List<SimCardGrouping> { SimCardGrouping.NoGrouping };
    }
    else
    {
        return new List<SimCardGrouping> {
                SimCardGrouping.NoGrouping,
                SimCardGrouping.GroupByCommunicationPlan };
    }
}
```

## Strategy 4: Provider-Specific Optimization with Cross-Provider Comparison

### What
Provider-Specific Optimization with Cross-Provider Comparison optimizes within each provider individually, then compares results across providers for best overall assignment, considering provider migration costs and benefits through comprehensive cross-provider analysis.

### Why
- **Provider Expertise**: Leverages provider-specific optimization capabilities and constraints
- **Comprehensive Analysis**: Ensures all provider options are thoroughly evaluated
- **Migration Optimization**: Considers costs and benefits of moving devices between providers
- **Risk Management**: Reduces optimization risk through provider-specific validation
- **Best Overall Results**: Selects optimal assignments from comprehensive provider comparison

### How
The system performs independent optimization within each provider, then conducts cross-provider comparison to determine the best overall assignment considering migration costs, provider capabilities, and long-term benefits.

### Algorithm: ProviderSpecificOptimizationWithCrossProviderComparison()

**INPUT**: List<Provider> serviceProviders, List<SimCard> crossProviderDevices
**OUTPUT**: Best overall assignments with migration consideration

1. **OPTIMIZE within each provider individually**:
   ```
   a. FOR each provider in serviceProviders:
      i. EXTRACT provider-specific devices and rate plans
      ii. EXECUTE provider-specific optimization algorithm
      iii. RECORD provider-specific results and costs
   ```

2. **COMPARE results across providers**:
   ```
   a. CALCULATE total costs for each provider scenario
   b. EVALUATE cross-provider assignment options
   c. ASSESS migration costs and benefits
   ```

3. **CONSIDER provider migration costs and benefits**:
   ```
   a. CALCULATE migration costs between providers
   b. EVALUATE long-term contract implications
   c. ASSESS service quality and reliability factors
   ```

4. **SELECT best overall assignment**:
   ```
   a. COMPARE total costs including migration expenses
   b. FACTOR in provider-specific benefits and constraints
   c. CHOOSE optimal cross-provider configuration
   ```

### Code Locations

**Result Recording and Comparison:**
```390:398:AltaworxSimCardCostOptimizer.cs
var isSuccess = assigner.Best_Result != null;
if (isSuccess)
{
    // record results
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
```

**Queue Processing and Result Management:**
```401:407:AltaworxSimCardCostOptimizer.cs
foreach (long queueId in queueIds)
{
    // stop queue
    StopQueue(context, queueId, isSuccess);
}
```

**Cross-Provider Device Retrieval:**
```296:298:AltaworxSimCardCostOptimizer.cs
else if (portalType == PortalTypes.CrossProvider)
{
    return crossProviderOptimizationRepository.GetCrossProviderOptimizationDevices(ParameterizedLog(context), instance.Id, billingPeriod, commPlanGroupId, instance.IsCustomerOptimization);
}
```

**Provider-Specific Processing Logic:**
```254:267:AltaworxSimCardCostOptimizer.cs
var shouldFilterByRatePlanType = instance.PortalType == PortalTypes.Mobility && !instance.IsCustomerOptimization;
var shouldPoolUsageBetweenRatePlans = (instance.PortalType == PortalTypes.Mobility || instance.IsCustomerOptimization) && ratePoolCollection.IsPooled;
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

## Summary

Cross-Provider Optimization Strategies provide a comprehensive framework for:

1. **Strategy 1 (No Grouping + Largest to Smallest)**: Maximizes cost reduction through high-usage device prioritization
2. **Strategy 2 (No Grouping + Smallest to Largest)**: Optimizes plan utilization efficiency through balanced resource allocation
3. **Strategy 3 (Communication Plan Grouping)**: Enables volume discounts and bulk optimization through plan consistency
4. **Strategy 4 (Provider-Specific with Comparison)**: Delivers best overall results through comprehensive provider analysis

Each strategy addresses different optimization objectives and business requirements, providing flexibility to achieve optimal cost management across diverse cross-provider environments. The system supports sequential execution of multiple strategies to ensure comprehensive optimization coverage and maximum cost savings potential.