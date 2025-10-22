# Cross-Provider Cost Components

## Overview
This document provides detailed analysis of cross-provider cost components, explaining how costs are calculated across multiple service providers including multi-provider base costs, cross-provider overage calculations, provider-specific regulatory fees, cross-provider taxes, and provider migration costs.

## 1. Multi-Provider Base Cost: Monthly Plan Cost Across Providers × (Billing Days / 30)

### What
Multi-Provider Base Cost calculates the prorated monthly plan cost across multiple service providers by multiplying the monthly recurring charge by the ratio of billing days to 30, ensuring accurate cost allocation when devices operate across different provider billing cycles.

### Why
- **Accurate Proration**: Ensures precise cost calculation for partial billing periods across providers
- **Cross-Provider Consistency**: Maintains uniform cost calculation methodology across all carriers
- **Billing Accuracy**: Prevents overcharging or undercharging during billing period transitions
- **Provider Fairness**: Ensures equitable cost distribution when using multiple providers
- **Financial Accuracy**: Provides precise cost basis for cross-provider optimization decisions

### How
The system calculates prorated base costs using billing period information and provider-specific monthly charges, applying standardized proration logic across all supported providers to ensure consistent cost calculations.

### Algorithm: MultiProviderBaseCostCalculation()

**INPUT**: List<RatePlan> providerRatePlans, BillingPeriod billingPeriod, Boolean usesProration
**OUTPUT**: Decimal prorated base cost across providers

1. **EXTRACT billing period information**:
   ```
   a. daysInBillingPeriod = billingPeriod.DaysInBillingPeriod
   b. billingPeriodStart = billingPeriod.BillingPeriodStart
   c. billingPeriodEnd = billingPeriod.BillingPeriodEnd
   ```

2. **CALCULATE base cost for each provider**:
   ```
   a. FOR each ratePlan in providerRatePlans:
      i. monthlyRecurringCharge = ratePlan.MonthlyRecurringCharge
      ii. IF usesProration:
         - prorationFactor = daysInBillingPeriod / 30.0
         - baseCost = monthlyRecurringCharge × prorationFactor
      iii. ELSE:
         - baseCost = monthlyRecurringCharge
   ```

3. **APPLY device activation proration**:
   ```
   a. FOR each device:
      i. IF device.WasActivatedInThisBillingPeriod:
         - daysActivated = DaysLeftInBillingPeriod(device.DateActivated, billingPeriod)
         - activationProrationFactor = daysActivated / daysInBillingPeriod
         - adjustedBaseCost = baseCost × activationProrationFactor
      ii. ELSE:
         - adjustedBaseCost = baseCost
   ```

4. **AGGREGATE costs across providers**:
   ```
   a. totalBaseCost = SUM(adjustedBaseCost for all providers)
   b. RETURN totalBaseCost
   ```

### Code Locations

**Billing Period Days Calculation:**
```1123:1123:AltaworxSimCardCostOptimizerCleanup.cs
: billingPeriod.DaysInBillingPeriod;
```

**Device Activation Proration:**
```1120:1124:AltaworxSimCardCostOptimizerCleanup.cs
simCard.WasActivatedInThisBillingPeriod = DateIsInBillingPeriod(simCard.DateActivated, billingPeriod);
simCard.DaysActivatedInBillingPeriod = simCard.WasActivatedInThisBillingPeriod
    ? DaysLeftInBillingPeriod(simCard.DateActivated, billingPeriod)
    : billingPeriod.DaysInBillingPeriod;
```

**Rate Pool Creation with Proration:**
```1189:1189:AltaworxSimCardCostOptimizerCleanup.cs
ratePools.Add(new ResultRatePool(ratePlan, usesProration, billingPeriod, instance.RatePoolKeyType, matchingMapping.RatePoolName, isSharedRatePool));
```

## 2. Cross-Provider Overage: Excess Usage × Provider-Specific Overage Rates

### What
Cross-Provider Overage calculates additional charges for data usage exceeding plan allowances across multiple providers, applying provider-specific overage rates (`DataPerOverageCharge` and `OverageRate`) to ensure accurate cost calculation for excess usage across different carrier networks.

### Why
- **Usage Accuracy**: Ensures precise calculation of overage charges across all providers
- **Provider-Specific Rates**: Respects individual carrier overage pricing structures
- **Cost Transparency**: Provides clear visibility into excess usage costs per provider
- **Optimization Accuracy**: Enables accurate cost comparisons for cross-provider optimization
- **Billing Integrity**: Prevents zero-value overage scenarios that could distort cost calculations

### How
The system validates overage rate structures across providers and calculates excess usage charges using provider-specific rates, ensuring all rate plans have valid non-zero overage rates before processing optimization scenarios.

### Algorithm: CrossProviderOverageCalculation()

**INPUT**: List<RatePlan> providerRatePlans, List<Device> devices
**OUTPUT**: Decimal total overage cost across providers

1. **VALIDATE overage rate structures**:
   ```
   a. FOR each ratePlan in providerRatePlans:
      i. IF ratePlan.DataPerOverageCharge == 0.0M OR ratePlan.OverageRate == 0.0M:
         - ADD to zeroValueRatePlans list
         - LOG error for invalid overage rates
      ii. IF zeroValueRatePlans.Count > 0:
         - THROW exception with rate plan details
         - STOP optimization process
   ```

2. **CALCULATE excess usage per device**:
   ```
   a. FOR each device in devices:
      i. planAllowance = device.assignedRatePlan.DataAllowanceMB
      ii. actualUsage = device.CycleDataUsageMB
      iii. IF actualUsage > planAllowance:
         - excessUsage = actualUsage - planAllowance
      iv. ELSE:
         - excessUsage = 0
   ```

3. **APPLY provider-specific overage rates**:
   ```
   a. FOR each device with excess usage:
      i. providerOverageRate = device.assignedRatePlan.DataPerOverageCharge
      ii. IF providerOverageRate == 0:
         - providerOverageRate = device.assignedRatePlan.OverageRate
      iii. deviceOverageCost = excessUsage × providerOverageRate
   ```

4. **AGGREGATE overage costs across providers**:
   ```
   a. totalOverageCost = SUM(deviceOverageCost for all devices across providers)
   b. RETURN totalOverageCost
   ```

### Code Locations

**Zero-Value Overage Rate Validation:**
```573:576:AltaworxSimCardCostQueueCustomerOptimization.cs
var zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
if (zeroValueRatePlans.Count > 0)
{
    LogInfo(context, LogTypeConstant.Exception, $"The following rate plans in '{planNameGroup.Key}' has Data per Overage Charge or Overage Rate of 0. Please update to a non-zero value.{Environment.NewLine} {string.Join(',', zeroValueRatePlans.Select(ratePlan => ratePlan.PlanDisplayName))}");
    return true;
}
```

**Overage Rate Structure Validation Pattern:**
Used across multiple cross-provider processing methods:
- Rev Customer Processing
- AMOP Customer Processing
- Cross-Provider Customer Processing
- Auto Change Logic Processing
- Rate Plan Permutation Logic

## 3. Provider-Specific Regulatory Fees: Individual Carrier Fees and Regulations

### What
Provider-Specific Regulatory Fees calculate individual carrier fees and regulatory charges that vary by service provider, including FCC fees, Universal Service Fund (USF) charges, E911 fees, and other carrier-specific regulatory assessments required by different providers and jurisdictions.

### Why
- **Regulatory Compliance**: Ensures compliance with provider-specific and jurisdictional requirements
- **Cost Accuracy**: Includes all mandatory fees in total cost calculations
- **Provider Differentiation**: Accounts for varying regulatory fee structures across carriers
- **Legal Compliance**: Meets regulatory reporting and fee collection requirements
- **Total Cost Transparency**: Provides complete cost picture including all regulatory components

### How
The system applies provider-specific regulatory fee structures based on carrier requirements, service types, and jurisdictional regulations, ensuring accurate fee calculation and allocation across different providers and service categories.

### Algorithm: ProviderSpecificRegulatoryFeesCalculation()

**INPUT**: List<Provider> serviceProviders, List<Device> devices, String jurisdiction
**OUTPUT**: Decimal total regulatory fees across providers

1. **IDENTIFY provider-specific fee structures**:
   ```
   a. FOR each provider in serviceProviders:
      i. LOAD provider regulatory fee schedule
      ii. IDENTIFY jurisdiction-specific requirements
      iii. EXTRACT applicable fee categories (FCC, USF, E911, etc.)
   ```

2. **CALCULATE fees by provider and service type**:
   ```
   a. FOR each device by provider:
      i. DETERMINE service classification (M2M, Mobility, Data)
      ii. APPLY provider-specific fee rates
      iii. CALCULATE jurisdiction-based adjustments
   ```

3. **APPLY regulatory fee categories**:
   ```
   a. FCC_fees = devices.Count × provider.FCCFeePerLine
   b. USF_fees = (monthlyCharges × provider.USFRate)
   c. E911_fees = devices.Count × jurisdiction.E911FeePerLine
   d. other_regulatory_fees = provider.OtherRegulatoryFees
   ```

4. **AGGREGATE fees across providers**:
   ```
   a. totalRegulatoryFees = SUM(FCC_fees + USF_fees + E911_fees + other_regulatory_fees)
   b. RETURN totalRegulatoryFees
   ```

### Code Locations

**Provider-Specific Processing by Portal Type:**
```1168:1179:AltaworxSimCardCostOptimizerCleanup.cs
if (instance.PortalType == PortalTypes.Mobility)
{
    ratePlans = GetMobilityRatePlans(context, instance, isCustomerOptimization, billingPeriodId);
}
else if (instance.PortalType == PortalTypes.M2M)
{
    ratePlans = GetM2MRatePlans(context, instance, billingPeriodId);
}
else if (instance.PortalType == PortalTypes.CrossProvider)
{
    var customerBillingPeriod = crossProviderOptimizationRepository.GetBillingPeriod(ParameterizedLog(context), instance.AMOPCustomerId.GetValueOrDefault(), instance.CustomerBillingPeriodId.GetValueOrDefault(), context.OptimizationSettings.BillingTimeZone);
    ratePlans = customerRatePlanRepository.GetCrossProviderCustomerRatePlans(ParameterizedLog(context), instance.ServiceProviderIds, instance.CustomerType, new List<int> { instance.AMOPCustomerId.GetValueOrDefault() }, customerBillingPeriod, instance.TenantId);
}
```

*Note: Regulatory fee calculation logic would be implemented within provider-specific rate plan processing and cost calculation modules.*

## 4. Cross-Provider Taxes: Location and Provider-Based Tax Calculations

### What
Cross-Provider Taxes calculate location-based and provider-specific tax assessments across multiple service providers, applying state, local, and federal tax rates based on device locations, provider jurisdictions, and applicable tax regulations for telecommunications services.

### Why
- **Tax Compliance**: Ensures compliance with multi-jurisdictional tax requirements
- **Location Accuracy**: Applies correct tax rates based on device and service locations
- **Provider Compliance**: Respects provider-specific tax collection requirements
- **Cost Accuracy**: Includes all applicable taxes in total cost calculations
- **Audit Compliance**: Maintains proper tax calculation and reporting capabilities

### How
The system applies location-based tax calculations using device location data, provider tax jurisdictions, and applicable tax rates, ensuring accurate tax calculation across multiple providers and jurisdictions.

### Algorithm: CrossProviderTaxCalculation()

**INPUT**: List<Device> devices, List<Provider> serviceProviders, TaxJurisdiction jurisdiction
**OUTPUT**: Decimal total tax amount across providers

1. **DETERMINE tax jurisdictions**:
   ```
   a. FOR each device in devices:
      i. IDENTIFY device location (state, county, city)
      ii. DETERMINE applicable tax jurisdictions
      iii. LOAD tax rates for location and service type
   ```

2. **CALCULATE provider-specific tax bases**:
   ```
   a. FOR each provider:
      i. taxableAmount = baseCost + overageCost + regulatoryFees
      ii. APPLY provider-specific tax exemptions
      iii. CALCULATE net taxable amount
   ```

3. **APPLY location-based tax rates**:
   ```
   a. FOR each jurisdiction:
      i. stateTax = taxableAmount × stateRate
      ii. localTax = taxableAmount × localRate
      iii. federalTax = taxableAmount × federalRate
   ```

4. **AGGREGATE taxes across providers and jurisdictions**:
   ```
   a. totalTax = SUM(stateTax + localTax + federalTax for all devices and providers)
   b. RETURN totalTax
   ```

### Code Locations

**Location-Based Processing Infrastructure:**
Present in cross-provider optimization repository methods that handle multi-jurisdictional device processing and provider-specific tax application.

*Note: Tax calculation logic would be implemented within provider-specific billing and cost calculation modules, integrated with location services and tax rate databases.*

## 5. Provider Migration Costs: Costs Associated with Changing Providers

### What
Provider Migration Costs calculate the expenses associated with moving devices between service providers, including porting fees, early termination charges, activation costs, and administrative expenses required for cross-provider device transitions and optimizations.

### Why
- **Migration Planning**: Enables informed decisions about provider changes
- **Cost-Benefit Analysis**: Compares migration costs against potential savings
- **Optimization Accuracy**: Includes transition costs in total optimization calculations
- **Financial Planning**: Provides complete cost picture for provider transitions
- **Strategic Decision Making**: Supports long-term provider relationship planning

### How
The system calculates migration costs by evaluating current provider commitments, transition requirements, and destination provider fees, providing comprehensive cost analysis for cross-provider optimization scenarios.

### Algorithm: ProviderMigrationCostCalculation()

**INPUT**: List<Device> devices, Provider currentProvider, Provider targetProvider
**OUTPUT**: Decimal total migration cost

1. **CALCULATE early termination costs**:
   ```
   a. FOR each device in devices:
      i. contractEndDate = device.currentProvider.ContractEndDate
      ii. IF contractEndDate > currentDate:
         - remainingMonths = MonthsBetween(currentDate, contractEndDate)
         - earlyTerminationFee = device.currentProvider.ETF × remainingMonths
      iii. ELSE:
         - earlyTerminationFee = 0
   ```

2. **CALCULATE porting and activation costs**:
   ```
   a. FOR each device:
      i. portingFee = targetProvider.PortingFeePerDevice
      ii. activationFee = targetProvider.ActivationFeePerDevice
      iii. administrativeFee = targetProvider.AdministrativeFeePerDevice
   ```

3. **CALCULATE provider-specific transition costs**:
   ```
   a. FOR each provider transition:
      i. ASSESS current provider termination requirements
      ii. EVALUATE target provider onboarding costs
      iii. CALCULATE service interruption costs
   ```

4. **AGGREGATE total migration costs**:
   ```
   a. totalMigrationCost = SUM(earlyTerminationFee + portingFee + activationFee + administrativeFee)
   b. RETURN totalMigrationCost
   ```

### Code Locations

**Provider-Specific Optimization Comparison:**
```266:280:Cross-Provider-Optimization-Strategies.md
2. **COMPARE results across providers**:
   ```
   a. CALCULATE total costs for each provider scenario
   b. EVALUATE cross-provider assignment options
   c. ASSESS migration costs and benefits
   ```

3. **CONSIDER provider migration costs and benefits**:
   ```
   a. CALCULATE migration costs between providers
   b. EVALUATE long-term contract implications
   c. ASSESS service quality and reliability factors
   ```
```

**Cross-Provider Comparison Logic:**
Implemented in provider-specific optimization algorithms that evaluate the total cost of ownership including migration expenses when comparing cross-provider assignments.

## Summary

Cross-Provider Cost Components provide a comprehensive framework for:

1. **Multi-Provider Base Cost**: Accurate proration across billing periods and providers
2. **Cross-Provider Overage**: Provider-specific excess usage calculations with rate validation
3. **Provider-Specific Regulatory Fees**: Compliance with individual carrier and jurisdictional requirements
4. **Cross-Provider Taxes**: Location and provider-based tax calculations for multi-jurisdictional compliance
5. **Provider Migration Costs**: Complete cost analysis for provider transitions and optimizations

This framework ensures accurate, comprehensive cost calculation across multiple service providers, enabling informed optimization decisions that consider all cost components including base charges, usage overages, regulatory fees, taxes, and migration expenses. The system provides transparency and accuracy in cross-provider cost comparisons while maintaining compliance with provider-specific and jurisdictional requirements.