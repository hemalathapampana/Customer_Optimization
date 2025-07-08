# 2.3 Customer Assignment Strategies

## Overview
The system executes customer-focused assignment strategies to optimize SIM card rate plan assignments based on different customer objectives and usage patterns. Each strategy employs distinct algorithmic approaches to maximize cost efficiency and plan utilization.

---

## Strategy 1: Customer No Grouping + Largest to Smallest

### What
**Definition**: Processes customer devices individually without communication plan grouping, assigning highest usage customer devices first to optimal rate plans.

**Core Functionality**: Individual device processing with usage-based prioritization for maximum cost impact.

### Why
**Business Rationale**:
- **Maximum Cost Reduction**: High-usage devices have the greatest cost impact, so optimizing them first yields maximum savings
- **Customer Value Priority**: Addresses the most expensive devices first to deliver immediate customer value
- **Risk Mitigation**: Ensures high-cost devices are properly optimized before processing lower-impact devices
- **Customer Satisfaction**: Delivers the most visible cost improvements early in the optimization process

**Technical Justification**:
- High-usage devices consume more data and generate higher charges
- Rate plan optimization on high-usage devices produces larger absolute savings
- Greedy algorithm approach ensures local optimization leads to global optimization

### How
**Technical Implementation**:
1. **Device Retrieval**: Extract all customer devices without grouping constraints
2. **Usage Sorting**: Sort devices by data usage in descending order (largest first)
3. **Sequential Assignment**: Process devices one-by-one starting with highest usage
4. **Rate Plan Optimization**: Calculate optimal rate plan for each device based on usage patterns
5. **Cost Calculation**: Compute cost reduction for each assignment decision

### Algorithmic Explanation

```
ALGORITHM: CustomerNoGroupingLargestToSmallest
INPUT: 
    - customerDevices: Collection<SimCard>
    - availableRatePlans: Collection<RatePlan>
    - billingPeriod: BillingPeriod
    - optimizationContext: Context

STEP 1: Device Preparation
    devices ← GetOptimizationSimCards(customer, billingPeriod)
    filteredDevices ← devices.Where(d → HasValidRatePlanCode(d))

STEP 2: Usage-Based Sorting  
    sortedDevices ← filteredDevices.OrderByDescending(d → d.CycleDataUsageMB)
    
STEP 3: Rate Pool Creation
    calculatedPlans ← RatePoolCalculator.CalculateMaxAvgUsage(availableRatePlans, averageUsage)
    ratePools ← RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType)
    ratePoolCollection ← RatePoolCollectionFactory.CreateRatePoolCollection(ratePools)

STEP 4: Sequential Device Assignment
    totalCostReduction ← 0
    FOR EACH device IN sortedDevices DO:
        currentCost ← CalculateCurrentCost(device, device.currentRatePlan)
        
        optimalRatePlan ← NULL
        maxSavings ← 0
        
        FOR EACH ratePlan IN availableRatePlans DO:
            projectedCost ← CalculateProjectedCost(device, ratePlan)
            savings ← currentCost - projectedCost
            
            IF savings > maxSavings THEN:
                maxSavings ← savings
                optimalRatePlan ← ratePlan
            END IF
        END FOR
        
        IF optimalRatePlan ≠ NULL THEN:
            AssignDeviceToRatePlan(device, optimalRatePlan)
            totalCostReduction ← totalCostReduction + maxSavings
            LogAssignment(device, optimalRatePlan, maxSavings)
        END IF
    END FOR

STEP 5: Assignment Execution
    assigner ← NEW RatePoolAssigner(ratePoolCollection, sortedDevices, context)
    assigner.AssignSimCards(SimCardGrouping.NoGrouping, billingTimeZone, 
                           useRemainingOrderLargestToSmallest: true)

OUTPUT: Optimized device assignments with maximum cost reduction priority
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

**File: `AltaworxSimCardCostQueueCustomerOptimization.cs`**

**Lines 511-515: Device Filtering**
```csharp
var optimizationSimCards = GetOptimizationSimCards(context, null, billingPeriod.ServiceProviderId, revAccountNumber, integrationAuthenticationId, billingPeriod.Id, tenantId, customerType, AMOPCustomerId);
if (revAccountNumber != null || AMOPCustomerId != null)
{
    optimizationSimCards = optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList();
}
```

---

## Strategy 2: Customer No Grouping + Smallest to Largest

### What
**Definition**: Processes customer devices individually without communication plan grouping, assigning lowest usage customer devices first to optimize plan utilization efficiency.

**Core Functionality**: Individual device processing with reverse usage prioritization for plan capacity optimization.

### Why
**Business Rationale**:
- **Plan Utilization Optimization**: Low-usage devices can efficiently utilize plan base allowances without triggering overage charges
- **Capacity Management**: Fills available plan capacity systematically before moving to higher-tier plans
- **Cost Efficiency**: Maximizes the value extracted from plan base allowances
- **Resource Optimization**: Prevents waste of plan capacity by strategic device placement

**Technical Justification**:
- Low-usage devices are less likely to exceed plan limits
- Filling plans with small devices first optimizes overall capacity utilization
- Reduces the likelihood of overage charges across the customer portfolio
- Balances load distribution across available rate plans

### How
**Technical Implementation**:
1. **Device Retrieval**: Extract all customer devices without grouping constraints
2. **Reverse Usage Sorting**: Sort devices by data usage in ascending order (smallest first)
3. **Capacity-Based Assignment**: Assign devices to plans with available capacity
4. **Utilization Monitoring**: Track plan capacity usage during assignment process
5. **Efficiency Calculation**: Optimize for maximum plan utilization without overages

### Algorithmic Explanation

```
ALGORITHM: CustomerNoGroupingSmallestToLargest
INPUT: 
    - customerDevices: Collection<SimCard>
    - availableRatePlans: Collection<RatePlan>
    - billingPeriod: BillingPeriod
    - optimizationContext: Context

STEP 1: Device Preparation
    devices ← GetOptimizationSimCards(customer, billingPeriod)
    filteredDevices ← devices.Where(d → HasValidRatePlanCode(d))

STEP 2: Reverse Usage Sorting
    sortedDevices ← filteredDevices.OrderBy(d → d.CycleDataUsageMB)

STEP 3: Plan Capacity Analysis
    FOR EACH ratePlan IN availableRatePlans DO:
        ratePlan.remainingCapacity ← ratePlan.dataAllowance
        ratePlan.assignedDevices ← EMPTY_LIST
    END FOR

STEP 4: Capacity-Based Assignment
    totalUtilizationEfficiency ← 0
    
    FOR EACH device IN sortedDevices DO:
        deviceUsage ← device.CycleDataUsageMB
        bestPlan ← NULL
        bestUtilization ← 0
        
        FOR EACH ratePlan IN availableRatePlans DO:
            IF ratePlan.remainingCapacity >= deviceUsage THEN:
                utilization ← (ratePlan.dataAllowance - ratePlan.remainingCapacity + deviceUsage) / ratePlan.dataAllowance
                cost ← CalculateTotalCost(ratePlan, ratePlan.assignedDevices + device)
                
                efficiency ← utilization / cost
                
                IF efficiency > bestUtilization THEN:
                    bestUtilization ← efficiency
                    bestPlan ← ratePlan
                END IF
            END IF
        END FOR
        
        IF bestPlan ≠ NULL THEN:
            AssignDeviceToRatePlan(device, bestPlan)
            bestPlan.remainingCapacity ← bestPlan.remainingCapacity - deviceUsage
            bestPlan.assignedDevices.Add(device)
            totalUtilizationEfficiency ← totalUtilizationEfficiency + bestUtilization
        END IF
    END FOR

STEP 5: Assignment Execution
    assigner ← NEW RatePoolAssigner(ratePoolCollection, sortedDevices, context)
    assigner.AssignSimCards(SimCardGrouping.NoGrouping, billingTimeZone,
                           useRemainingOrderSmallestToLargest: true)

OUTPUT: Optimized device assignments with maximum plan utilization efficiency
```

### Code Location

**File: `AltaworxSimCardCostOptimizer.cs`**

**Lines 271-282: No Grouping Strategy Selection**
```csharp
if (portalType == PortalTypes.Mobility || isCustomerOptimization)
{
    return new List<SimCardGrouping> { SimCardGrouping.NoGrouping };
}
```

**Lines 261-266: Assignment Parameters**
```csharp
assigner.AssignSimCards(GetSimCardGroupingByPortalType(instance.PortalType, instance.IsCustomerOptimization),
                            context.OptimizationSettings.BillingTimeZone,
                            false,
                            false,
                            ratePoolSequences);
```

**File: `AltaworxSimCardCostQueueCustomerOptimization.cs`**

**Lines 594-596: Base Device Assignment Implementation**
```csharp
var baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId,
    revAccountNumber, integrationAuthenticationId, null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId);
```

**Lines 590-593: Rate Pool Configuration**
```csharp
var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);
```

---

## Strategy 3: Customer Communication Plan Grouping (M2M only)

### What
**Definition**: Groups customer devices by communication plan assignments, processing devices within the same communication plan together to maintain plan consistency and optimize bulk assignments.

**Core Functionality**: Communication plan-based grouping with bulk assignment optimization for M2M customer scenarios.

### Why
**Business Rationale**:
- **Plan Consistency**: Maintains coherent communication plan assignments across device groups
- **Bulk Assignment Efficiency**: Leverages economies of scale for devices with similar communication requirements
- **Operational Simplicity**: Reduces complexity in managing diverse communication plan assignments
- **M2M Optimization**: Specifically designed for Machine-to-Machine communication patterns where devices often share similar usage profiles

**Technical Justification**:
- M2M devices typically have predictable, consistent usage patterns
- Communication plan grouping enables more efficient rate plan negotiations
- Bulk assignments reduce administrative overhead
- Grouped devices can share pooled resources more effectively

### How
**Technical Implementation**:
1. **Communication Plan Grouping**: Group devices by their assigned communication plans
2. **Bulk Processing**: Process entire communication plan groups together
3. **Consistency Maintenance**: Ensure all devices in a group maintain plan coherence
4. **Group-Level Optimization**: Apply optimization algorithms at the communication plan group level
5. **Bulk Assignment**: Assign rate plans to entire groups rather than individual devices

### Algorithmic Explanation

```
ALGORITHM: CustomerCommunicationPlanGrouping
INPUT: 
    - customerDevices: Collection<SimCard>
    - availableRatePlans: Collection<RatePlan>
    - communicationPlans: Collection<CommunicationPlan>
    - billingPeriod: BillingPeriod
    - optimizationContext: Context

STEP 1: Communication Plan Group Creation
    IF instance.PortalType == PortalTypes.M2M AND NOT instance.IsCustomerOptimization THEN:
        commPlans ← GetCommPlansForCommGroup(context, queue.CommPlanGroupId)
        deviceGroups ← GroupDevicesByCommunicationPlan(customerDevices, commPlans)
    ELSE:
        RETURN "Strategy not applicable for this portal type"
    END IF

STEP 2: Group-Level Analysis
    FOR EACH commPlanGroup IN deviceGroups DO:
        groupUsage ← SUM(device.CycleDataUsageMB FOR device IN commPlanGroup.devices)
        groupAverageUsage ← groupUsage / commPlanGroup.devices.Count
        groupPeakUsage ← MAX(device.CycleDataUsageMB FOR device IN commPlanGroup.devices)
        
        commPlanGroup.usageProfile ← {
            totalUsage: groupUsage,
            averageUsage: groupAverageUsage,
            peakUsage: groupPeakUsage,
            deviceCount: commPlanGroup.devices.Count
        }
    END FOR

STEP 3: Group Rate Plan Optimization
    FOR EACH commPlanGroup IN deviceGroups DO:
        applicableRatePlans ← FilterRatePlansByCommunicationPlan(availableRatePlans, commPlanGroup.communicationPlan)
        
        bestGroupAssignment ← NULL
        bestGroupCost ← INFINITY
        
        FOR EACH ratePlan IN applicableRatePlans DO:
            groupCost ← CalculateGroupCost(commPlanGroup, ratePlan)
            consistency ← EvaluatePlanConsistency(commPlanGroup, ratePlan)
            bulkDiscount ← CalculateBulkDiscount(commPlanGroup.devices.Count, ratePlan)
            
            totalCost ← groupCost - bulkDiscount
            
            IF totalCost < bestGroupCost AND consistency >= MINIMUM_CONSISTENCY_THRESHOLD THEN:
                bestGroupCost ← totalCost
                bestGroupAssignment ← ratePlan
            END IF
        END FOR
        
        IF bestGroupAssignment ≠ NULL THEN:
            FOR EACH device IN commPlanGroup.devices DO:
                AssignDeviceToRatePlan(device, bestGroupAssignment)
            END FOR
            LogGroupAssignment(commPlanGroup, bestGroupAssignment, bestGroupCost)
        END IF
    END FOR

STEP 4: Bulk Assignment Execution
    commPlanGroupId ← CreateCommPlanGroup(context, instanceId)
    
    FOR EACH deviceGroup IN deviceGroups DO:
        calculatedPlans ← RatePoolCalculator.CalculateMaxAvgUsage(deviceGroup.applicableRatePlans, deviceGroup.usageProfile.averageUsage)
        ratePools ← RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType)
        ratePoolCollection ← RatePoolCollectionFactory.CreateRatePoolCollection(ratePools)
        
        baseAssignedCount ← BaseDeviceAssignment(context, instanceId, commPlanGroupId, serviceProviderId,
                                                revAccountNumber, integrationAuthenticationId, null, 
                                                ratePoolCollection, ratePools, deviceGroup.devices, 
                                                billingPeriod, usesProration, AMOPCustomerId)
    END FOR

STEP 5: Group-Level Assignment
    assigner ← NEW RatePoolAssigner(ratePoolCollection, allGroupedDevices, context)
    assigner.AssignSimCards(SimCardGrouping.GroupByCommunicationPlan, billingTimeZone, 
                           maintainGroupConsistency: true)

OUTPUT: Bulk-optimized device assignments with communication plan consistency
```

### Code Location

**File: `AltaworxSimCardCostOptimizer.cs`**

**Lines 271-282: Communication Plan Grouping Strategy Selection**
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

**Lines 213-215: M2M Specific Processing Configuration**
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

**Lines 594-596: Base Device Assignment for Groups**
```csharp
var baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId,
    revAccountNumber, integrationAuthenticationId, null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId);
```

---

## Strategy Selection Logic

### Portal Type Determination

**File: `AltaworxSimCardCostOptimizer.cs`**

**Lines 271-282: Strategy Selection Based on Portal Type and Customer Optimization Flag**
```csharp
private static List<SimCardGrouping> GetSimCardGroupingByPortalType(PortalTypes portalType, bool isCustomerOptimization)
{
    // Customer Optimization and Mobility Portal: Use No Grouping strategies only
    if (portalType == PortalTypes.Mobility || isCustomerOptimization)
    {
        return new List<SimCardGrouping> { SimCardGrouping.NoGrouping };
    }
    // M2M Portal: Use both No Grouping and Communication Plan Grouping strategies
    else
    {
        return new List<SimCardGrouping> {
                SimCardGrouping.NoGrouping,
                SimCardGrouping.GroupByCommunicationPlan };
    }
}
```

### Strategy Execution Flow

**File: `AltaworxSimCardCostOptimizer.cs`**

**Lines 249-253: Multi-Strategy Sequential Processing**
```csharp
// each run will have 4 sequential calculation with strategy based on a pair of attributes SimCardGrouping and RemainingAssignmentOrder
// No Grouping + Largest To Smallest
// No Grouping + Smallest To Largest  
// Group By Communication Plan + Largest To Smallest
// Group By Communication Plan + Smallest To Largest
```

## Key Implementation Insights

### Strategy Differentiation
- **Customer Optimization**: Always uses `SimCardGrouping.NoGrouping` regardless of portal type
- **M2M Portal**: Supports both `NoGrouping` and `GroupByCommunicationPlan` strategies
- **Mobility Portal**: Limited to `SimCardGrouping.NoGrouping` strategies only

### Processing Order Impact
- **Largest to Smallest**: Greedy approach for immediate maximum cost impact
- **Smallest to Largest**: Capacity optimization approach for maximum efficiency
- **Communication Plan Grouping**: Bulk processing approach for operational consistency

### Assignment Execution Control
- **Rate Pool Calculation**: `RatePoolCalculator.CalculateMaxAvgUsage()` determines optimal rate pools
- **Assignment Engine**: `RatePoolAssigner.AssignSimCards()` executes the strategy
- **Base Assignment**: `BaseDeviceAssignment()` handles initial device-to-plan mappings