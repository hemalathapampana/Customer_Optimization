# Cross-Provider Bill in Advance Features

## Overview
This document provides detailed analysis of the cross-provider bill in advance (BIA) features, explaining how rate plans are identified for advance billing, next billing periods are loaded, charge types are set, and BIA processing is coordinated across multiple providers with provider-specific requirements and limitations.

## 1. Identifies Rate Plans Eligible for BIA Across All Providers

### What
The system identifies and counts rate plans that have the `IsBillInAdvanceEligible` flag set to true across all service providers associated with a customer, determining whether advance billing should be enabled for the optimization session.

### Why
- **Advance Cost Planning**: Enables customers to calculate and prepare for upcoming billing cycles
- **Cash Flow Management**: Helps businesses plan their telecommunications expenses in advance
- **Billing Synchronization**: Ensures consistent billing practices across multiple service providers
- **Compliance Requirements**: Meets regulatory and contractual obligations for advance billing disclosure
- **Risk Mitigation**: Identifies potential billing issues before they impact operations

### How
The system aggregates customer rate plans from all providers and performs a count operation to determine BIA eligibility:

```12:15:AltaworxSimCardCostQueueCustomerOptimization.cs
var useBillInAdvance = ratePlans.Count(x => x.IsBillInAdvanceEligible) > 0;
//Disable bill in advance logic until new logic is defined (PORT-166)
useBillInAdvance = false;
```

### Algorithm
```
ALGORITHM: IdentifyBillInAdvanceEligibleRatePlans()
INPUT: List<RatePlan> ratePlans
OUTPUT: Boolean useBillInAdvance

1. INITIALIZE count = 0
2. FOR each ratePlan in ratePlans:
   a. IF ratePlan.IsBillInAdvanceEligible == true:
      i. INCREMENT count
3. SET useBillInAdvance = (count > 0)
4. APPLY constraint: useBillInAdvance = false (PORT-166)
5. LOG eligibility status
6. RETURN useBillInAdvance
```

### Code Locations

**Rev Customer Processing:**
```287:289:AltaworxSimCardCostQueueCustomerOptimization.cs
var useBillInAdvance = ratePlans.Count(x => x.IsBillInAdvanceEligible) > 0;
//Disable bill in advance logic until new logic is defined (PORT-166)
useBillInAdvance = false;
```

**AMOP Customer Processing:**
```404:406:AltaworxSimCardCostQueueCustomerOptimization.cs
var useBillInAdvance = ratePlans.Count(x => x.IsBillInAdvanceEligible) > 0;

LogInfo(context, "INFO", $"Use Bill In Advance: {useBillInAdvance}");
```

**Cross-Provider Processing:**
```699:701:AltaworxSimCardCostQueueCustomerOptimization.cs
var useBillInAdvance = ratePlans.Count(x => x.IsBillInAdvanceEligible) > 0;
//Disable bill in advance logic until new logic is defined (PORT-166)
useBillInAdvance = false;
```

## 2. Loads Next Billing Period for Cross-Provider Advance Billing Calculations

### What
The system loads the next billing period following the current billing cycle for each provider to enable advance billing calculations and ensure proper billing period alignment across multiple providers.

### Why
- **Future Cost Calculation**: Enables accurate calculation of charges for the upcoming billing cycle
- **Billing Period Continuity**: Ensures seamless transition between billing periods
- **Multi-Provider Synchronization**: Maintains consistent billing timeline across different carriers
- **Data Integrity**: Prevents billing gaps and overlaps in advance billing scenarios
- **Regulatory Compliance**: Meets advance billing notification requirements

### How
The system retrieves the next billing period using provider-specific methods and validates availability:

```297:303:AltaworxSimCardCostQueueCustomerOptimization.cs
BillingPeriod nextBillingPeriod = null;
if (billingPeriod != null)
{
    nextBillingPeriod = GetNextBillingPeriod(context, billingPeriod.ServiceProviderId, billingPeriod.BillingPeriodEnd);
}

var billInAdvanceBillingPeriodId = nextBillingPeriod?.Id;
```

### Algorithm
```
ALGORITHM: LoadNextBillingPeriodForAdvanceBilling()
INPUT: BillingPeriod currentBillingPeriod, Context context
OUTPUT: BillingPeriod nextBillingPeriod, Long billInAdvanceBillingPeriodId

1. INITIALIZE nextBillingPeriod = null
2. IF currentBillingPeriod != null:
   a. CALL GetNextBillingPeriod(context, serviceProviderId, billingPeriodEnd)
   b. SET nextBillingPeriod = result
3. EXTRACT billInAdvanceBillingPeriodId from nextBillingPeriod
4. LOG billInAdvanceBillingPeriodId
5. VALIDATE billing period availability
6. IF useBillInAdvance AND (nextBillingPeriod == null OR currentBillingPeriod == null):
   a. LOG error: "Billing period not found for advance billing"
   b. ABORT optimization
7. RETURN nextBillingPeriod, billInAdvanceBillingPeriodId
```

### Code Locations

**Rev Customer Processing:**
```297:310:AltaworxSimCardCostQueueCustomerOptimization.cs
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
```

**AMOP Customer Processing:**
```412:425:AltaworxSimCardCostQueueCustomerOptimization.cs
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
```

**Cross-Provider Processing:**
```705:718:AltaworxSimCardCostQueueCustomerOptimization.cs
BillingPeriod nextBillingPeriod = null;
if (billingPeriod != null)
{
    nextBillingPeriod = crossProviderOptimizationRepository.GetBillingPeriod(ParameterizedLog(context), customerId, billingPeriod.BillingPeriodEnd.AddMonths(CommonConstants.BILL_CYCLE_LENGTH_IN_MONTHS), context.OptimizationSettings.BillingTimeZone);
}

var billInAdvanceBillingPeriodId = nextBillingPeriod?.Id;

LogVariableValue(context, nameof(billInAdvanceBillingPeriodId), billInAdvanceBillingPeriodId);

if (useBillInAdvance && (billInAdvanceBillingPeriodId == null || billingPeriod == null))
{
    LogInfo(context, CommonConstants.ERROR, $"A Billing Period past Customer Billing Period Id of {customerBillingPeriodId} could not be found for this Customer. So, billing in advance is not possible at this time. Optimization not run.");
    return;
}
```

## 3. Sets Provider-Specific Charge Types for Advance Billing Scenarios

### What
The system determines and sets appropriate charge types based on bill-in-advance status, switching between `RateChargeAndOverage` for normal billing and `OverageOnly` for advance billing scenarios.

### Why
- **Accurate Billing Calculation**: Ensures correct charge application based on billing timing
- **Advance Billing Compliance**: Meets regulatory requirements for advance billing transparency
- **Cost Prediction Accuracy**: Provides precise cost estimates for future billing periods
- **Provider Differentiation**: Handles provider-specific charging methodologies
- **Financial Planning**: Enables customers to understand exact charge breakdown

### How
The system conditionally sets charge types based on BIA status:

```322:326:AltaworxSimCardCostQueueCustomerOptimization.cs
var chargeType = OptimizationChargeType.RateChargeAndOverage;
if (useBillInAdvance)
{
    chargeType = OptimizationChargeType.OverageOnly;
}
```

### Algorithm
```
ALGORITHM: SetProviderSpecificChargeTypes()
INPUT: Boolean useBillInAdvance
OUTPUT: OptimizationChargeType chargeType

1. INITIALIZE chargeType = OptimizationChargeType.RateChargeAndOverage
2. IF useBillInAdvance == true:
   a. SET chargeType = OptimizationChargeType.OverageOnly
3. LOG charge type selection
4. VALIDATE charge type compatibility with provider
5. RETURN chargeType
```

### Code Locations

**Rev Customer Processing:**
```322:326:AltaworxSimCardCostQueueCustomerOptimization.cs
// what kind of charge type should we use?
var chargeType = OptimizationChargeType.RateChargeAndOverage;
if (useBillInAdvance)
{
    chargeType = OptimizationChargeType.OverageOnly;
}
```

**AMOP Customer Processing:**
```437:441:AltaworxSimCardCostQueueCustomerOptimization.cs
// what kind of charge type should we use?
var chargeType = OptimizationChargeType.RateChargeAndOverage;
if (useBillInAdvance)
{
    chargeType = OptimizationChargeType.OverageOnly;
}
```

**Cross-Provider Processing:**
```729:729:AltaworxSimCardCostQueueCustomerOptimization.cs
OptimizationChargeType chargeType = GetChargeType(useBillInAdvance);
```

## 4. Coordinates BIA Processing Across Multiple Providers Simultaneously

### What
The system coordinates bill-in-advance processing across multiple service providers (Verizon, AT&T, T-Mobile) simultaneously, ensuring consistent advance billing calculations and provider synchronization.

### Why
- **Unified Customer Experience**: Provides consistent billing experience across all providers
- **Operational Efficiency**: Reduces processing time through parallel execution
- **Data Consistency**: Maintains synchronized billing data across provider boundaries
- **Cost Optimization**: Enables cross-provider cost comparison in advance billing scenarios
- **Business Continuity**: Ensures uninterrupted service across multiple carriers

### How
The system initiates cross-provider optimization instances and processes devices across all providers:

```725:727:AltaworxSimCardCostQueueCustomerOptimization.cs
var instanceId = crossProviderOptimizationRepository.StartCrossProviderOptimizationInstance(ParameterizedLog(context), tenantId, messageId,
    customer, PortalTypes.CrossProvider, optimizationSessionId,
    useBillInAdvance, billingPeriod, nextBillingPeriod, serviceProviderIds);
```

### Algorithm
```
ALGORITHM: CoordinateBIAProcessingAcrossProviders()
INPUT: String serviceProviderIds, Boolean useBillInAdvance, BillingPeriod billingPeriod, BillingPeriod nextBillingPeriod
OUTPUT: Long instanceId, Boolean processingSuccess

1. PARSE serviceProviderIds into provider list
2. VALIDATE each provider supports BIA
3. START cross-provider optimization instance:
   a. CALL StartCrossProviderOptimizationInstance()
   b. PASS useBillInAdvance, billingPeriod, nextBillingPeriod, serviceProviderIds
   c. RECEIVE instanceId
4. DETERMINE charge type based on BIA status
5. FOR each provider in serviceProviderIds:
   a. VALIDATE provider-specific BIA requirements
   b. SYNCHRONIZE billing period alignment
   c. COORDINATE advance billing calculations
6. PROCESS devices across all providers simultaneously
7. HANDLE provider-specific BIA constraints
8. RETURN instanceId, processingSuccess
```

### Code Locations

**Cross-Provider Instance Creation:**
```725:727:AltaworxSimCardCostQueueCustomerOptimization.cs
var instanceId = crossProviderOptimizationRepository.StartCrossProviderOptimizationInstance(ParameterizedLog(context), tenantId, messageId,
    customer, PortalTypes.CrossProvider, optimizationSessionId,
    useBillInAdvance, billingPeriod, nextBillingPeriod, serviceProviderIds);
```

**Multi-Provider Device Processing:**
```733:735:AltaworxSimCardCostQueueCustomerOptimization.cs
var isError = await ProcessCrossProviderDevicesByCustomerRatePlans(context, serviceProviderIds, false, ratePlans, billingPeriod, nextBillingPeriod, instanceId, chargeType, customer, tenantId);

if (!isError)
{
    // enqueue cleanup method
    EnqueueCleanup(context, instanceId, isCustomerOptimization: true, isLastInstance: isLastInstance);
}
```

**Provider Coordination Logic:**
```808:820:AltaworxSimCardCostQueueCustomerOptimization.cs
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

var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();
```

## 5. Handles Provider-Specific BIA Requirements and Limitations

### What
The system handles provider-specific bill-in-advance requirements and limitations, including disabled BIA logic (PORT-166 constraint), rate plan eligibility validation, and provider-specific billing period requirements.

### Why
- **Provider Compliance**: Meets individual carrier requirements for advance billing
- **Risk Management**: Prevents optimization failures due to provider limitations
- **Regulatory Adherence**: Ensures compliance with provider-specific regulations
- **Business Rule Enforcement**: Applies carrier-specific business logic consistently
- **System Stability**: Prevents system errors from incompatible provider configurations

### How
The system applies multiple layers of validation and constraint handling:

```700:701:AltaworxSimCardCostQueueCustomerOptimization.cs
//Disable bill in advance logic until new logic is defined (PORT-166)
useBillInAdvance = false;
```

### Algorithm
```
ALGORITHM: HandleProviderSpecificBIARequirements()
INPUT: List<RatePlan> ratePlans, String serviceProviderIds, Boolean useBillInAdvance
OUTPUT: Boolean finalBIAStatus, List<String> validationErrors

1. APPLY global constraint PORT-166:
   a. SET useBillInAdvance = false
   b. LOG constraint application
2. FOR each provider in serviceProviderIds:
   a. VALIDATE provider supports BIA
   b. CHECK provider-specific rate plan requirements
   c. VERIFY billing period compatibility
3. VALIDATE cross-provider rate plan eligibility:
   a. FILTER rate plans by AutoChangeRatePlan = true
   b. CHECK ServiceProviderIds compatibility
   c. VALIDATE ContainsAllItems(serviceProviderIdList)
4. IF no valid cross-provider rate plans found:
   a. LOG error: "No valid cross-provider customer rate plan found"
   b. RETURN error status
5. HANDLE provider-specific limitations:
   a. APPLY rate plan count limits (15 maximum)
   b. CHECK minimum rate plan requirements (2 minimum)
   c. VALIDATE zero-value rate plan constraints
6. COORDINATE provider-specific billing periods
7. RETURN finalBIAStatus, validationErrors
```

### Code Locations

**Global BIA Constraint:**
```700:701:AltaworxSimCardCostQueueCustomerOptimization.cs
//Disable bill in advance logic until new logic is defined (PORT-166)
useBillInAdvance = false;
```

**Provider Rate Plan Validation:**
```816:823:AltaworxSimCardCostQueueCustomerOptimization.cs
var autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan);
if (autoChangeRatePlans.Any() && !string.IsNullOrWhiteSpace(serviceProviderIds))
{
    var serviceProviderIdList = serviceProviderIds.Replace(" ", "").Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList();
    autoChangeRatePlans = autoChangeRatePlans.Where(x => x.ServiceProviderIds.Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList().ContainsAllItems(serviceProviderIdList)).ToList();
    if (!autoChangeRatePlans.Any())
    {
        LogInfo(context, CommonConstants.ERROR, string.Format(LogCommonStrings.NO_VALID_CROSS_PROVIDER_CUSTOMER_RATE_PLAN_FOUND, serviceProviderIds));
        return true;
```

**Cross-Provider Eligibility Check:**
```811:824:AltaworxSimCardCostQueueCustomerOptimization.cs
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

var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();

foreach (var simCardsByRatePoolId in simCardsByRatePoolIds)
{
    LogInfo(context, CommonConstants.INFO, $"RatePoolId: {simCardsByRatePoolId.Key}");
```

**Rate Plan Limit Enforcement:**
```601:608:AltaworxSimCardCostQueueCustomerOptimization.cs
// permute rate plans
if (calculatedPlans.Count > OptimizationConstant.RatePlanLimit)
{
    LogInfo(context, LogTypeConstant.Exception, $"The rate plan count exceeds the limit of 15 for this Rate Plan Code {ratePlanGroup.Key}. Please cut down the options to 15 or less for this Rate Plan Code.");
    continue;
}
if (calculatedPlans.Count <= OptimizationConstant.RatePlanMinimumLimit)
{
    LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.AUTO_CHANGE_MINIMUM_RATE_PLAN_LIMIT_REACHED, calculatedPlans.Count, planNameGroup.Key, ratePlanGroup.Key));
```

**Zero-Value Rate Plan Validation:**
```586:591:AltaworxSimCardCostQueueCustomerOptimization.cs
// get rate plans for group
var groupRatePlans = ratePlanGroup.ToList();
var zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
if (zeroValueRatePlans.Count > 0)
{
    LogInfo(context, LogTypeConstant.Exception, $"The following rate plans in '{planNameGroup.Key}' has Data per Overage Charge or Overage Rate of 0. Please update to a non-zero value.{Environment.NewLine} {string.Join(',', zeroValueRatePlans.Select(ratePlan => ratePlan.PlanDisplayName))}");
    return true;
```

## Implementation Status and Constraints

### Current Limitations
1. **PORT-166 Constraint**: BIA logic is currently disabled system-wide until new logic is defined
2. **Auto Change Compatibility**: BIA calculation logic is not implemented for optimization with auto change rate plan enabled
3. **Provider Synchronization**: Some provider-specific BIA requirements may not be fully synchronized

### Future Considerations
1. **BIA Re-enablement**: Implementation of new BIA logic to replace PORT-166 constraint
2. **Enhanced Provider Support**: Extended support for provider-specific BIA variations
3. **Real-time Coordination**: Improved real-time coordination across multiple providers

## Error Handling and Validation

The system implements comprehensive error handling for BIA scenarios:

1. **Missing Billing Period**: Validates next billing period availability before enabling BIA
2. **Provider Compatibility**: Ensures all providers support BIA before processing
3. **Rate Plan Validation**: Checks rate plan eligibility and compatibility
4. **Cross-Provider Synchronization**: Validates provider alignment and coordination

This comprehensive BIA framework ensures reliable and compliant advance billing processing across all supported service providers while maintaining system stability and data integrity.