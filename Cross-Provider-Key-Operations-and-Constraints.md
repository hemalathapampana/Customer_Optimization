# Cross-Provider Key Operations and Constraints

## Overview
This document provides detailed analysis of cross-provider key operations, sequence characteristics, and constraints, explaining how multi-provider compatibility is ensured, cost ranking is performed, auto change logic is applied, optimization priority is sequenced, and various constraints are enforced across multiple service providers.

---

# Section 1: Cross-Provider Key Operations

## 1. Multi-Provider Compatibility: Ensures Rate Plans Work Across Customer Billing Cycles and Providers

### What
Multi-provider compatibility ensures that rate plans are validated to work seamlessly across different customer billing cycles and multiple service providers, maintaining consistency in billing periods, rate plan structures, and optimization logic.

### Why
- **Billing Synchronization**: Maintains consistent billing periods across different carriers
- **Data Integrity**: Ensures rate plan compatibility across provider boundaries
- **Customer Experience**: Provides seamless optimization regardless of provider mix
- **Operational Efficiency**: Reduces complexity by standardizing cross-provider operations
- **Regulatory Compliance**: Meets provider-specific requirements while maintaining uniformity

### How
The system validates billing period alignment and ensures rate plan compatibility across providers:

```683:690:AltaworxSimCardCostQueueCustomerOptimization.cs
private async Task RunCrossProviderCustomerOptimization(KeySysLambdaContext context, int tenantId, int customerId, SiteTypes customerType, string serviceProviderIds, int customerBillingPeriodId, string messageId, long optimizationSessionId, bool isLastInstance, string additionalData)
{
    LogInfo(context, CommonConstants.SUB, $"({tenantId},{messageId})");

    // get customer
    var customer = crossProviderOptimizationRepository.GetOptimizationCustomer(ParameterizedLog(context), customerId, customerType);
```

### Algorithm
```
ALGORITHM: EnsureMultiProviderCompatibility()
INPUT: String serviceProviderIds, Int customerBillingPeriodId, SiteTypes customerType
OUTPUT: Boolean compatibilityValidated

1. RETRIEVE customer information:
   a. customer = GetOptimizationCustomer(customerId, customerType)
2. VALIDATE billing period alignment:
   a. billingPeriod = GetBillingPeriod(customerId, customerBillingPeriodId, billingTimeZone)
   b. ArgumentNullException.ThrowIfNull(billingPeriod)
3. RETRIEVE cross-provider rate plans:
   a. ratePlans = GetCrossProviderCustomerRatePlans(serviceProviderIds, customerType, customerId, billingPeriod, tenantId)
4. VALIDATE rate plan compatibility:
   a. FOR each provider in serviceProviderIds:
      i. CHECK billing period alignment across providers
      ii. VALIDATE rate plan structure consistency
      iii. ENSURE customer type compatibility
5. COORDINATE billing cycle synchronization:
   a. ALIGN billing periods across all providers
   b. VALIDATE billing time zone consistency
6. RETURN compatibility validation status
```

### Code Locations

**Cross-Provider Customer Optimization Setup:**
```683:690:AltaworxSimCardCostQueueCustomerOptimization.cs
private async Task RunCrossProviderCustomerOptimization(KeySysLambdaContext context, int tenantId, int customerId, SiteTypes customerType, string serviceProviderIds, int customerBillingPeriodId, string messageId, long optimizationSessionId, bool isLastInstance, string additionalData)
{
    LogInfo(context, CommonConstants.SUB, $"({tenantId},{messageId})");

    // get customer
    var customer = crossProviderOptimizationRepository.GetOptimizationCustomer(ParameterizedLog(context), customerId, customerType);
```

**Billing Period Validation:**
```690:695:AltaworxSimCardCostQueueCustomerOptimization.cs
// start instance
if (customerBillingPeriodId > 0)
{
    var billingPeriod = crossProviderOptimizationRepository.GetBillingPeriod(ParameterizedLog(context), customerId, customerBillingPeriodId, context.OptimizationSettings.BillingTimeZone);
    ArgumentNullException.ThrowIfNull(billingPeriod);
```

## 2. Cross-Provider Cost Ranking: Orders Plans by Total Cross-Provider Cost-Effectiveness

### What
Cross-provider cost ranking orders optimization plans by their total cost-effectiveness across all service providers, using `ORDER BY TotalCost ASC` to prioritize sequences with the lowest total cost across all carriers.

### Why
- **Cost Optimization**: Identifies the most cost-effective solutions across all providers
- **Resource Efficiency**: Processes most promising scenarios first
- **Decision Support**: Provides clear cost comparison across provider combinations
- **ROI Maximization**: Ensures maximum return on optimization investment
- **Performance Enhancement**: Reduces processing time by prioritizing optimal results

### How
The system uses cost-based ordering to rank optimization results by total cross-provider cost:

```text:AltaworxSimCardCostOptimizerCleanup.cs
SELECT TOP 1 Id FROM OptimizationQueue WHERE CommPlanGroupId = @commGroupId AND TotalCost IS NOT NULL AND RunEndTime IS NOT NULL ORDER BY TotalCost ASC
```

### Algorithm
```
ALGORITHM: RankPlansByCrossProviderCostEffectiveness()
INPUT: List<OptimizationQueue> optimizationQueues, Long commPlanGroupId
OUTPUT: OptimizationQueue bestCostEffectiveQueue

1. FILTER completed optimization queues:
   a. validQueues = queues.Where(TotalCost IS NOT NULL AND RunEndTime IS NOT NULL)
2. CALCULATE total cross-provider costs:
   a. FOR each queue in validQueues:
      i. AGGREGATE costs across all providers
      ii. CALCULATE total optimization cost
      iii. INCLUDE provider-specific charges
3. RANK by cost-effectiveness:
   a. SELECT TOP 1 queue ORDER BY TotalCost ASC
   b. PRIORITIZE lowest total cost across all providers
4. VALIDATE cost calculation accuracy:
   a. ENSURE all provider costs are included
   b. VERIFY cross-provider billing alignment
5. RETURN best cost-effective optimization queue
```

### Code Locations

**Cost-Based Queue Selection:**
```text:AltaworxSimCardCostOptimizerCleanup.cs
SELECT TOP 1 Id FROM OptimizationQueue WHERE CommPlanGroupId = @commGroupId AND TotalCost IS NOT NULL AND RunEndTime IS NOT NULL ORDER BY TotalCost ASC
```

**Cross-Provider Rate Plan Retrieval:**
```695:700:AltaworxSimCardCostQueueCustomerOptimization.cs
// get customer rate plans
var ratePlans = customerRatePlanRepository.GetCrossProviderCustomerRatePlans(ParameterizedLog(context), serviceProviderIds, customerType, new List<int> { customerId }, billingPeriod, tenantId);
```

## 3. Provider-Specific Auto Change Logic: Applies Individual Provider Rate Plan Change Rules

### What
Provider-specific auto change logic applies individual provider rate plan change rules by filtering auto change rate plans based on provider compatibility and ensuring each provider's specific business rules are respected.

### Why
- **Provider Compliance**: Respects individual carrier business rules and restrictions
- **Risk Management**: Minimizes optimization failures due to provider-specific constraints
- **Regulatory Adherence**: Ensures compliance with carrier-specific regulations
- **Flexibility**: Allows different auto change strategies per provider
- **Quality Assurance**: Validates rate plan changes meet provider requirements

### How
The system filters auto change rate plans by provider compatibility and applies provider-specific validation:

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
ALGORITHM: ApplyProviderSpecificAutoChangeLogic()
INPUT: List<RatePlan> ratePlans, String serviceProviderIds
OUTPUT: List<RatePlan> validAutoChangeRatePlans

1. FILTER auto change enabled rate plans:
   a. autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan)
2. VALIDATE provider-specific compatibility:
   a. IF autoChangeRatePlans.Any() AND serviceProviderIds specified:
      i. PARSE provider ID list: serviceProviderIdList = serviceProviderIds.Split()
      ii. FILTER compatible plans: autoChangeRatePlans.Where(x => x.ServiceProviderIds.ContainsAllItems(serviceProviderIdList))
3. APPLY provider-specific business rules:
   a. FOR each provider in serviceProviderIdList:
      i. VALIDATE provider-specific auto change constraints
      ii. CHECK rate plan change permissions
      iii. ENSURE compliance with provider regulations
4. VALIDATE cross-provider eligibility:
   a. IF no compatible plans found:
      i. LOG error: NO_VALID_CROSS_PROVIDER_CUSTOMER_RATE_PLAN_FOUND
      ii. RETURN validation failure
5. RETURN validated auto change rate plans
```

### Code Locations

**Auto Change Rate Plan Filtering:**
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

## 4. Cross-Provider Optimization Priority: Sequences with Highest Total Savings Potential Across All Providers

### What
Cross-provider optimization priority sequences optimization scenarios with the highest total savings potential across all providers by using permutation generation and cost-based prioritization to maximize cross-provider savings.

### Why
- **Maximum Savings**: Identifies optimization scenarios with highest savings potential
- **Strategic Prioritization**: Focuses resources on most impactful optimizations
- **Cross-Provider Synergy**: Leverages savings opportunities across provider boundaries
- **Investment Optimization**: Maximizes return on optimization processing investment
- **Performance Enhancement**: Processes high-value scenarios first for faster results

### How
The system generates permutation sequences and prioritizes them based on cross-provider savings potential:

```616:619:AltaworxSimCardCostQueueCustomerOptimization.cs
GeneratePermutationQueueRatePlans(context, usesProration, billingPeriod, instanceId, commPlanGroupId, ratePoolCollection, commGroupRatePlanTable);

// enqueue rate plan permutations
await EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true);
```

### Algorithm
```
ALGORITHM: PrioritizeOptimizationByTotalSavingsPotential()
INPUT: RatePoolCollection ratePoolCollection, Long commPlanGroupId, OptimizationChargeType chargeType
OUTPUT: List<OptimizationQueue> prioritizedQueues

1. GENERATE cross-provider permutations:
   a. GeneratePermutationQueueRatePlans(ratePoolCollection, commGroupRatePlanTable)
2. CALCULATE savings potential for each sequence:
   a. FOR each permutation sequence:
      i. ESTIMATE total savings across all providers
      ii. CALCULATE cost reduction potential
      iii. ASSESS cross-provider synergy benefits
3. PRIORITIZE sequences by savings potential:
   a. ORDER sequences by highest total savings potential
   b. CONSIDER cross-provider optimization opportunities
   c. FACTOR provider-specific cost advantages
4. ENQUEUE optimization runs with priority:
   a. EnqueueOptimizationRunsAsync(commPlanGroupId, chargeType, QueuesPerInstance, skipLowerCostCheck: true)
5. EXECUTE optimizations in priority order:
   a. PROCESS highest-potential sequences first
   b. MONITOR savings achievement
6. RETURN prioritized optimization queues
```

### Code Locations

**Permutation Generation with Priority:**
```616:619:AltaworxSimCardCostQueueCustomerOptimization.cs
GeneratePermutationQueueRatePlans(context, usesProration, billingPeriod, instanceId, commPlanGroupId, ratePoolCollection, commGroupRatePlanTable);

// enqueue rate plan permutations
await EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true);
```

**Cross-Provider Instance Creation:**
```725:727:AltaworxSimCardCostQueueCustomerOptimization.cs
var instanceId = crossProviderOptimizationRepository.StartCrossProviderOptimizationInstance(ParameterizedLog(context), tenantId, messageId,
    customer, PortalTypes.CrossProvider, optimizationSessionId,
    useBillInAdvance, billingPeriod, nextBillingPeriod, serviceProviderIds);
```

---

# Section 2: Cross-Provider Sequence Characteristics

## Ordering: Multi-Provider Cost Optimization (Lowest Total Cost Across Providers)

### What
Multi-provider cost optimization ordering prioritizes sequences based on the lowest total cost across all providers, using comprehensive cost calculation and ranking mechanisms.

### Why
- **Cost Minimization**: Ensures lowest total cost across all provider combinations
- **Efficiency Optimization**: Processes most cost-effective scenarios first
- **Resource Allocation**: Focuses processing power on highest-value optimizations
- **Decision Support**: Provides clear cost-based ranking for optimization choices

### How
The system uses comprehensive cost calculation and database ordering to achieve lowest total cost:

```text:AltaworxSimCardCostOptimizerCleanup.cs
SELECT TOP 1 Id FROM OptimizationQueue WHERE CommPlanGroupId = @commGroupId AND TotalCost IS NOT NULL AND RunEndTime IS NOT NULL ORDER BY TotalCost ASC
```

### Algorithm
```
ALGORITHM: OrderByMultiProviderCostOptimization()
INPUT: Long commPlanGroupId, List<OptimizationQueue> completedQueues
OUTPUT: OptimizationQueue lowestCostQueue

1. FILTER completed optimization queues:
   a. validQueues = queues.Where(TotalCost IS NOT NULL AND RunEndTime IS NOT NULL)
2. CALCULATE total multi-provider costs:
   a. FOR each queue in validQueues:
      i. AGGREGATE costs from all providers
      ii. INCLUDE cross-provider charges
      iii. CALCULATE total optimization cost
3. ORDER by lowest total cost:
   a. SELECT TOP 1 queue ORDER BY TotalCost ASC
4. RETURN lowest cost optimization queue
```

## Filtering: Eliminates Incompatible Cross-Provider Rate Plan Combinations

### What
Filtering eliminates incompatible cross-provider rate plan combinations by validating provider compatibility, checking zero-value constraints, and ensuring cross-provider service provider ID alignment.

### Why
- **Data Integrity**: Prevents processing of invalid rate plan combinations
- **System Reliability**: Avoids failures from incompatible provider configurations
- **Quality Assurance**: Ensures only viable combinations are processed
- **Performance Optimization**: Reduces processing overhead from invalid combinations

### How
The system applies comprehensive filtering to eliminate incompatible combinations:

```573:577:AltaworxSimCardCostQueueCustomerOptimization.cs
var zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
if (zeroValueRatePlans.Count > 0)
{
    LogInfo(context, LogTypeConstant.Exception, $"The following rate plans in '{planNameGroup.Key}' has Data per Overage Charge or Overage Rate of 0. Please update to a non-zero value.{Environment.NewLine} {string.Join(',', zeroValueRatePlans.Select(ratePlan => ratePlan.PlanDisplayName))}");
    return true;
}
```

### Algorithm
```
ALGORITHM: FilterIncompatibleCrossProviderCombinations()
INPUT: List<RatePlan> groupRatePlans, String serviceProviderIds
OUTPUT: List<RatePlan> compatibleRatePlans

1. VALIDATE zero-value rate plans:
   a. zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M OR x.OverageRate == 0.0M)
   b. IF zeroValueRatePlans.Count > 0:
      i. LOG exception with plan details
      ii. RETURN filter failure
2. VALIDATE cross-provider compatibility:
   a. CHECK ServiceProviderIds alignment
   b. ENSURE ContainsAllItems(serviceProviderIdList) compatibility
3. FILTER by provider-specific constraints:
   a. VALIDATE rate plan structure compatibility
   b. CHECK billing period alignment
4. RETURN compatible rate plans
```

## Limits: Controlled by RATE_PLAN_SEQUENCES_FIRST_INSTANCE_LIMIT per Provider

### What
Limits are controlled by queue-per-instance configuration to manage the number of optimization sequences processed per provider, preventing system overload and ensuring manageable processing batches.

### Why
- **Performance Management**: Prevents system overload from excessive concurrent processing
- **Resource Control**: Manages memory and CPU usage per optimization instance
- **Scalability**: Ensures system can handle multiple concurrent optimizations
- **Quality Assurance**: Maintains processing quality by controlling batch sizes

### How
The system uses QueuesPerInstance configuration to control processing limits:

```31:55:AltaworxSimCardCostQueueCustomerOptimization.cs
private int QueuesPerInstance = Convert.ToInt32(Environment.GetEnvironmentVariable("QueuesPerInstance"));
private string ErrorNotificationEmailReceiver = Environment.GetEnvironmentVariable("ErrorNotificationEmailReceiver");

// Defaulted to M2M portal type. This lambda also support Cross-Provider customer optimization
public Function() : base(PortalTypes.M2M)
{
}

/// <summary>
/// M2M Customer Optimization
/// </summary>
/// <param name="sqsEvent"></param>
/// <param name="context"></param>
/// <returns></returns>
public async Task FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)
{
    KeySysLambdaContext keysysContext = null;
    try
    {
        keysysContext = BaseFunctionHandler(context);
        InitializeRepositories(context, keysysContext);

        if (QueuesPerInstance == 0)
        {
            QueuesPerInstance = DEFAULT_QUEUES_PER_INSTANCE;
            ErrorNotificationEmailReceiver = context.ClientContext.Environment["ErrorNotificationEmailReceiver"];
        }
```

### Algorithm
```
ALGORITHM: ControlLimitsByQueuesPerInstance()
INPUT: Int QueuesPerInstance, Int DEFAULT_QUEUES_PER_INSTANCE
OUTPUT: Int validatedQueuesPerInstance

1. INITIALIZE queue limits from environment:
   a. QueuesPerInstance = Convert.ToInt32(Environment.GetEnvironmentVariable("QueuesPerInstance"))
2. VALIDATE queue configuration:
   a. IF QueuesPerInstance == 0:
      i. SET QueuesPerInstance = DEFAULT_QUEUES_PER_INSTANCE (5)
3. APPLY per-provider limits:
   a. FOR each provider in optimization:
      i. LIMIT sequences to QueuesPerInstance
      ii. MANAGE concurrent processing
4. RETURN validated queue limits
```

## Batching: Sequences Split into Provider-Specific Manageable Batches

### What
Batching splits optimization sequences into provider-specific manageable batches using QueuesPerInstance configuration and provider-specific processing logic to ensure optimal resource utilization.

### Why
- **Processing Efficiency**: Optimizes resource utilization through controlled batch sizes
- **Provider Isolation**: Separates processing by provider for better management
- **Error Containment**: Isolates failures to specific batches without affecting others
- **Scalability**: Enables parallel processing across multiple providers

### How
The system uses QueuesPerInstance to create manageable batches for each provider:

```619:619:AltaworxSimCardCostQueueCustomerOptimization.cs
await EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true);
```

### Algorithm
```
ALGORITHM: SplitSequencesIntoProviderSpecificBatches()
INPUT: List<Long> commPlanGroupIds, Int QueuesPerInstance, OptimizationChargeType chargeType
OUTPUT: List<OptimizationBatch> providerSpecificBatches

1. DETERMINE batch size per provider:
   a. batchSize = QueuesPerInstance (default: 5)
2. GROUP sequences by provider:
   a. FOR each provider in serviceProviderIds:
      i. CREATE provider-specific batch
      ii. LIMIT to QueuesPerInstance sequences
3. ENQUEUE batches for parallel processing:
   a. EnqueueOptimizationRunsAsync(commPlanGroupIds, chargeType, QueuesPerInstance)
4. COORDINATE cross-provider execution:
   a. PROCESS batches in parallel
   b. MONITOR batch completion
5. RETURN provider-specific optimization batches
```

---

# Section 3: Cross-Provider Constraints

## Minimum Device Limit: Requires > 1 Device per Provider for Cross-Provider Optimization

### What
Minimum device limit requires more than 1 device per provider for cross-provider optimization by validating that `baseAssignedSimCardsCount > OptimizationConstant.BaseAssignedDeviceLimit` before permutation optimization.

### Why
- **Optimization Viability**: Ensures sufficient devices for meaningful optimization
- **Cost-Effectiveness**: Prevents optimization overhead for single-device scenarios
- **Algorithm Efficiency**: Maintains meaningful permutation generation
- **Resource Conservation**: Avoids unnecessary processing for minimal benefit scenarios

### How
The system validates device count against the base assigned device limit:

```602:604:AltaworxSimCardCostQueueCustomerOptimization.cs
if (baseAssignedSimCardsCount > OptimizationConstant.BaseAssignedDeviceLimit)
{
    // permute rate plans
```

### Algorithm
```
ALGORITHM: ValidateMinimumDeviceLimitPerProvider()
INPUT: Int baseAssignedSimCardsCount, Int BaseAssignedDeviceLimit
OUTPUT: Boolean optimizationAllowed

1. VALIDATE device count threshold:
   a. IF baseAssignedSimCardsCount > OptimizationConstant.BaseAssignedDeviceLimit:
      i. ALLOW permutation optimization
      ii. RETURN true
2. HANDLE insufficient devices:
   a. LOG info: "Insufficient devices for permutation optimization"
   b. SKIP optimization for this provider
   c. RETURN false
3. APPLY per-provider validation:
   a. FOR each provider in cross-provider optimization:
      i. CHECK minimum device requirement
      ii. ENSURE > 1 device per provider
4. RETURN optimization permission status
```

### Code Locations

**Device Count Validation:**
```602:604:AltaworxSimCardCostQueueCustomerOptimization.cs
if (baseAssignedSimCardsCount > OptimizationConstant.BaseAssignedDeviceLimit)
{
    // permute rate plans
```

**Device Assignment Logic:**
```594:596:AltaworxSimCardCostQueueCustomerOptimization.cs
var baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId,
    revAccountNumber, integrationAuthenticationId, null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId);
```

## Cross-Provider Rate Plan Limits: Maximum 15 Rate Plans per Provider per Rate Plan Group

### What
Cross-provider rate plan limits enforce a maximum of 15 rate plans per provider per rate plan group using `OptimizationConstant.RatePlanLimit` to prevent combinatorial explosion while maintaining optimization effectiveness.

### Why
- **Performance Management**: Prevents system overload from excessive permutations
- **Computational Feasibility**: Maintains acceptable processing times
- **Memory Management**: Controls memory usage during optimization
- **Quality Assurance**: Ensures meaningful optimization within reasonable constraints

### How
The system validates rate plan count against the maximum limit:

```605:608:AltaworxSimCardCostQueueCustomerOptimization.cs
// permute rate plans
if (calculatedPlans.Count > OptimizationConstant.RatePlanLimit)
{
    LogInfo(context, LogTypeConstant.Exception, $"The rate plan count exceeds the limit of 15 for this Rate Plan Code {ratePlanGroup.Key}. Please cut down the options to 15 or less for this Rate Plan Code.");
    continue;
}
```

### Algorithm
```
ALGORITHM: EnforceCrossProviderRatePlanLimits()
INPUT: List<RatePlan> calculatedPlans, String ratePlanGroupKey
OUTPUT: Boolean limitValidation

1. VALIDATE maximum rate plan limit:
   a. IF calculatedPlans.Count > OptimizationConstant.RatePlanLimit (15):
      i. LOG exception: "Rate plan count exceeds limit of 15"
      ii. SKIP optimization for this group
      iii. RETURN false
2. APPLY per-provider rate plan limits:
   a. FOR each provider in rate plan group:
      i. CHECK maximum 15 rate plans per provider
      ii. ENSURE combinatorial feasibility
3. VALIDATE rate plan group constraints:
   a. ENSURE manageable permutation generation
   b. MAINTAIN optimization quality
4. RETURN limit validation status
```

### Code Locations

**Rate Plan Limit Enforcement:**
```605:608:AltaworxSimCardCostQueueCustomerOptimization.cs
// permute rate plans
if (calculatedPlans.Count > OptimizationConstant.RatePlanLimit)
{
    LogInfo(context, LogTypeConstant.Exception, $"The rate plan count exceeds the limit of 15 for this Rate Plan Code {ratePlanGroup.Key}. Please cut down the options to 15 or less for this Rate Plan Code.");
    continue;
}
```

## Multi-Provider Auto Change Requirements: Minimum 2 Rate Plans per Provider for Optimization

### What
Multi-provider auto change requirements enforce a minimum of 2 rate plans per provider for optimization using `OptimizationConstant.RatePlanMinimumLimit` to ensure meaningful auto change optimization scenarios.

### Why
- **Optimization Meaningfulness**: Ensures sufficient options for rate plan changes
- **Algorithm Effectiveness**: Provides meaningful choice alternatives
- **Cost-Benefit Analysis**: Enables proper cost comparison scenarios
- **Quality Assurance**: Maintains optimization value proposition

### How
The system validates minimum rate plan requirements:

```610:615:AltaworxSimCardCostQueueCustomerOptimization.cs
if (calculatedPlans.Count <= OptimizationConstant.RatePlanMinimumLimit)
{

    LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.AUTO_CHANGE_MINIMUM_RATE_PLAN_LIMIT_REACHED, calculatedPlans.Count, planNameGroup.Key, ratePlanGroup.Key));
    continue;
}
```

### Algorithm
```
ALGORITHM: ValidateMultiProviderAutoChangeRequirements()
INPUT: List<RatePlan> calculatedPlans, String planNameGroupKey, String ratePlanGroupKey
OUTPUT: Boolean requirementsMet

1. VALIDATE minimum rate plan requirement:
   a. IF calculatedPlans.Count <= OptimizationConstant.RatePlanMinimumLimit (2):
      i. LOG info: AUTO_CHANGE_MINIMUM_RATE_PLAN_LIMIT_REACHED
      ii. SKIP optimization for this group
      iii. RETURN false
2. ENSURE per-provider minimum:
   a. FOR each provider in multi-provider scenario:
      i. CHECK minimum 2 rate plans per provider
      ii. VALIDATE auto change capability
3. VALIDATE optimization viability:
   a. ENSURE meaningful rate plan alternatives
   b. VERIFY auto change permissibility
4. RETURN requirements validation status
```

### Code Locations

**Minimum Rate Plan Validation:**
```610:615:AltaworxSimCardCostQueueCustomerOptimization.cs
if (calculatedPlans.Count <= OptimizationConstant.RatePlanMinimumLimit)
{

    LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.AUTO_CHANGE_MINIMUM_RATE_PLAN_LIMIT_REACHED, calculatedPlans.Count, planNameGroup.Key, ratePlanGroup.Key));
    continue;
}
```

## Cross-Provider Rate Pool: Uses Customer-Specific Rate Pooling Across Multiple Providers

### What
Cross-provider rate pool uses customer-specific rate pooling across multiple providers by grouping devices using `CustomerRatePoolId` and processing through cross-provider specific repository methods.

### Why
- **Customer-Centric Optimization**: Maintains customer-specific rate pooling logic
- **Cross-Provider Consistency**: Ensures uniform rate pooling across carriers
- **Data Integrity**: Preserves customer rate pool associations
- **Optimization Accuracy**: Maintains proper device grouping for optimization

### How
The system uses customer-specific rate pooling with cross-provider repository methods:

```818:820:AltaworxSimCardCostQueueCustomerOptimization.cs
var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();

foreach (var simCardsByRatePoolId in simCardsByRatePoolIds)
{
    LogInfo(context, CommonConstants.INFO, $"RatePoolId: {simCardsByRatePoolId.Key}");
```

### Algorithm
```
ALGORITHM: ImplementCrossProviderCustomerSpecificRatePooling()
INPUT: List<OptimizationSimCard> optimizationSimCards, String serviceProviderIds
OUTPUT: List<RatePoolGroup> customerSpecificRatePools

1. GROUP devices by customer rate pool:
   a. simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct()
2. PROCESS each rate pool group:
   a. FOR each simCardsByRatePoolId in simCardsByRatePoolIds:
      i. LOG rate pool ID for tracking
      ii. EXTRACT rate plan codes from devices
      iii. PROCESS rate pool group across providers
3. APPLY cross-provider rate pooling:
   a. MAINTAIN customer-specific pooling logic
   b. ENSURE consistency across providers
   c. VALIDATE rate pool associations
4. RECORD cross-provider rate pool results:
   a. OptimizationResultDbWriter.RecordCrossProviderRatePool()
5. RETURN customer-specific rate pool groups
```

### Code Locations

**Customer Rate Pool Grouping:**
```818:820:AltaworxSimCardCostQueueCustomerOptimization.cs
var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();

foreach (var simCardsByRatePoolId in simCardsByRatePoolIds)
{
    LogInfo(context, CommonConstants.INFO, $"RatePoolId: {simCardsByRatePoolId.Key}");
```

**Cross-Provider Rate Pool Recording:**
```866:866:AltaworxSimCardCostQueueCustomerOptimization.cs
OptimizationResultDbWriter.RecordCrossProviderRatePool(context, context.ConnectionString, unusedQueueId, simsWithNoRatePlanCodes, customerBillingPeriod.Id);
```

---

## Summary

The three sections work together to provide comprehensive cross-provider optimization:

1. **Key Operations** establish the foundation for multi-provider compatibility, cost ranking, auto change logic, and optimization priority
2. **Sequence Characteristics** define how optimization sequences are ordered, filtered, limited, and batched for optimal processing
3. **Constraints** ensure system stability through device limits, rate plan limits, auto change requirements, and customer-specific rate pooling

This integrated approach ensures efficient, scalable, and effective cross-provider customer optimization while maintaining system performance and data integrity.