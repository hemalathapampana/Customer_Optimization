# Key Operations: Customer Rate Plan Processing Documentation

## 1. Customer Compatibility: Billing Cycle Integration

### What
Ensures rate plans work with customer billing cycles by validating billing period compatibility, rate plan eligibility, and customer-specific billing characteristics for seamless optimization processing.

### Why
Essential for preventing billing conflicts and ensuring rate plan changes align with customer billing cycles. Maintains billing accuracy and prevents optimization errors that could disrupt customer billing processes.

### How
Validates billing period existence and compatibility, checks rate plan eligibility flags, matches customer billing characteristics with rate plan requirements, and ensures proper billing period sequencing for optimization calculations.

### Algorithm
**ALGORITHM: ValidateCustomerBillingCompatibility**  
**INPUT:** Customer Rate Plans, Billing Period, Customer Type  
**OUTPUT:** Compatible Rate Plans Collection

**Step 1: Validate Billing Period Compatibility**
- Retrieve billing period using GetBillingPeriod method
- Verify billing period exists and is valid for optimization
- Check billing period start and end dates for proper sequencing
- Log billing period information for tracking and validation

**Step 2: Check Rate Plan Eligibility**
- Examine each rate plan for IsBillInAdvanceEligible flag
- Validate rate plan compatibility with customer billing cycle
- Filter rate plans based on customer type (Rev vs AMOP)
- Ensure rate plans support customer billing characteristics

**Step 3: Validate Next Billing Period Sequence**
- Call GetNextBillingPeriod with service provider and end date
- Ensure proper billing period progression for advance billing
- Validate next billing period availability for optimization
- Configure bill in advance billing period ID if applicable

**Step 4: Match Customer Rate Plan Codes**
- Extract CustomerRatePlanCode from optimization SIM cards
- Match rate plan codes with available rate plans by PlanName
- Filter devices with valid rate plan code assignments
- Ensure rate plan compatibility with device assignments

**Step 5: Validate Service Provider Compatibility**
- Check service provider ID from billing period
- Ensure rate plans belong to correct service provider
- Validate cross-provider scenarios if applicable
- Maintain service provider isolation and boundaries

**Step 6: Return Compatible Rate Plans**
- Return validated and compatible rate plans collection
- Include billing period compatibility confirmation
- Log successful compatibility validation for monitoring
- Prepare rate plans for optimization processing

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
- **Primary Processing:** Lines 284-310, 402-424
```csharp
// get customer rate plans
var ratePlans = GetCustomerRatePlans(context, customerId, (int)billingPeriodId, serviceProviderId, tenantId);

var useBillInAdvance = ratePlans.Count(x => x.IsBillInAdvanceEligible) > 0;
//Disable bill in advance logic until new logic is defined (PORT-166)
useBillInAdvance = false;

LogInfo(context, "INFO", $"Use Bill In Advance: {useBillInAdvance}");

// start instance
if (billingPeriodId.HasValue)
{
    var billingPeriod = GetBillingPeriod(context, billingPeriodId.Value);
    BillingPeriod nextBillingPeriod = null;
    if (billingPeriod != null)
    {
        nextBillingPeriod = GetNextBillingPeriod(context, billingPeriod.ServiceProviderId, billingPeriod.BillingPeriodEnd);
    }

    var billInAdvanceBillingPeriodId = nextBillingPeriod?.Id;

    LogInfo(context, "INFO", $"Bill In Advance Billing Period Id: {billInAdvanceBillingPeriodId}");

    if (useBillInAdvance && (billInAdvanceBillingPeriodId == null || billingPeriod == null))
    {
        LogInfo(context, "ERROR", $"A Billing Period past Billing Period Id = {billingPeriodId.Value} could not be found for this Customer. So, billing in advance is not possible at this time. Optimization not run.");
        return;
    }
}
```

---

## 2. Cost Ranking: Rate Plan Cost-Effectiveness Ordering

### What
Orders plans by customer cost-effectiveness through systematic cost calculation, usage analysis, and optimization potential ranking to prioritize the most beneficial rate plan options for customers.

### Why
Essential for maximizing customer cost savings by prioritizing rate plans with highest savings potential. Ensures optimization algorithms focus on most cost-effective options first, improving processing efficiency and customer outcomes.

### How
Calculates maximum average usage for rate plans, creates rate pools with cost analysis, generates rate pool collections with cost rankings, and orders sequences by optimization potential through RatePoolAssigner logic.

### Algorithm
**ALGORITHM: RankRatePlansByCostEffectiveness**  
**INPUT:** Rate Plans, Billing Period, Usage Data  
**OUTPUT:** Cost-Ranked Rate Plan Collections

**Step 1: Calculate Maximum Average Usage**
- Call RatePoolCalculator.CalculateMaxAvgUsage with rate plans
- Analyze historical usage patterns for cost projections
- Calculate potential cost savings for each rate plan option
- Generate calculated plans with usage and cost metrics

**Step 2: Create Cost-Analyzed Rate Pools**
- Use RatePoolFactory.CreateRatePools with calculated plans
- Include billing period and charge type in calculations
- Apply proration logic for accurate cost projections
- Generate rate pools with embedded cost analysis

**Step 3: Generate Rate Pool Collection**
- Call RatePoolCollectionFactory.CreateRatePoolCollection
- Organize rate pools by cost-effectiveness ranking
- Prepare collection for optimization sequence generation
- Ensure proper cost-based ordering for processing

**Step 4: Generate Cost-Optimized Sequences**
- Call RatePoolAssigner.GenerateRatePoolSequences
- Generate sequences ordered by cost optimization potential
- Prioritize combinations with highest savings potential
- Create permutations that maximize customer cost benefits

**Step 5: Apply Cost-Based Filtering**
- Filter out rate plans with zero cost effectiveness
- Remove plans with DataPerOverageCharge or OverageRate of 0
- Validate cost calculations meet minimum thresholds
- Ensure all included plans provide measurable savings

**Step 6: Return Cost-Ranked Collections**
- Return rate pool collections ordered by cost effectiveness
- Include cost analysis metadata for optimization processing
- Log cost ranking results for monitoring and validation
- Prepare optimized sequences for queue creation

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
- **Primary Processing:** Lines 590-594, 631-633
```csharp
// create new comm plan group
var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);

var baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId,
    revAccountNumber, integrationAuthenticationId, null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId);

// Generate cost-optimized sequences
LogInfo(context, LogTypeConstant.Sub, detail: $"Start GenerateRatePoolSequences for {ratePoolCollection.RatePools.Count} Rate Plans");
var ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools);
LogInfo(context, LogTypeConstant.Sub, "End GenerateRatePoolSequences");
```

---

## 3. Auto Change Logic: Customer-Specific Rate Plan Rules

### What
Applies customer-specific rate plan change rules by filtering AutoChangeRatePlan enabled plans, validating customer eligibility, and implementing automated rate plan optimization according to customer preferences and constraints.

### Why
Essential for enabling automated cost optimization while respecting customer-defined constraints and preferences. Ensures rate plan changes comply with customer agreements and maintain service quality standards.

### How
Filters rate plans with AutoChangeRatePlan flag enabled, groups compatible plans by name and pooling settings, validates customer-specific constraints, and processes groups through automated optimization algorithms.

### Algorithm
**ALGORITHM: ApplyCustomerAutoChangeRules**  
**INPUT:** Rate Plans, Customer Preferences, Device Collections  
**OUTPUT:** Auto Change Optimized Assignments

**Step 1: Filter Auto Change Enabled Plans**
- Examine rate plans for AutoChangeRatePlan flag set to true
- Filter collection to include only eligible auto change plans
- Match filtered plans with customer rate plan codes
- Create eligible auto change rate plans collection

**Step 2: Group by Plan Name Compatibility**
- Group auto change plans using PlanName as grouping key
- Ensure compatibility within each plan name group
- Create separate processing groups for each unique plan name
- Log plan name groups for customer tracking and validation

**Step 3: Validate Customer-Specific Constraints**
- Check customer rate pool assignments and compatibility
- Validate SIM pooling settings (AllowsSimPooling flag)
- Ensure rate plan changes comply with customer agreements
- Apply customer-specific optimization constraints

**Step 4: Process Plan Name Groups**
- For each plan name group, call ProcessPlanNameGroup method
- Apply customer-specific auto change logic and rules
- Handle Rev and AMOP customer scenarios appropriately
- Monitor for constraint violations and processing errors

**Step 5: Apply Rate Plan Change Validation**
- Validate zero value rate plans are excluded from auto change
- Check DataPerOverageCharge and OverageRate constraints
- Ensure auto change plans meet minimum cost thresholds
- Validate device count requirements for auto change processing

**Step 6: Execute Customer Auto Change Processing**
- Create communication plan groups for valid auto change plans
- Generate rate pool collections with customer preferences
- Enqueue optimization runs with customer-specific parameters
- Return auto change processing results with customer validation

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
- **Primary Processing:** Lines 549, 567, 572-577
```csharp
// Group rate plans by rate plan code and run auto change optimization logic for this group of devices
var ratePlansByCodes = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan && ratePlanCodes.Contains(ratePlan.PlanName)).GroupBy(x => x.PlanName);
foreach (var ratePlansByCode in ratePlansByCodes)
{
    isError = await ProcessPlanNameGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, billingPeriod, instanceId, chargeType, ratePlansByCode, simCardsByRatePoolId.ToList());
}

// Within ProcessPlanNameGroup - customer-specific validation
foreach (var ratePlanGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling))
{
    LogInfo(context, LogTypeConstant.Info, $"Allows SIM Pooling: {ratePlanGroup.Key}");

    // get rate plans for group
    var groupRatePlans = ratePlanGroup.ToList();
    var zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
    if (zeroValueRatePlans.Count > 0)
    {
        LogInfo(context, LogTypeConstant.Exception, $"The following rate plans in '{planNameGroup.Key}' has Data per Overage Charge or Overage Rate of 0. Please update to a non-zero value.{Environment.NewLine} {string.Join(',', zeroValueRatePlans.Select(ratePlan => ratePlan.PlanDisplayName))}");
        return true;
    }
}
```

---

## 4. Optimization Priority: Customer Savings Potential Sequencing

### What
Sequences with highest customer savings potential first through systematic permutation generation, cost analysis prioritization, and optimization queue ordering to maximize customer cost benefits and processing efficiency.

### Why
Essential for maximizing customer cost savings by processing highest-value optimization scenarios first. Ensures limited processing resources are allocated to combinations with greatest customer benefit potential.

### How
Generates rate pool sequences ordered by savings potential, validates device count thresholds for optimization effectiveness, creates priority-based queue assignments, and configures parallel processing with customer optimization parameters.

### Algorithm
**ALGORITHM: PrioritizeCustomerSavingsSequences**  
**INPUT:** Rate Pool Collection, Device Count, Customer Parameters  
**OUTPUT:** Priority-Ordered Optimization Sequences

**Step 1: Validate Device Count for Optimization**
- Check if baseAssignedSimCardsCount exceeds BaseAssignedDeviceLimit
- Ensure sufficient device population for meaningful optimization
- Skip permutation logic if device count insufficient for savings
- Log device count validation for customer optimization tracking

**Step 2: Generate Savings-Optimized Sequences**
- Call RatePoolAssigner.GenerateRatePoolSequences with rate pools
- Generate sequences ordered by customer savings potential
- Each sequence represents different customer cost optimization scenario
- Prioritize combinations with highest projected savings

**Step 3: Create Priority-Based Queues**
- Create optimization queues for each high-priority sequence
- Assign sequence order based on customer savings potential
- Configure queue parameters for customer optimization processing
- Ensure proper priority ordering for processing efficiency

**Step 4: Configure Customer Optimization Parameters**
- Set skipLowerCostCheck to true for customer optimization
- Configure isCustomerOptimization flag for proper processing
- Apply QueuesPerInstance limits for resource management
- Enable customer-specific optimization algorithms

**Step 5: Enqueue High-Priority Sequences First**
- Call EnqueueOptimizationRunsAsync with priority parameters
- Process sequences with highest customer savings potential first
- Configure parallel processing for efficient resource utilization
- Monitor queue processing for customer optimization effectiveness

**Step 6: Return Prioritized Processing Results**
- Return optimization queues ordered by customer savings priority
- Include customer savings potential metadata for tracking
- Log priority sequence creation for customer optimization monitoring
- Prepare high-priority sequences for immediate processing

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
- **Primary Processing:** Lines 602-603, 619, 631-633
```csharp
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

    // enqueue rate plan permutations with customer optimization priority
    await EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true);
}
else
{
    LogInfo(context, LogTypeConstant.Info, $"Plan name group for the rate plans {string.Join(',', ratePlanGroup.Select(plan => plan.Id).ToList())} only have {baseAssignedSimCardsCount} devices. The optimization by permutation logic will not be triggered.");
}

// Priority sequence generation
LogInfo(context, LogTypeConstant.Sub, detail: $"Start GenerateRatePoolSequences for {ratePoolCollection.RatePools.Count} Rate Plans");
var ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools);
LogInfo(context, LogTypeConstant.Sub, "End GenerateRatePoolSequences");
```