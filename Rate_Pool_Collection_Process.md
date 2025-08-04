# Rate Pool Collection Process in QueueCustomerOptimization

## Overview

The Rate Pool Collection process is a critical component within the **QueueCustomerOptimization** workflow that creates optimized groupings of compatible rate plans for automated cost optimization. This process ensures that devices are assigned to the most cost-effective rate plans while respecting compatibility constraints and pooling settings.

## Context: Where This Fits in the Overall Process

### 1. **Pre-Rate Pool Collection Steps**
Before rate pool collection begins, the system has already:

```
Customer Request → Get Billing Periods → Get Customer Rate Plans → Group Devices by Rate Pool ID
```

- **Device Grouping**: Devices are grouped by `CustomerRatePoolId` 
- **Rate Plan Filtering**: Rate plans are filtered to include only those with `AutoChangeRatePlan = true`
- **Initial Validation**: Zero-value rate plans are identified and flagged

### 2. **Rate Pool Collection Phase** (Current Focus)
This is where compatible rate plans are grouped and sequences are generated for optimization.

### 3. **Post-Rate Pool Collection Steps**
After rate pool collection, the system:
- Creates optimization queues for each permutation sequence
- Runs the optimization algorithm
- Records results and assigns optimal rate plans to devices

## Detailed Rate Pool Collection Algorithm

### Phase 1: Rate Plan Compatibility Grouping

```csharp
// Step 1: Group by Rate Plan Code (PlanName)
var ratePlansByCodes = ratePlans
    .Where(ratePlan => ratePlan.AutoChangeRatePlan && ratePlanCodes.Contains(ratePlan.PlanName))
    .GroupBy(x => x.PlanName);

// Step 2: Within each PlanName group, further group by AllowsSimPooling
foreach (var planNameGroup in ratePlansByCodes)
{
    foreach (var ratePlanGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling))
    {
        // Process compatible rate plans together
    }
}
```

**Why This Grouping Matters:**
- **Rate Plan Code Compatibility**: Only rate plans with the same `PlanName` can be swapped for the same devices
- **Pooling Constraint**: Rate plans with different `AllowsSimPooling` settings cannot be mixed in the same optimization run

### Phase 2: Validation and Filtering

```csharp
// 1. Zero Value Validation
var zeroValueRatePlans = groupRatePlans.FindAll(x => 
    x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
if (zeroValueRatePlans.Count > 0)
{
    // Log error and skip this group - cannot optimize with zero cost plans
    return true;
}

// 2. Rate Plan Count Limits
if (calculatedPlans.Count > OptimizationConstant.RatePlanLimit) // 15 plans max
{
    // Too many plans - would create too many permutations
    continue;
}
if (calculatedPlans.Count <= OptimizationConstant.RatePlanMinimumLimit) // 2 plans min
{
    // Not enough plans to optimize - skip
    continue;
}

// 3. Device Count Check
if (baseAssignedSimCardsCount <= OptimizationConstant.BaseAssignedDeviceLimit) // 1 device
{
    // Single device optimization already handled by base assignment
    continue;
}
```

### Phase 3: Rate Pool Creation

```csharp
// Step 1: Calculate Maximum Average Usage
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);

// Step 2: Create Rate Pools with Billing Context
var ratePools = RatePoolFactory.CreateRatePools(
    calculatedPlans, 
    billingPeriod, 
    usesProration, 
    chargeType
);

// Step 3: Create Rate Pool Collection
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);
```

**What Each Step Accomplishes:**
- **CalculateMaxAvgUsage**: Determines the maximum expected data usage for each rate plan to enable accurate cost projections
- **CreateRatePools**: Wraps rate plans with billing context (proration settings, charge type, billing period)
- **CreateRatePoolCollection**: Creates a structured collection that can be processed by the optimization algorithm

### Phase 4: Permutation Sequence Generation

```csharp
// Generate all possible assignment sequences
var ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools);
```

**Permutation Logic:**
- Creates different ordering sequences of the rate plans
- Each sequence represents a different priority order for device assignment
- Example: If you have Rate Plans A, B, C, sequences might be:
  - Sequence 1: [A, B, C] - Try A first, then B, then C
  - Sequence 2: [B, A, C] - Try B first, then A, then C
  - Sequence 3: [C, B, A] - Try C first, then B, then A

### Phase 5: Optimization Queue Creation

```csharp
foreach (var ratePoolSequence in ratePoolSequences)
{
    // Create a separate optimization queue for each sequence
    var queueId = CreateQueue(context, instanceId, commPlanGroupId, 
                             billingPeriod.ServiceProviderId, usesProration);
    
    // Add rate plans to queue in sequence order
    var dtQueueRatePlanTemp = AddRatePlansToQueue(queueId, ratePoolSequence, commGroupRatePlanTable);
}
```

## Real-World Example

### Scenario
- **Customer**: Enterprise with 100 devices
- **Available Rate Plans**: 
  - Plan A: $20/month, 5GB, AllowsSimPooling=true
  - Plan B: $30/month, 10GB, AllowsSimPooling=true  
  - Plan C: $50/month, 20GB, AllowsSimPooling=true

### Process Flow

1. **Grouping**: All three plans have same PlanName and AllowsSimPooling=true → grouped together

2. **Validation**: 
   - ✅ No zero-value plans
   - ✅ 3 plans (within 2-15 limit)
   - ✅ 100 devices (>1 device limit)

3. **Rate Pool Creation**: 
   - Calculate max usage for each plan
   - Create rate pools with billing context
   - Create collection for optimization

4. **Sequence Generation**: Creates 6 permutations (3! = 6):
   - Queue 1: [A, B, C] - Try cheapest first
   - Queue 2: [A, C, B] - Try cheapest, then highest
   - Queue 3: [B, A, C] - Try medium first
   - Queue 4: [B, C, A] - Try medium, then highest
   - Queue 5: [C, A, B] - Try highest first
   - Queue 6: [C, B, A] - Try highest, then medium

5. **Optimization**: Each queue runs independently, algorithm picks the sequence that results in lowest total cost

## Key Benefits

### 1. **Comprehensive Optimization**
- Tests all possible assignment orders to find global optimum
- Prevents getting stuck in local optimums

### 2. **Constraint Respect**
- Ensures only compatible rate plans are grouped
- Respects pooling settings and rate plan codes

### 3. **Scalability Control**
- Limits on rate plan count prevent exponential explosion
- Device count thresholds ensure meaningful optimization

### 4. **Cost Effectiveness**
- Base assignment handles simple cases efficiently
- Complex optimization only runs when beneficial

## Integration Points

### Before Rate Pool Collection:
- **Device Discovery**: `GetOptimizationSimCards()`
- **Rate Plan Retrieval**: Customer rate plans with AutoChangeRatePlan enabled
- **Validation**: Zero-value rate plan detection

### After Rate Pool Collection:
- **Queue Processing**: `EnqueueOptimizationRunsAsync()`
- **Algorithm Execution**: Each sequence processed by optimization engine
- **Result Recording**: Optimal assignments recorded in database

### Error Handling:
- Zero-value rate plans → Stop entire optimization
- Too many rate plans → Skip this group, continue with others
- Too few rate plans/devices → Skip optimization, use base assignment

## Performance Considerations

### Permutation Explosion Prevention:
- **15 Plan Limit**: Prevents factorial explosion (15! = 1.3 trillion permutations)
- **Smart Grouping**: Only compatible plans grouped together
- **Early Exits**: Skip optimization for cases where base assignment is sufficient

### Memory Management:
- **Streaming Processing**: Processes one rate pool group at a time
- **Database Chunking**: Uses DataTable for bulk operations
- **Resource Cleanup**: Temporary collections disposed after processing

This Rate Pool Collection process is the foundation that enables the optimization algorithm to systematically find the most cost-effective rate plan assignments while maintaining system performance and respecting business constraints.