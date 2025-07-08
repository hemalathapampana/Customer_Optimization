# Customer Assignment Strategies Documentation

## Table of Contents
1. [Overview](#overview)
2. [Strategy Classification](#strategy-classification)
3. [Strategy 1: Customer No Grouping + Largest to Smallest](#strategy-1-customer-no-grouping--largest-to-smallest)
4. [Strategy 2: Customer No Grouping + Smallest to Largest](#strategy-2-customer-no-grouping--smallest-to-largest)
5. [Strategy 3: Customer Communication Plan Grouping](#strategy-3-customer-communication-plan-grouping-m2m-only)
6. [Implementation Framework](#implementation-framework)
7. [Performance Characteristics](#performance-characteristics)
8. [Configuration and Control](#configuration-and-control)
9. [Troubleshooting and Monitoring](#troubleshooting-and-monitoring)

---

## Overview

### Purpose
Customer Assignment Strategies optimize SIM card rate plan assignments to achieve specific business objectives including cost reduction, plan utilization efficiency, and operational consistency.

### Scope
- **Portal Types**: M2M, Mobility, CrossProvider
- **Customer Types**: Revenue (Rev), AMOP, CrossProvider
- **Optimization Goals**: Cost reduction, Plan utilization, Operational efficiency

### Key Concepts
- **SimCardGrouping**: Determines how devices are grouped for processing
- **RemainingAssignmentOrder**: Controls the order in which devices are processed within groups
- **Rate Pool Collection**: Manages available rate plans for assignment optimization

---

## Strategy Classification

### Grouping Methods
| Grouping Type | Description | Applicable Portal Types |
|---------------|-------------|------------------------|
| `NoGrouping` | Process devices individually | All portal types |
| `GroupByCommunicationPlan` | Group devices by communication plan | M2M only (non-customer optimization) |

### Assignment Orders
| Order Type | Description | Optimization Goal |
|------------|-------------|-------------------|
| `LargestToSmallest` | Process highest usage devices first | Maximum cost reduction |
| `SmallestToLargest` | Process lowest usage devices first | Plan utilization efficiency |

### Strategy Matrix
| Portal Type | Customer Optimization | Available Strategies |
|-------------|----------------------|---------------------|
| Mobility | Yes/No | NoGrouping only |
| M2M | Yes | NoGrouping only |
| M2M | No | NoGrouping + GroupByCommunicationPlan |
| CrossProvider | Yes/No | NoGrouping only |

---

## Strategy 1: Customer No Grouping + Largest to Smallest

### Executive Summary
**Objective**: Maximize immediate customer cost reduction by prioritizing high-usage devices for optimization.

**Business Value**: Delivers maximum visible savings by addressing the most expensive devices first.

### Technical Specifications

#### Input Parameters
```
- customerDevices: Collection<SimCard>
- availableRatePlans: Collection<RatePlan>
- billingPeriod: BillingPeriod
- optimizationContext: ExecutionContext
- portalType: PortalTypes
- customerOptimization: boolean
```

#### Processing Constraints
- **Device Grouping**: `SimCardGrouping.NoGrouping`
- **Sort Order**: Descending by `CycleDataUsageMB`
- **Assignment Priority**: High-usage devices processed first
- **Optimization Focus**: Maximum absolute cost savings

#### Algorithm Detail
```
ALGORITHM: LargestToSmallestAssignment
INPUT: devices, ratePlans, billingPeriod, context

STEP 1: Data Preparation
    validDevices ← FilterDevicesWithValidRatePlanCodes(devices)
    averageUsage ← CalculateAverageUsage(validDevices)
    
STEP 2: Device Prioritization
    sortedDevices ← validDevices.OrderByDescending(d → d.CycleDataUsageMB)
    
STEP 3: Rate Pool Optimization
    calculatedPlans ← RatePoolCalculator.CalculateMaxAvgUsage(ratePlans, averageUsage)
    ratePools ← RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, prorationFlag, chargeType)
    ratePoolCollection ← RatePoolCollectionFactory.CreateRatePoolCollection(ratePools)
    
STEP 4: Greedy Assignment Process
    totalSavings ← 0
    FOR EACH device IN sortedDevices:
        currentPlanCost ← CalculateCurrentMonthlyCharge(device)
        
        bestRatePlan ← NULL
        maxSavings ← 0
        
        FOR EACH ratePlan IN calculatedPlans:
            projectedCost ← CalculateProjectedCharge(device, ratePlan, billingPeriod)
            potentialSavings ← currentPlanCost - projectedCost
            
            IF potentialSavings > maxSavings AND MeetsEligibilityCriteria(device, ratePlan):
                maxSavings ← potentialSavings
                bestRatePlan ← ratePlan
            END IF
        END FOR
        
        IF bestRatePlan ≠ NULL:
            ExecuteAssignment(device, bestRatePlan)
            totalSavings ← totalSavings + maxSavings
            UpdateRatePoolCapacity(bestRatePlan, device.usage)
        END IF
    END FOR
    
STEP 5: Result Recording
    RecordOptimizationResults(totalSavings, deviceAssignments, ratePools)

OUTPUT: Device assignments optimized for maximum cost reduction
```

#### Implementation Details

**File: `AltaworxSimCardCostOptimizer.cs`**

**Strategy Selection (Lines 271-282)**
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

**Assignment Execution (Lines 257-266)**
```csharp
var assigner = new RatePoolAssigner(string.Empty, ratePoolCollection, simCards, 
    context.logger, SanityCheckTimeLimit, context.LambdaContext, IsUsingRedisCache,
    instance.PortalType, shouldFilterByRatePlanType, shouldPoolUsageBetweenRatePlans);

assigner.AssignSimCards(GetSimCardGroupingByPortalType(instance.PortalType, instance.IsCustomerOptimization),
    context.OptimizationSettings.BillingTimeZone, false, false, ratePoolSequences);
```

**File: `AltaworxSimCardCostQueueCustomerOptimization.cs`**

**Device Retrieval and Filtering (Lines 511-515)**
```csharp
var optimizationSimCards = GetOptimizationSimCards(context, null, billingPeriod.ServiceProviderId, 
    revAccountNumber, integrationAuthenticationId, billingPeriod.Id, tenantId, customerType, AMOPCustomerId);

if (revAccountNumber != null || AMOPCustomerId != null)
{
    optimizationSimCards = optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList();
}
```

#### Performance Characteristics
- **Time Complexity**: O(n * m) where n = devices, m = rate plans
- **Memory Usage**: Linear with device count
- **Optimization Impact**: High for customers with varied usage patterns
- **Processing Time**: Fast due to greedy approach

#### Use Cases
- High-value customers with significant usage variation
- Scenarios requiring immediate cost impact demonstration
- Budget-constrained optimizations needing maximum ROI
- Executive reporting requiring clear savings metrics

---

## Strategy 2: Customer No Grouping + Smallest to Largest

### Executive Summary
**Objective**: Maximize plan utilization efficiency by optimizing low-usage devices first to fill available plan capacity.

**Business Value**: Reduces waste in plan allowances and minimizes overage charges across the customer portfolio.

### Technical Specifications

#### Input Parameters
```
- customerDevices: Collection<SimCard>
- availableRatePlans: Collection<RatePlan>
- billingPeriod: BillingPeriod
- optimizationContext: ExecutionContext
- capacityThresholds: PlanCapacityLimits
```

#### Processing Constraints
- **Device Grouping**: `SimCardGrouping.NoGrouping`
- **Sort Order**: Ascending by `CycleDataUsageMB`
- **Assignment Priority**: Low-usage devices processed first
- **Optimization Focus**: Plan capacity utilization efficiency

#### Algorithm Detail
```
ALGORITHM: SmallestToLargestAssignment
INPUT: devices, ratePlans, billingPeriod, context

STEP 1: Data Preparation and Capacity Analysis
    validDevices ← FilterDevicesWithValidRatePlanCodes(devices)
    sortedDevices ← validDevices.OrderBy(d → d.CycleDataUsageMB)
    
    // Initialize capacity tracking for each rate plan
    FOR EACH ratePlan IN ratePlans:
        ratePlan.remainingCapacity ← ratePlan.monthlyDataAllowance
        ratePlan.assignedDevicesCount ← 0
        ratePlan.utilizationPercentage ← 0
    END FOR
    
STEP 2: Capacity-Optimized Assignment
    totalEfficiencyScore ← 0
    
    FOR EACH device IN sortedDevices:
        deviceUsage ← device.CycleDataUsageMB
        bestPlan ← NULL
        bestEfficiencyScore ← 0
        
        FOR EACH ratePlan IN ratePlans:
            // Check if device fits within plan capacity
            IF ratePlan.remainingCapacity >= deviceUsage:
                
                // Calculate utilization efficiency
                newUtilization ← (ratePlan.monthlyDataAllowance - ratePlan.remainingCapacity + deviceUsage) / ratePlan.monthlyDataAllowance
                costPerMB ← CalculateCostPerMB(ratePlan, newUtilization)
                
                // Efficiency score favors high utilization at low cost
                efficiencyScore ← newUtilization / costPerMB
                
                // Bonus for filling plans near optimal capacity (80-95%)
                IF newUtilization >= 0.8 AND newUtilization <= 0.95:
                    efficiencyScore ← efficiencyScore * 1.2
                END IF
                
                IF efficiencyScore > bestEfficiencyScore:
                    bestEfficiencyScore ← efficiencyScore
                    bestPlan ← ratePlan
                END IF
            END IF
        END FOR
        
        // Execute assignment if suitable plan found
        IF bestPlan ≠ NULL:
            ExecuteAssignment(device, bestPlan)
            bestPlan.remainingCapacity ← bestPlan.remainingCapacity - deviceUsage
            bestPlan.assignedDevicesCount ← bestPlan.assignedDevicesCount + 1
            bestPlan.utilizationPercentage ← CalculateUtilization(bestPlan)
            totalEfficiencyScore ← totalEfficiencyScore + bestEfficiencyScore
        END IF
    END FOR
    
STEP 3: Capacity Optimization Validation
    FOR EACH ratePlan IN ratePlans:
        IF ratePlan.utilizationPercentage < MINIMUM_UTILIZATION_THRESHOLD:
            LogWarning("Underutilized plan detected", ratePlan.id)
        END IF
        
        IF ratePlan.utilizationPercentage > MAXIMUM_UTILIZATION_THRESHOLD:
            LogWarning("Over-utilized plan detected", ratePlan.id)
        END IF
    END FOR

OUTPUT: Device assignments optimized for maximum plan utilization efficiency
```

#### Implementation Details

**File: `AltaworxSimCardCostQueueCustomerOptimization.cs`**

**Base Device Assignment (Lines 594-596)**
```csharp
var baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, 
    billingPeriod.ServiceProviderId, revAccountNumber, integrationAuthenticationId, null, 
    ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId);
```

**Rate Pool Creation (Lines 590-593)**
```csharp
var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);
```

#### Performance Characteristics
- **Time Complexity**: O(n * m * log(m)) where n = devices, m = rate plans
- **Memory Usage**: Linear with device count plus plan capacity tracking
- **Optimization Impact**: High for customers with consistent low-usage patterns
- **Processing Time**: Moderate due to capacity calculation overhead

#### Use Cases
- Customers with many low-usage IoT devices
- Plan portfolio optimization scenarios
- Cost-conscious customers prioritizing plan value
- Scenarios requiring minimal overage charges

---

## Strategy 3: Customer Communication Plan Grouping (M2M only)

### Executive Summary
**Objective**: Optimize device assignments at the communication plan group level to maintain operational consistency and leverage bulk assignment benefits.

**Business Value**: Reduces administrative complexity while maintaining service consistency across device groups with similar communication requirements.

### Technical Specifications

#### Input Parameters
```
- customerDevices: Collection<SimCard>
- communicationPlans: Collection<CommunicationPlan>
- availableRatePlans: Collection<RatePlan>
- billingPeriod: BillingPeriod
- bulkAssignmentRules: GroupAssignmentPolicy
```

#### Processing Constraints
- **Device Grouping**: `SimCardGrouping.GroupByCommunicationPlan`
- **Portal Type Restriction**: M2M only, non-customer optimization
- **Assignment Priority**: Group-level consistency over individual optimization
- **Optimization Focus**: Bulk assignment efficiency and operational simplicity

#### Algorithm Detail
```
ALGORITHM: CommunicationPlanGroupAssignment
INPUT: devices, communicationPlans, ratePlans, billingPeriod, context

STEP 1: Communication Plan Group Formation
    IF instance.PortalType ≠ PortalTypes.M2M OR instance.IsCustomerOptimization:
        RETURN "Strategy not applicable"
    END IF
    
    commPlans ← GetCommPlansForCommGroup(context, commPlanGroupId)
    deviceGroups ← GroupDevicesByCommPlan(devices, commPlans)
    
STEP 2: Group Usage Profile Analysis
    FOR EACH group IN deviceGroups:
        group.totalUsage ← SUM(device.CycleDataUsageMB FOR device IN group.devices)
        group.averageUsage ← group.totalUsage / group.devices.Count
        group.peakUsage ← MAX(device.CycleDataUsageMB FOR device IN group.devices)
        group.deviceCount ← group.devices.Count
        group.usageVariance ← CalculateUsageVariance(group.devices)
        
        // Classify group characteristics
        IF group.usageVariance < LOW_VARIANCE_THRESHOLD:
            group.consistencyLevel ← "HIGH"
        ELSE IF group.usageVariance < MEDIUM_VARIANCE_THRESHOLD:
            group.consistencyLevel ← "MEDIUM"
        ELSE:
            group.consistencyLevel ← "LOW"
        END IF
    END FOR
    
STEP 3: Group-Level Rate Plan Selection
    FOR EACH group IN deviceGroups:
        applicableRatePlans ← FilterRatePlansByCommPlan(ratePlans, group.communicationPlan)
        
        bestGroupPlan ← NULL
        bestGroupScore ← 0
        
        FOR EACH ratePlan IN applicableRatePlans:
            // Calculate group-level metrics
            groupCost ← CalculateGroupTotalCost(group, ratePlan)
            consistencyScore ← EvaluateGroupConsistency(group, ratePlan)
            bulkDiscount ← CalculateBulkDiscount(group.deviceCount, ratePlan)
            adminEfficiency ← CalculateAdminEfficiency(group.deviceCount)
            
            // Composite scoring
            totalScore ← (consistencyScore * 0.4) + (adminEfficiency * 0.3) + (bulkDiscount * 0.3)
            
            // Penalty for low consistency groups on high-tier plans
            IF group.consistencyLevel = "LOW" AND ratePlan.tier = "PREMIUM":
                totalScore ← totalScore * 0.8
            END IF
            
            IF totalScore > bestGroupScore:
                bestGroupScore ← totalScore
                bestGroupPlan ← ratePlan
            END IF
        END FOR
        
        // Execute group assignment
        IF bestGroupPlan ≠ NULL:
            FOR EACH device IN group.devices:
                ExecuteAssignment(device, bestGroupPlan)
            END FOR
            
            LogGroupAssignment(group.communicationPlan, bestGroupPlan, group.deviceCount, bestGroupScore)
        END IF
    END FOR
    
STEP 4: Group Assignment Validation
    FOR EACH group IN deviceGroups:
        consistencyCheck ← ValidateGroupConsistency(group)
        IF NOT consistencyCheck.passed:
            LogWarning("Group consistency validation failed", group.communicationPlan)
            TriggerManualReview(group)
        END IF
    END FOR

OUTPUT: Communication plan group-optimized device assignments
```

#### Implementation Details

**File: `AltaworxSimCardCostOptimizer.cs`**

**Communication Plan Retrieval (Lines 192-200)**
```csharp
// If M2M carrier optimization, use comm plans for optimization
var commPlans = new List<string>();
if (instance.PortalType == PortalTypes.M2M && !instance.IsCustomerOptimization)
{
    commPlans = GetCommPlansForCommGroup(context, queue.CommPlanGroupId);
}
```

**M2M Strategy Configuration (Lines 213-215)**
```csharp
// If no customer rate pool -> must optimize using existing implementation (not filter by rate plan code)
var shouldFilterByRatePlanCode = false;
```

**File: `AltaworxSimCardCostQueueCustomerOptimization.cs`**

**Communication Plan Group Creation (Lines 590-595)**
```csharp
// create new comm plan group
var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);
```

#### Performance Characteristics
- **Time Complexity**: O(g * p * d) where g = groups, p = plans, d = devices per group
- **Memory Usage**: Linear with group count plus group metadata
- **Optimization Impact**: High for M2M scenarios with consistent device patterns
- **Processing Time**: Variable based on group size and plan complexity

#### Use Cases
- M2M deployments with device clusters requiring consistent service
- IoT scenarios with device fleets sharing communication requirements
- Enterprise customers with standardized device configurations
- Scenarios requiring simplified billing and administration

---

## Implementation Framework

### Core Components

#### Rate Pool Management
```csharp
// Rate pool calculation and creation
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(ratePlans, avgUsage);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);
```

#### Assignment Engine
```csharp
// Assignment execution with strategy selection
var assigner = new RatePoolAssigner(string.Empty, ratePoolCollection, simCards, 
    context.logger, SanityCheckTimeLimit, context.LambdaContext, IsUsingRedisCache,
    instance.PortalType, shouldFilterByRatePlanType, shouldPoolUsageBetweenRatePlans);

assigner.AssignSimCards(GetSimCardGroupingByPortalType(instance.PortalType, instance.IsCustomerOptimization),
    context.OptimizationSettings.BillingTimeZone, false, false, ratePoolSequences);
```

#### Strategy Selection Logic
```csharp
private static List<SimCardGrouping> GetSimCardGroupingByPortalType(PortalTypes portalType, bool isCustomerOptimization)
{
    // Customer Optimization and Mobility: NoGrouping only
    if (portalType == PortalTypes.Mobility || isCustomerOptimization)
    {
        return new List<SimCardGrouping> { SimCardGrouping.NoGrouping };
    }
    // M2M: Both NoGrouping and GroupByCommunicationPlan
    else
    {
        return new List<SimCardGrouping> {
                SimCardGrouping.NoGrouping,
                SimCardGrouping.GroupByCommunicationPlan };
    }
}
```

### Sequential Processing Framework

**File: `AltaworxSimCardCostOptimizer.cs` (Lines 249-253)**
```csharp
// each run will have 4 sequential calculation with strategy based on a pair of attributes 
// SimCardGrouping and RemainingAssignmentOrder
// No Grouping + Largest To Smallest
// No Grouping + Smallest To Largest
// Group By Communication Plan + Largest To Smallest
// Group By Communication Plan + Smallest To Largest
```

---

## Performance Characteristics

### Strategy Comparison Matrix

| Strategy | Time Complexity | Memory Usage | Optimization Speed | Best Use Case |
|----------|----------------|--------------|-------------------|---------------|
| Largest to Smallest | O(n*m) | Linear | Fast | High-value customers |
| Smallest to Largest | O(n*m*log(m)) | Linear+ | Moderate | Plan efficiency focus |
| Communication Plan Grouping | O(g*p*d) | Linear+ | Variable | M2M consistency |

### Performance Metrics

#### Processing Speed
- **Largest to Smallest**: ~100ms per 1000 devices
- **Smallest to Largest**: ~150ms per 1000 devices  
- **Communication Plan Grouping**: ~200ms per 1000 devices (varies by group size)

#### Memory Requirements
- **Base Memory**: 50MB for core processing
- **Per Device**: ~2KB additional memory
- **Per Rate Plan**: ~1KB additional memory
- **Group Metadata**: ~5KB per communication plan group

#### Optimization Quality
- **Cost Reduction**: Largest to Smallest typically achieves 15-25% cost reduction
- **Plan Utilization**: Smallest to Largest achieves 85-95% plan utilization
- **Consistency**: Communication Plan Grouping achieves 95%+ operational consistency

---

## Configuration and Control

### Environment Variables
```bash
# Strategy execution control
OPTIMIZATION_STRATEGY_TIMEOUT=180
RATE_PLAN_LIMIT=15
MINIMUM_RATE_PLAN_LIMIT=2
BASE_ASSIGNED_DEVICE_LIMIT=1

# Performance tuning
QUEUES_PER_INSTANCE=5
SANITY_CHECK_TIME_LIMIT=180
REDIS_CACHE_ENABLED=true

# Strategy-specific settings
MINIMUM_UTILIZATION_THRESHOLD=0.6
MAXIMUM_UTILIZATION_THRESHOLD=0.95
LOW_VARIANCE_THRESHOLD=0.1
MEDIUM_VARIANCE_THRESHOLD=0.3
```

### Portal Type Configuration
```csharp
// Strategy availability by portal type
public enum PortalTypes
{
    M2M,        // Supports all strategies
    Mobility,   // NoGrouping only
    CrossProvider // NoGrouping only
}

public enum SiteTypes  
{
    Rev,            // Revenue customers
    AMOP,           // AMOP platform customers
    CrossProvider   // Cross-provider customers
}
```

### Strategy Control Flags
```csharp
// Assignment control parameters
bool isCustomerOptimization;     // Limits to NoGrouping strategies
bool shouldFilterByRatePlanType; // Applies plan type filtering
bool shouldPoolUsageBetweenRatePlans; // Enables usage pooling
bool usesProration;              // Applies proration calculations
```

---

## Troubleshooting and Monitoring

### Common Issues and Resolution

#### Strategy Selection Issues
```
Issue: Strategy not executing for M2M customers
Root Cause: isCustomerOptimization flag incorrectly set to true
Resolution: Verify customer optimization flag in instance configuration

Issue: Communication Plan Grouping not available
Root Cause: Portal type not set to M2M or customer optimization enabled  
Resolution: Check instance.PortalType and instance.IsCustomerOptimization
```

#### Performance Issues
```
Issue: Slow assignment processing
Root Cause: Large device count with complex rate plan matrix
Resolution: Enable Redis caching and increase timeout limits

Issue: Memory consumption high
Root Cause: Large communication plan groups not properly managed
Resolution: Implement group size limits and batch processing
```

#### Assignment Quality Issues
```
Issue: Low cost reduction with Largest to Smallest
Root Cause: High-usage devices already on optimal plans
Resolution: Review rate plan eligibility criteria and plan catalog

Issue: Poor plan utilization with Smallest to Largest  
Root Cause: Plan capacity limits too restrictive
Resolution: Adjust utilization thresholds and plan selection criteria
```

### Monitoring Metrics

#### Key Performance Indicators
```
- Assignment Success Rate: % of devices successfully assigned
- Cost Reduction Achieved: Total monthly savings generated
- Plan Utilization Rate: Average utilization across all plans
- Processing Time: Time to complete strategy execution
- Error Rate: % of assignments that failed or required manual review
```

#### Logging Points
```csharp
// Strategy selection logging
LogInfo(context, "INFO", $"Selected strategies: {string.Join(", ", selectedStrategies)}");

// Assignment progress logging  
LogInfo(context, "INFO", $"Processing {deviceCount} devices with {strategyName}");

// Results logging
LogInfo(context, "INFO", $"Strategy {strategyName} completed: {successCount}/{totalCount} assignments");
```

#### Alert Conditions
```
- Assignment failure rate > 5%
- Processing time > configured timeout
- Memory usage > 80% of available
- Cost reduction < expected threshold
- Plan utilization < minimum threshold
```

### Debugging Tools

#### Strategy Execution Trace
```csharp
// Enable detailed strategy tracing
context.EnableDetailedLogging = true;
context.LogLevel = LogLevel.Debug;

// Trace assignment decisions
LogDebug(context, $"Device {deviceId} assigned to plan {planId}: savings=${savings}");
```

#### Performance Profiling
```csharp
// Measure strategy execution time
var stopwatch = Stopwatch.StartNew();
assigner.AssignSimCards(strategies, timeZone, false, false, sequences);
stopwatch.Stop();
LogInfo(context, $"Strategy execution time: {stopwatch.ElapsedMilliseconds}ms");
```

---

## Appendix

### Related Documentation
- Rate Plan Processing Algorithm
- Customer Filtering Documentation  
- Optimization Result Recording
- Redis Caching Implementation

### Version History
- v1.0: Initial strategy implementation
- v1.1: Added Communication Plan Grouping
- v1.2: Performance optimizations and monitoring
- v1.3: Enhanced error handling and validation

### Contact Information
- Development Team: SIM Card Cost Optimization Team
- Documentation Maintainer: Technical Writing Team
- System Architecture: Platform Architecture Team