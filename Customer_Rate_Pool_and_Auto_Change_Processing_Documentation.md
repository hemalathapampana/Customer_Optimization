# Customer Rate Pool and Auto Change Processing Documentation

## 1. Customer Rate Pool Processing: Device Grouping by Rate Pool ID

Groups devices by customer rate pool ID for pooled optimization to enable efficient resource allocation and cost optimization across multiple devices sharing the same rate pool. Essential for ensuring devices with shared billing characteristics are optimized together, maintaining proper billing boundaries and maximizing cost savings potential.

Examines optimization SIM cards to group devices by their assigned customer rate pool identifier, processes each group separately for optimization algorithms, and handles both pooled and non-pooled device scenarios.

### Primary Processing: Lines 532, 818
**Device grouping by rate pool ID:** Lines 532, 818
**Rate pool validation:** Lines 537-544, 823-830  
**Pool-specific optimization:** Lines 541-544, 827-830

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
```csharp
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
}
```

---

## 2. Auto Change Processing: Dynamic Rate Plan Grouping

Groups devices by rate plan code for dynamic rate plan changes to enable automatic optimization across different rate plan options. Essential for implementing cost-saving rate plan switches while maintaining service quality and ensuring customers are always on the most cost-effective plans.

Filters rate plans enabled for auto change functionality, groups them by plan name to ensure compatibility, and processes each group through optimization algorithms for automatic rate plan assignment.

### Primary Processing: Lines 549, 835
**Auto change filtering:** Lines 520-529, 806-815
**Plan name grouping:** Lines 549, 835
**SIM pooling validation:** Lines 567, ProcessPlanNameGroup method

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
```csharp
// Group rate plans by rate plan code and run auto change optimization logic for this group of devices
var ratePlansByCodes = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan && ratePlanCodes.Contains(ratePlan.PlanName)).GroupBy(x => x.PlanName);
foreach (var ratePlansByCode in ratePlansByCodes)
{
    isError = await ProcessPlanNameGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, billingPeriod, instanceId, chargeType, ratePlansByCode, simCardsByRatePoolId.ToList());
}

// Within ProcessPlanNameGroup - further grouping by AllowsSimPooling
foreach (var ratePlanGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling))
{
    LogInfo(context, LogTypeConstant.Info, $"Allows SIM Pooling: {ratePlanGroup.Key}");
    // get rate plans for group
    var groupRatePlans = ratePlanGroup.ToList();
}
```

---

## 3. Permutation Generation: Rate Plan Combination Creation

Creates all valid rate plan combinations for testing to ensure comprehensive optimization coverage across different rate plan scenarios. Essential for maximizing cost savings by evaluating all possible rate plan assignments and selecting the most cost-effective combinations.

Generates sequences of compatible rate plans using RatePoolAssigner, validates rate plan limits to prevent combinatorial explosion, and creates optimization queues for each permutation sequence.

### Primary Processing: Lines 631-633
**Sequence generation start:** Line 631
**RatePoolAssigner execution:** Line 632  
**Sequence generation completion:** Line 633
**Queue creation per sequence:** Line 645
**Rate plan limit validation:** Lines 605-607, 610-613

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
```csharp
LogInfo(context, LogTypeConstant.Sub, detail: $"Start GenerateRatePoolSequences for {ratePoolCollection.RatePools.Count} Rate Plans");
var ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools);
LogInfo(context, LogTypeConstant.Sub, "End GenerateRatePoolSequences");

// Limit validation
if (calculatedPlans.Count > OptimizationConstant.RatePlanLimit)
{
    LogInfo(context, LogTypeConstant.Exception, $"The rate plan count exceeds the limit of 15 for this Rate Plan Code {ratePlanGroup.Key}. Please cut down the options to 15 or less for this Rate Plan Code.");
    continue;
}

foreach (var ratePoolSequence in ratePoolSequences)
{
    // add queue for rate plan permutation
    var queueId = CreateQueue(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId, usesProration);
}
```

---

## 4. Queue Creation: Parallel Processing Queue Generation

Generates optimization queues for parallel processing to enable concurrent execution of optimization algorithms across multiple rate plan combinations. Essential for reducing processing time and improving system performance by distributing computational workload across multiple parallel execution paths.

Creates individual queues for each rate plan permutation, assigns queues to communication plan groups for batch processing, and configures queue parameters for optimal parallel execution.

### Primary Processing: Line 645
**Queue creation:** Line 645
**Queue rate plan assignment:** Lines 647-657
**Queue initialization:** Lines 672, 862 (for unused devices)
**Parallel execution setup:** Line 619 (EnqueueOptimizationRunsAsync)

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
```csharp
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

CreateQueueRatePlans(context, dtQueueRatePlan);

// enqueue rate plan permutations
await EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true);
```

---

## Rate Plan Permutation Logic

### Overview
Generates sequences of compatible rate plans for testing while implementing safeguards to prevent combinatorial explosion and ensure optimal resource utilization.

### Core Features

**Sequence Generation:** Lines 631-633  
**Limit Enforcement:** Lines 605-607 (max 15 rate plans per group)  
**Minimum Validation:** Lines 610-613 (minimum 2 rate plans required)  
**Cost Optimization Ordering:** Built into RatePoolAssigner.GenerateRatePoolSequences()  
**Invalid Combination Filtering:** Lines 572-577 (zero value rate plan validation)

### Validation Rules

- **Maximum Rate Plans:** 15 per rate plan code group to prevent excessive permutations
- **Minimum Rate Plans:** 2 required for meaningful optimization comparisons  
- **Zero Value Filtering:** Rate plans with DataPerOverageCharge or OverageRate of 0 are excluded
- **Device Count Validation:** BaseAssignedDeviceLimit check ensures sufficient devices for optimization
- **SIM Pooling Compatibility:** Groups validated by AllowsSimPooling setting

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
```csharp
// Rate plan limit validation
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

// Zero value rate plan filtering  
var zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
if (zeroValueRatePlans.Count > 0)
{
    LogInfo(context, LogTypeConstant.Exception, $"The following rate plans in '{planNameGroup.Key}' has Data per Overage Charge or Overage Rate of 0. Please update to a non-zero value.{Environment.NewLine} {string.Join(',', zeroValueRatePlans.Select(ratePlan => ratePlan.PlanDisplayName))}");
    return true;
}
```

---

## Additional Processing Details

### Cross-Provider Customer Optimization Validations
- **Customer Rate Pool Processing:** Lines 818-820, 827-830
- **Auto Change Processing:** Lines 807-815, 835-837  
- **Service Provider Validation:** Lines 807-815
- **Rate Pool Grouping:** Lines 818-820

### Error Handling Patterns
- **All processing failures result in immediate error return with proper logging**
- **Comprehensive validation prevents invalid rate plan combinations**  
- **Proper exception handling with detailed error messages for debugging**
- **Integration with AMOP 2.0 API for error notification and tracking**

### Security Considerations
- **Rate pool isolation maintained through proper grouping logic**
- **Service provider boundaries respected in cross-provider scenarios**
- **Customer data access controlled through validated rate pool assignments**
- **Audit trail maintained through comprehensive logging at each processing step**

---

## Summary

These processing features form the core optimization engine for the Altaworx SIM Card Cost Optimization system. They ensure:

1. **Efficient Resource Grouping:** Customer rate pools enable logical device grouping for optimal processing
2. **Dynamic Plan Management:** Auto change processing provides automated cost optimization
3. **Comprehensive Testing:** Permutation generation ensures all viable combinations are evaluated  
4. **Scalable Performance:** Parallel queue processing enables efficient large-scale optimization

The processing logic is implemented primarily in the ProcessDevicesByCustomerRatePlans and ProcessPlanNameGroup methods within AltaworxSimCardCostQueueCustomerOptimization.cs.