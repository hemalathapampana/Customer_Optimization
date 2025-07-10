# Cross-Provider Rate Plan Permutation Logic

## Overview
This document provides detailed analysis of the cross-provider rate plan permutation logic, explaining how sequences of compatible rate plans are generated across providers, permutation limits are enforced, sequences are ordered by cost optimization potential, invalid combinations are filtered, and provider-specific constraints are considered.

## 1. Generates Sequences of Compatible Rate Plans Across Providers

### What
The system generates sequences of compatible rate plans across multiple service providers using `RatePoolAssigner.GenerateRatePoolSequences()`, creating comprehensive permutation combinations that respect cross-provider compatibility requirements.

### Why
- **Comprehensive Optimization**: Explores all possible combinations for maximum cost savings
- **Cross-Provider Coverage**: Ensures rate plans work across different carrier networks
- **Algorithm Completeness**: Guarantees no optimal combination is missed
- **Provider Interoperability**: Maintains compatibility across carrier boundaries
- **Maximum Savings Potential**: Identifies the best possible cost optimization scenarios

### How
The system calculates rate pool usage, creates rate pools, and generates sequences using the RatePoolAssigner:

```589:594:AltaworxSimCardCostQueueCustomerOptimization.cs
// create new comm plan group
var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);
```

### Algorithm
```
ALGORITHM: GenerateCompatibleRatePlanSequences()
INPUT: List<RatePlan> groupRatePlans, BillingPeriod billingPeriod, OptimizationChargeType chargeType
OUTPUT: List<RatePoolSequence> compatibleSequences

1. CREATE communication plan group:
   a. commPlanGroupId = CreateCommPlanGroup(context, instanceId)
2. CALCULATE optimal usage patterns:
   a. calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null)
3. CREATE rate pools with billing constraints:
   a. ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType)
4. GENERATE rate pool collection:
   a. ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools)
5. GENERATE cross-provider compatible sequences:
   a. LOG sequence generation start with rate pool count
   b. ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools)
   c. LOG sequence generation completion
6. RETURN compatibleSequences
```

### Code Locations

**Rate Pool Collection Creation:**
```589:594:AltaworxSimCardCostQueueCustomerOptimization.cs
// create new comm plan group
var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);
```

**Sequence Generation:**
```631:633:AltaworxSimCardCostQueueCustomerOptimization.cs
LogInfo(context, LogTypeConstant.Sub, detail: $"Start GenerateRatePoolSequences for {ratePoolCollection.RatePools.Count} Rate Plans");
var ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools);
LogInfo(context, LogTypeConstant.Sub, "End GenerateRatePoolSequences");
```

## 2. Limits Permutations to Prevent Cross-Provider Combinatorial Explosion

### What
The system implements strict limits on permutation generation to prevent combinatorial explosion, enforcing maximum rate plan limits (15 plans) and minimum requirements (2 plans) to maintain computational feasibility across providers.

### Why
- **Performance Management**: Prevents system overload from excessive permutations
- **Resource Optimization**: Maintains acceptable processing times and memory usage
- **Scalability**: Ensures system can handle multiple concurrent optimizations
- **Cost-Effectiveness**: Balances comprehensive optimization with computational cost
- **System Stability**: Prevents memory exhaustion and timeout failures

### How
The system validates rate plan counts against predefined constants before permutation generation:

```605:615:AltaworxSimCardCostQueueCustomerOptimization.cs
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
```

### Algorithm
```
ALGORITHM: LimitPermutationsForCombinatorialControl()
INPUT: List<RatePlan> calculatedPlans, String planNameGroupKey, String ratePlanGroupKey
OUTPUT: Boolean permutationAllowed

1. VALIDATE maximum permutation limit:
   a. IF calculatedPlans.Count > OptimizationConstant.RatePlanLimit (15):
      i. LOG exception: "Rate plan count exceeds limit of 15"
      ii. CONTINUE to next group (skip permutation)
      iii. RETURN false
2. VALIDATE minimum permutation requirement:
   a. IF calculatedPlans.Count <= OptimizationConstant.RatePlanMinimumLimit (2):
      i. LOG info: AUTO_CHANGE_MINIMUM_RATE_PLAN_LIMIT_REACHED
      ii. CONTINUE to next group (skip permutation)
      iii. RETURN false
3. VALIDATE device assignment threshold:
   a. IF baseAssignedSimCardsCount <= OptimizationConstant.BaseAssignedDeviceLimit:
      i. LOG info: "Insufficient devices for permutation optimization"
      ii. RETURN false
4. RETURN true (permutation allowed)
```

### Code Locations

**Maximum Rate Plan Limit Enforcement:**
```605:608:AltaworxSimCardCostQueueCustomerOptimization.cs
// permute rate plans
if (calculatedPlans.Count > OptimizationConstant.RatePlanLimit)
{
    LogInfo(context, LogTypeConstant.Exception, $"The rate plan count exceeds the limit of 15 for this Rate Plan Code {ratePlanGroup.Key}. Please cut down the options to 15 or less for this Rate Plan Code.");
    continue;
}
```

**Minimum Rate Plan Requirement Validation:**
```610:615:AltaworxSimCardCostQueueCustomerOptimization.cs
if (calculatedPlans.Count <= OptimizationConstant.RatePlanMinimumLimit)
{

    LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.AUTO_CHANGE_MINIMUM_RATE_PLAN_LIMIT_REACHED, calculatedPlans.Count, planNameGroup.Key, ratePlanGroup.Key));
    continue;
}
```

**Device Assignment Threshold Check:**
```602:604:AltaworxSimCardCostQueueCustomerOptimization.cs
if (baseAssignedSimCardsCount > OptimizationConstant.BaseAssignedDeviceLimit)
{
    // permute rate plans
```

## 3. Orders Sequences by Cross-Provider Cost Optimization Potential

### What
The system orders optimization sequences by cost optimization potential, using sequence ordering mechanisms and total cost-based selection to prioritize the most promising permutations for cross-provider optimization.

### Why
- **Efficiency Optimization**: Processes most promising permutations first
- **Resource Allocation**: Focuses computational resources on high-value scenarios
- **Early Termination**: Enables stopping when optimal solution is found
- **Performance Enhancement**: Reduces overall processing time
- **Result Quality**: Improves likelihood of finding optimal cost solutions

### How
The system creates ordered sequences with sequence numbering and uses cost-based result selection:

```635:640:AltaworxSimCardCostQueueCustomerOptimization.cs
var dtQueueRatePlan = new DataTable();
dtQueueRatePlan.Columns.Add("QueueId", typeof(long));
dtQueueRatePlan.Columns.Add("CommGroup_RatePlanId", typeof(long));
dtQueueRatePlan.Columns.Add("SequenceOrder", typeof(int));
dtQueueRatePlan.Columns.Add("CreatedBy");
dtQueueRatePlan.Columns.Add("CreatedDate", typeof(DateTime));
```

### Algorithm
```
ALGORITHM: OrderSequencesByCostOptimizationPotential()
INPUT: List<RatePoolSequence> ratePoolSequences, DataTable commGroupRatePlanTable
OUTPUT: DataTable orderedQueueRatePlans

1. INITIALIZE ordered queue data structure:
   a. CREATE dtQueueRatePlan with columns: QueueId, CommGroup_RatePlanId, SequenceOrder, CreatedBy, CreatedDate
2. FOR each ratePoolSequence in ratePoolSequences (ordered by optimization potential):
   a. CREATE optimization queue: queueId = CreateQueue(...)
   b. ADD rate plans to queue with sequence order:
      i. dtQueueRatePlanTemp = AddRatePlansToQueue(queueId, ratePoolSequence, commGroupRatePlanTable)
   c. IF queue has rate plan data:
      i. FOR each DataRow in dtQueueRatePlanTemp:
         - ADD row to dtQueueRatePlan (maintaining sequence order)
3. PERSIST ordered queue rate plans:
   a. CreateQueueRatePlans(context, dtQueueRatePlan)
4. EXECUTE optimization with cost-based ordering:
   a. SELECT best result: ORDER BY TotalCost ASC
5. RETURN ordered optimization results
```

### Code Locations

**Sequence Order Data Structure:**
```635:640:AltaworxSimCardCostQueueCustomerOptimization.cs
var dtQueueRatePlan = new DataTable();
dtQueueRatePlan.Columns.Add("QueueId", typeof(long));
dtQueueRatePlan.Columns.Add("CommGroup_RatePlanId", typeof(long));
dtQueueRatePlan.Columns.Add("SequenceOrder", typeof(int));
dtQueueRatePlan.Columns.Add("CreatedBy");
dtQueueRatePlan.Columns.Add("CreatedDate", typeof(DateTime));
```

**Ordered Queue Creation:**
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

**Cost-Based Result Selection:**
```text:AltaworxSimCardCostOptimizerCleanup.cs
SELECT TOP 1 Id FROM OptimizationQueue WHERE CommPlanGroupId = @commGroupId AND TotalCost IS NOT NULL AND RunEndTime IS NOT NULL ORDER BY TotalCost ASC
```

## 4. Filters Out Invalid Cross-Provider Rate Plan Combinations

### What
The system filters out invalid cross-provider rate plan combinations by validating provider compatibility, checking zero-value rate plans, and ensuring cross-provider service provider ID alignment.

### Why
- **Data Integrity**: Prevents optimization with invalid or incompatible rate plans
- **System Reliability**: Avoids failures from malformed rate plan combinations
- **Cost Accuracy**: Ensures calculations use valid pricing information
- **Provider Compliance**: Maintains adherence to carrier-specific requirements
- **Quality Assurance**: Guarantees only viable combinations are processed

### How
The system performs multiple validation layers to filter invalid combinations:

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
ALGORITHM: FilterInvalidCrossProviderCombinations()
INPUT: List<RatePlan> ratePlans, String serviceProviderIds, List<RatePlan> groupRatePlans
OUTPUT: List<RatePlan> validRatePlans, Boolean validationSuccess

1. FILTER auto change enabled rate plans:
   a. autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan)
2. VALIDATE cross-provider compatibility:
   a. IF autoChangeRatePlans.Any() AND serviceProviderIds specified:
      i. PARSE provider ID list: serviceProviderIdList = serviceProviderIds.Split()
      ii. FILTER compatible plans: autoChangeRatePlans.Where(x => x.ServiceProviderIds.ContainsAllItems(serviceProviderIdList))
      iii. IF no compatible plans found:
          - LOG error: NO_VALID_CROSS_PROVIDER_CUSTOMER_RATE_PLAN_FOUND
          - RETURN validation failure
3. VALIDATE zero-value rate plan exclusion:
   a. zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M OR x.OverageRate == 0.0M)
   b. IF zeroValueRatePlans.Count > 0:
      i. LOG exception with plan display names
      ii. RETURN validation failure
4. VALIDATE rate plan name compatibility:
   a. FILTER by PlanName compatibility across providers
   b. ENSURE GroupBy(x => x.PlanName) consistency
5. RETURN validRatePlans, validation success
```

### Code Locations

**Cross-Provider Compatibility Validation:**
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

**Zero-Value Rate Plan Filtering:**
```573:577:AltaworxSimCardCostQueueCustomerOptimization.cs
var zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
if (zeroValueRatePlans.Count > 0)
{
    LogInfo(context, LogTypeConstant.Exception, $"The following rate plans in '{planNameGroup.Key}' has Data per Overage Charge or Overage Rate of 0. Please update to a non-zero value.{Environment.NewLine} {string.Join(',', zeroValueRatePlans.Select(ratePlan => ratePlan.PlanDisplayName))}");
    return true;
}
```

## 5. Considers Provider-Specific Constraints and Capabilities

### What
The system considers provider-specific constraints and capabilities including SIM pooling permissions (`AllowsSimPooling`), plan name variations, service provider associations, and billing period alignment across different carriers.

### Why
- **Provider Compliance**: Respects individual carrier business rules and limitations
- **Feature Compatibility**: Ensures rate plans support required capabilities
- **Billing Alignment**: Maintains consistent billing periods across providers
- **Capability Matching**: Matches rate plan features with provider capabilities
- **Regulatory Adherence**: Complies with provider-specific regulatory requirements

### How
The system groups rate plans by provider-specific capabilities and validates constraints:

```567:569:AltaworxSimCardCostQueueCustomerOptimization.cs
foreach (var ratePlanGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling))
{
    LogInfo(context, LogTypeConstant.Info, $"Allows SIM Pooling: {ratePlanGroup.Key}");
```

### Algorithm
```
ALGORITHM: ConsiderProviderSpecificConstraints()
INPUT: IGrouping<String, RatePlan> planNameGroup, String serviceProviderIds
OUTPUT: List<RatePlan> constraintCompliantPlans

1. GROUP by provider-specific capabilities:
   a. FOR each ratePlanGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling):
      i. LOG capability: "Allows SIM Pooling: {ratePlanGroup.Key}"
2. VALIDATE provider-specific rate plan constraints:
   a. groupRatePlans = ratePlanGroup.ToList()
   b. VALIDATE zero-value constraints per provider
   c. CHECK DataPerOverageCharge and OverageRate requirements
3. FILTER by service provider associations:
   a. VALIDATE ServiceProviderIds alignment
   b. ENSURE ContainsAllItems(serviceProviderIdList) compatibility
4. VALIDATE billing period alignment:
   a. CHECK billing period compatibility across providers
   b. ENSURE usesProration consistency
5. VALIDATE device assignment constraints:
   a. CHECK optimizationSimCards.Count > 0
   b. VALIDATE baseAssignedSimCardsCount > BaseAssignedDeviceLimit
6. APPLY provider-specific rate plan naming:
   a. FILTER by PlanName and PlanDisplayName variations
   b. HANDLE provider-specific naming conventions
7. RETURN constraint-compliant rate plans
```

### Code Locations

**Provider-Specific Capability Grouping:**
```567:569:AltaworxSimCardCostQueueCustomerOptimization.cs
foreach (var ratePlanGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling))
{
    LogInfo(context, LogTypeConstant.Info, $"Allows SIM Pooling: {ratePlanGroup.Key}");
```

**Rate Plan Code Filtering by Provider:**
```549:552:AltaworxSimCardCostQueueCustomerOptimization.cs
var ratePlansByCodes = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan && ratePlanCodes.Contains(ratePlan.PlanName)).GroupBy(x => x.PlanName);
foreach (var ratePlansByCode in ratePlansByCodes)
{
    isError = await ProcessPlanNameGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, billingPeriod, instanceId, chargeType, ratePlansByCode, simCardsByRatePoolId.ToList());
```

**Provider-Specific Validation Logic:**
```580:587:AltaworxSimCardCostQueueCustomerOptimization.cs
// filter rate plans that are used for auto change rate plan
if (optimizationSimCards.Count == 0)
{
    // No more devices to process the next steps for this rate plan group
    // if there are devices but no rate plans, the devices could be unassigned devices so it is expected
    LogInfo(context, LogTypeConstant.Info, $"No more device to optimize for rate plan in group with rate plan code '{planNameGroup.Key}', AllowsSimPooling: {ratePlanGroup.Key}.");
    continue;
}
```

## Permutation Logic Integration

The five components of cross-provider rate plan permutation logic work together to provide comprehensive optimization:

1. **Sequence Generation** creates comprehensive rate plan combinations
2. **Permutation Limiting** prevents computational explosion while maintaining effectiveness
3. **Cost-Based Ordering** prioritizes most promising optimization scenarios
4. **Invalid Combination Filtering** ensures data integrity and system reliability
5. **Provider Constraint Consideration** maintains compliance with carrier-specific requirements

This integrated approach ensures efficient, scalable, and comprehensive cross-provider rate plan permutation while respecting provider constraints and optimizing for maximum cost savings potential.