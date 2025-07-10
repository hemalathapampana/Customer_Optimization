# Cross-Provider Rate Plan Processing

## Overview
This document provides detailed analysis of the cross-provider rate plan processing logic, explaining how customer rate plans are retrieved, filtered, grouped, validated, and processed across multiple service providers with provider-specific structures and constraints.

## 1. Retrieves Customer Rate Plans from All Associated Providers

### What
The system retrieves customer rate plans from all service providers associated with a customer's account, aggregating plans across multiple carriers to enable comprehensive cross-provider optimization.

### Why
- **Comprehensive Analysis**: Ensures all available rate plan options are considered for optimization
- **Cost Minimization**: Identifies the most cost-effective plans across all provider relationships
- **Unified View**: Provides a single source of truth for customer's rate plan portfolio
- **Provider Flexibility**: Allows customers to leverage relationships with multiple carriers

### How
Multi-provider rate plan retrieval through centralized repository access:

#### Algorithm:
```
1. EXTRACT serviceProviderIds from optimization context
2. VALIDATE customer billing period and context
3. DETERMINE retrieval method based on customer type:
   a. IF Rev Customer THEN
      i. CALL GetCustomerRatePlans(context, customerId, billingPeriodId, serviceProviderId, tenantId)
      ii. USE provider-specific authentication
   b. IF AMOP Customer THEN
      i. CALL GetCustomerRatePlans(context, Guid.Empty, billingPeriodId, serviceProviderId, tenantId, customerType, AMOPCustomerId)
      ii. USE unified authentication
   c. IF Cross-Provider Customer THEN
      i. CALL customerRatePlanRepository.GetCrossProviderCustomerRatePlans(serviceProviderIds, customerType, customerIds, billingPeriod, tenantId)
      ii. AGGREGATE plans from multiple providers
4. VALIDATE rate plan collection:
   a. CHECK if ratePlans.Count > 0
   b. LOG retrieval results
   c. IF no plans found THEN log error and terminate
5. RETURN consolidated rate plan collection
```

#### Code Locations:
```285:285:AltaworxSimCardCostQueueCustomerOptimization.cs
var ratePlans = GetCustomerRatePlans(context, customerId, (int)billingPeriodId, serviceProviderId, tenantId);
```

```403:403:AltaworxSimCardCostQueueCustomerOptimization.cs
var ratePlans = GetCustomerRatePlans(context, Guid.Empty, (int)billingPeriodId, serviceProviderId, tenantId, customerType, AMOPCustomerId);
```

```697:697:AltaworxSimCardCostQueueCustomerOptimization.cs
var ratePlans = customerRatePlanRepository.GetCrossProviderCustomerRatePlans(ParameterizedLog(context), serviceProviderIds, customerType, new List<int> { customerId }, billingPeriod, tenantId);
```

## 2. Filters Rate Plans by Cross-Provider Customer Eligibility

### What
The system applies eligibility filters to ensure only valid and accessible rate plans are included in the optimization process based on customer type, provider relationships, and account status.

### Why
- **Access Control**: Ensures customers only see plans they're eligible for
- **Compliance**: Enforces provider-specific eligibility requirements
- **Data Accuracy**: Prevents optimization with inaccessible rate plans
- **Customer Experience**: Avoids proposing unavailable options

### How
Multi-stage filtering process based on customer eligibility criteria:

#### Algorithm:
```
1. FILTER by customer rate plan codes:
   a. EXTRACT optimizationSimCards with valid CustomerRatePlanCode
   b. EXCLUDE devices with empty or null rate plan codes:
      optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode))
2. ASSESS bill-in-advance eligibility:
   a. COUNT rate plans with IsBillInAdvanceEligible = true
   b. SET useBillInAdvance flag based on count
   c. APPLY PORT-166 constraint (currently disabled)
3. VALIDATE provider association:
   a. FOR each rate plan:
      i. CHECK if customer has valid relationship with provider
      ii. VERIFY account access permissions
      iii. CONFIRM plan availability for customer type
4. FILTER by service provider scope:
   a. IF serviceProviderIds specified THEN
      i. FILTER plans matching specified providers
   b. ELSE
      i. INCLUDE all accessible providers for customer
5. APPLY customer-specific constraints:
   a. CHECK tenant permissions
   b. VALIDATE customer type compatibility
   c. ENSURE billing period alignment
```

#### Code Locations:
```512:514:AltaworxSimCardCostQueueCustomerOptimization.cs
if (revAccountNumber != null || AMOPCustomerId != null)
{
    optimizationSimCards = optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList();
}
```

```287:290:AltaworxSimCardCostQueueCustomerOptimization.cs
var useBillInAdvance = ratePlans.Count(x => x.IsBillInAdvanceEligible) > 0;
//Disable bill in advance logic until new logic is defined (PORT-166)
useBillInAdvance = false;
```

```787:787:AltaworxSimCardCostQueueCustomerOptimization.cs
optimizationSimCards = optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList();
```

## 3. Groups Rate Plans by Provider-Specific Auto Change Capabilities

### What
The system categorizes rate plans based on their auto-change capabilities, separating plans that support automatic rate plan changes from those that require manual intervention.

### Why
- **Optimization Strategy**: Different optimization algorithms for auto vs manual change plans
- **Operational Efficiency**: Automates rate plan changes where possible
- **Risk Management**: Handles manual plans with appropriate oversight
- **Provider Compliance**: Respects provider-specific change policies

### How
Dual-path processing based on AutoChangeRatePlan flag:

#### Algorithm:
```
1. SEPARATE rate plans by auto-change capability:
   a. ratePlansByCustomerRatePool = ratePlans.Where(ratePlan => !ratePlan.AutoChangeRatePlan)
   b. autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan)
2. PROCESS customer rate pool plans (non-auto-change):
   a. IF ratePlansByCustomerRatePool.Any() THEN
      i. VALIDATE zero-value constraints
      ii. CALL ProcessDevicesWithAutoChangeDisabledRatePlans()
      iii. HANDLE pooled optimization logic
3. VALIDATE auto-change plans for cross-provider compatibility:
   a. IF autoChangeRatePlans.Any() AND serviceProviderIds specified THEN
      i. PARSE serviceProviderIdList from comma-separated string
      ii. FILTER plans by provider compatibility:
         autoChangeRatePlans.Where(x => x.ServiceProviderIds.Split().ContainsAllItems(serviceProviderIdList))
      iii. VALIDATE at least one compatible plan exists
4. GROUP auto-change plans by plan name:
   a. ratePlansByCodes = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan && ratePlanCodes.Contains(ratePlan.PlanName)).GroupBy(x => x.PlanName)
   b. FOR each planNameGroup:
      i. CALL ProcessPlanNameGroup() for optimization
5. FURTHER GROUP by SIM pooling capability:
   a. FOR each planNameGroup.GroupBy(x => x.AllowsSimPooling):
      i. SEPARATE plans by pooling support
      ii. APPLY different optimization strategies per group
```

#### Code Locations:
```518:518:AltaworxSimCardCostQueueCustomerOptimization.cs
var ratePlansByCustomerRatePool = ratePlans.Where(ratePlan => !ratePlan.AutoChangeRatePlan).ToList();
```

```549:552:AltaworxSimCardCostQueueCustomerOptimization.cs
var ratePlansByCodes = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan && ratePlanCodes.Contains(ratePlan.PlanName)).GroupBy(x => x.PlanName);
foreach (var ratePlansByCode in ratePlansByCodes)
{
    isError = await ProcessPlanNameGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, billingPeriod, instanceId, chargeType, ratePlansByCode, simCardsByRatePoolId.ToList());
}
```

```806:813:AltaworxSimCardCostQueueCustomerOptimization.cs
var autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan);
if (autoChangeRatePlans.Any() && !string.IsNullOrWhiteSpace(serviceProviderIds))
{
    var serviceProviderIdList = serviceProviderIds.Replace(" ", "").Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList();
    autoChangeRatePlans = autoChangeRatePlans.Where(x => x.ServiceProviderIds.Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList().ContainsAllItems(serviceProviderIdList)).ToList();
    LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.CROSS_PROVIDER_CUSTOMER_RATE_PLAN_FOUND, serviceProviderIds));
}
```

```567:567:AltaworxSimCardCostQueueCustomerOptimization.cs
foreach (var ratePlanGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling))
```

## 4. Validates Cross-Provider Rate Plan Compatibility and Overage Rates

### What
The system performs comprehensive validation to ensure rate plans are compatible across providers and have valid overage rate structures before optimization execution.

### Why
- **Data Integrity**: Prevents optimization with invalid rate plan data
- **Cost Accuracy**: Ensures overage calculations are mathematically valid
- **Risk Mitigation**: Avoids optimization failures due to incompatible plans
- **Regulatory Compliance**: Validates rate structures meet provider requirements

### How
Multi-layer validation process for rate plan compatibility:

#### Algorithm:
```
1. VALIDATE overage rate structures:
   a. FOR each rate plan in group:
      i. CHECK DataPerOverageCharge != 0.0M
      ii. CHECK OverageRate != 0.0M
      iii. IF zero values found THEN
         - LOG detailed error with plan names
         - EXCLUDE from optimization
         - RETURN validation failure
2. VALIDATE rate plan count limits:
   a. CHECK calculatedPlans.Count <= OptimizationConstant.RatePlanLimit (15)
   b. CHECK calculatedPlans.Count >= OptimizationConstant.RatePlanMinimumLimit
   c. IF limits exceeded THEN
      i. LOG warning with specific limits
      ii. SKIP optimization for this group
3. VALIDATE cross-provider compatibility:
   a. FOR Cross-Provider scenarios:
      i. ENSURE ServiceProviderIds alignment
      ii. VALIDATE billing period synchronization
      iii. CHECK integration compatibility
4. VALIDATE device assignment thresholds:
   a. CHECK baseAssignedSimCardsCount > OptimizationConstant.BaseAssignedDeviceLimit
   b. IF insufficient devices THEN skip permutation optimization
5. VERIFY rate plan structural integrity:
   a. VALIDATE all required fields are populated
   b. CHECK for data consistency across providers
   c. ENSURE plan relationships are valid
```

#### Code Locations:
```573:577:AltaworxSimCardCostQueueCustomerOptimization.cs
var zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
if (zeroValueRatePlans.Count > 0)
{
    LogInfo(context, LogTypeConstant.Exception, $"The following rate plans in '{planNameGroup.Key}' has Data per Overage Charge or Overage Rate of 0. Please update to a non-zero value.{Environment.NewLine} {string.Join(',', zeroValueRatePlans.Select(ratePlan => ratePlan.PlanDisplayName))}");
    return true;
}
```

```608:616:AltaworxSimCardCostQueueCustomerOptimization.cs
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

```521:525:AltaworxSimCardCostQueueCustomerOptimization.cs
if (CheckZeroValueRatePlans(context, instanceId, ratePlansByCustomerRatePool, shouldStopInstance: true))
{
    return true;
}
```

## 5. Handles Provider-Specific Rate Plan Structures and Limitations

### What
The system accommodates different rate plan structures, billing methodologies, and operational limitations specific to each service provider while maintaining consistent optimization logic.

### Why
- **Provider Diversity**: Each carrier has unique rate plan structures and constraints
- **Integration Flexibility**: Supports various provider API formats and limitations
- **Business Rule Compliance**: Enforces provider-specific operational rules
- **Scalability**: Enables addition of new providers without architectural changes

### How
Provider-agnostic processing with provider-specific adaptation layers:

#### Algorithm:
```
1. IDENTIFY provider-specific structures:
   a. EXTRACT ServiceProviderIds from rate plan metadata
   b. DETERMINE integration type per provider
   c. LOAD provider-specific constraints and limitations
2. HANDLE bill-in-advance structures:
   a. FOR each provider supporting bill-in-advance:
      i. VALIDATE nextBillingPeriod availability
      ii. SET appropriate charge type (OverageOnly vs RateChargeAndOverage)
      iii. CALCULATE bill-in-advance billing period ID
3. ACCOMMODATE provider pooling capabilities:
   a. CHECK AllowsSimPooling flag per rate plan
   b. GROUP rate plans by pooling capability
   c. APPLY provider-specific pooling rules
4. HANDLE provider rate plan limits:
   a. APPLY provider-specific rate plan count limits
   b. VALIDATE provider-specific rate structures
   c. ENFORCE provider billing constraints
5. PROCESS provider-specific metadata:
   a. HANDLE CustomerRatePoolId associations
   b. PROCESS PlanName and PlanDisplayName variations
   c. MANAGE provider-specific identifiers and codes
6. COORDINATE cross-provider operations:
   a. CREATE unified optimization queues
   b. GENERATE provider-specific permutations
   c. AGGREGATE results across providers
7. HANDLE provider-specific error conditions:
   a. GRACEFUL degradation for provider failures
   b. PROVIDER-SPECIFIC error logging and handling
   c. PARTIAL optimization when some providers unavailable
```

#### Code Locations:
```532:534:AltaworxSimCardCostQueueCustomerOptimization.cs
var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();

foreach (var simCardsByRatePoolId in simCardsByRatePoolIds)
{
```

```543:543:AltaworxSimCardCostQueueCustomerOptimization.cs
var ratePlansForPool = ratePlans.Where(x => ratePlanCodes.Contains(x.PlanName));
```

```590:595:AltaworxSimCardCostQueueCustomerOptimization.cs
var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);

var baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId,
    revAccountNumber, integrationAuthenticationId, null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId);
```

```642:660:AltaworxSimCardCostQueueCustomerOptimization.cs
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
```

```725:727:AltaworxSimCardCostQueueCustomerOptimization.cs
var instanceId = crossProviderOptimizationRepository.StartCrossProviderOptimizationInstance(ParameterizedLog(context), tenantId, messageId,
    customer, PortalTypes.CrossProvider, optimizationSessionId,
    useBillInAdvance, billingPeriod, nextBillingPeriod, serviceProviderIds);
```

## Implementation Architecture

### Rate Plan Processing Flow
```
Provider Discovery → Rate Plan Retrieval → Eligibility Filtering → 
Auto-Change Grouping → Compatibility Validation → Structure Adaptation → 
Optimization Execution → Result Aggregation
```

### Multi-Provider Coordination
- **Unified Repository Interface**: Single access point for all provider rate plans
- **Provider-Specific Adapters**: Handle unique provider structures and constraints
- **Cross-Provider Validation**: Ensures compatibility across provider boundaries
- **Aggregated Result Processing**: Combines optimization results from multiple providers

### Optimization Strategy Selection
1. **Pooled Rate Plans**: Customer rate pool-based optimization
2. **Auto-Change Plans**: Permutation-based optimization with automatic rate changes
3. **Manual Plans**: Traditional optimization with manual intervention requirements
4. **Cross-Provider Plans**: Unified optimization across multiple providers

### Error Handling and Resilience
- **Provider-Specific Error Handling**: Tailored error management per provider
- **Graceful Degradation**: Continues optimization with available providers
- **Comprehensive Validation**: Prevents optimization with invalid data
- **Detailed Logging**: Provider-specific audit trails and troubleshooting information

This rate plan processing framework ensures comprehensive, accurate, and efficient cross-provider optimization while respecting the unique characteristics and constraints of each service provider.