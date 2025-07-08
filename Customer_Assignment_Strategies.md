# Customer-Focused Assignment Strategies

## Strategy 1: Customer No Grouping + Largest to Smallest

### Algorithm
```
1. Process customer devices individually without communication plan grouping
2. Sort customer devices by data usage in descending order (largest first)
3. Assign highest usage customer devices to optimal rate plans first
4. Calculate cost reduction for maximum customer benefit
5. Continue assignment until all devices processed
```

### Code Location

**File: `AltaworxSimCardCostOptimizer.cs`**

**Lines 249-253: Strategy Documentation**
```csharp
// each run will have 4 sequential calculation with strategy based on a pair of attributes SimCardGrouping and RemainingAssignmentOrder
// No Grouping + Largest To Smallest
// No Grouping + Smallest To Largest
// Group By Communication Plan + Largest To Smallest
// Group By Communication Plan + Smallest To Largest
```

**Lines 271-282: Strategy Selection Logic**
```csharp
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

**Lines 257-266: Assignment Execution**
```csharp
var assigner = new RatePoolAssigner(string.Empty, ratePoolCollection, simCards, context.logger, SanityCheckTimeLimit, context.LambdaContext, IsUsingRedisCache,
    instance.PortalType,
    shouldFilterByRatePlanType,
    shouldPoolUsageBetweenRatePlans);
assigner.AssignSimCards(GetSimCardGroupingByPortalType(instance.PortalType, instance.IsCustomerOptimization),
                            context.OptimizationSettings.BillingTimeZone,
                            false,
                            false,
                            ratePoolSequences);
```

## Strategy 2: Customer No Grouping + Smallest to Largest

### Algorithm
```
1. Process customer devices individually without communication plan grouping
2. Sort customer devices by data usage in ascending order (smallest first)
3. Assign lowest usage customer devices to rate plans first
4. Optimize for customer plan utilization efficiency
5. Continue assignment prioritizing plan capacity utilization
```

### Code Location

**File: `AltaworxSimCardCostOptimizer.cs`**

**Lines 249-253: Strategy Documentation**
```csharp
// each run will have 4 sequential calculation with strategy based on a pair of attributes SimCardGrouping and RemainingAssignmentOrder
// No Grouping + Largest To Smallest
// No Grouping + Smallest To Largest
// Group By Communication Plan + Largest To Smallest
// Group By Communication Plan + Smallest To Largest
```

**Lines 271-282: No Grouping Strategy Selection**
```csharp
if (portalType == PortalTypes.Mobility || isCustomerOptimization)
{
    return new List<SimCardGrouping> { SimCardGrouping.NoGrouping };
}
```

**File: `AltaworxSimCardCostQueueCustomerOptimization.cs`**

**Lines 594-596: Base Device Assignment Implementation**
```csharp
var baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId,
    revAccountNumber, integrationAuthenticationId, null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId);
```

## Strategy 3: Customer Communication Plan Grouping (M2M only)

### Algorithm
```
1. Group customer devices by communication plan assignments
2. Process devices within same communication plan together
3. Maintain customer plan consistency across device groups
4. Optimize for customer bulk assignments and plan coherence
5. Apply rate plan assignments at communication plan group level
```

### Code Location

**File: `AltaworxSimCardCostOptimizer.cs`**

**Lines 271-282: Communication Plan Grouping Strategy**
```csharp
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

**Lines 192-200: Communication Plan Retrieval**
```csharp
// If M2M carrier optimization, use comm plans for optimization
var commPlans = new List<string>();
if (instance.PortalType == PortalTypes.M2M && !instance.IsCustomerOptimization)
{
    commPlans = GetCommPlansForCommGroup(context, queue.CommPlanGroupId);
}
```

**Lines 213-215: M2M Specific Processing**
```csharp
// If no customer rate pool -> must optimize using existing implementation (not filter by rate plan code)
var shouldFilterByRatePlanCode = false;
```

**File: `AltaworxSimCardCostQueueCustomerOptimization.cs`**

**Lines 590-595: Communication Plan Group Creation**
```csharp
// create new comm plan group
var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);
```

## Strategy Selection Logic

### Portal Type Determination
**File: `AltaworxSimCardCostOptimizer.cs`**

**Lines 271-282: Strategy Selection Based on Portal Type**
```csharp
private static List<SimCardGrouping> GetSimCardGroupingByPortalType(PortalTypes portalType, bool isCustomerOptimization)
{
    // Customer Optimization and Mobility use No Grouping only
    if (portalType == PortalTypes.Mobility || isCustomerOptimization)
    {
        return new List<SimCardGrouping> { SimCardGrouping.NoGrouping };
    }
    // M2M uses both No Grouping and Communication Plan Grouping
    else
    {
        return new List<SimCardGrouping> {
                SimCardGrouping.NoGrouping,
                SimCardGrouping.GroupByCommunicationPlan };
    }
}
```

## Assignment Order Implementation

### Sequential Strategy Execution
**File: `AltaworxSimCardCostOptimizer.cs`**

**Lines 249-253: Multi-Strategy Processing**
```csharp
// each run will have 4 sequential calculation with strategy based on a pair of attributes SimCardGrouping and RemainingAssignmentOrder
// No Grouping + Largest To Smallest
// No Grouping + Smallest To Largest  
// Group By Communication Plan + Largest To Smallest
// Group By Communication Plan + Smallest To Largest
```

**Lines 257-266: Strategy Execution**
```csharp
var assigner = new RatePoolAssigner(string.Empty, ratePoolCollection, simCards, context.logger, SanityCheckTimeLimit, context.LambdaContext, IsUsingRedisCache,
    instance.PortalType,
    shouldFilterByRatePlanType,
    shouldPoolUsageBetweenRatePlans);
assigner.AssignSimCards(GetSimCardGroupingByPortalType(instance.PortalType, instance.IsCustomerOptimization),
                            context.OptimizationSettings.BillingTimeZone,
                            false,
                            false,
                            ratePoolSequences);
```

## Key Implementation Points

### Strategy Differentiation
- **Customer Optimization**: Uses only `SimCardGrouping.NoGrouping`
- **M2M Portal**: Uses both `NoGrouping` and `GroupByCommunicationPlan`
- **Mobility Portal**: Uses only `SimCardGrouping.NoGrouping`

### Processing Order
- **Largest to Smallest**: Prioritizes high-usage devices for maximum cost reduction
- **Smallest to Largest**: Prioritizes low-usage devices for plan utilization efficiency
- **Communication Plan Grouping**: Groups devices by communication plan for consistency

### Assignment Control
- Base device assignment handled by `BaseDeviceAssignment()` method
- Rate pool calculation via `RatePoolCalculator.CalculateMaxAvgUsage()`
- Assignment execution through `RatePoolAssigner.AssignSimCards()`