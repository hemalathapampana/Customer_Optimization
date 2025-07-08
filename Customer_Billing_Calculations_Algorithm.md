# Customer Billing Calculations Algorithm

## Overview
Customer billing calculations in the SIM Card Cost Optimization system involve four primary components that determine the total customer charges for a billing period. Each component uses specific algorithmic approaches to ensure accurate and fair billing.

---

## 1. Customer Base Cost Calculation

### Definition
**Customer Base Cost**: Monthly plan cost × (customer billing days / 30)

### What
Calculates the prorated monthly plan cost based on the actual number of days the customer was active during the billing period.

### Why
- **Fair Billing**: Customers should only pay for the days they actually used the service
- **Activation Proration**: New activations mid-cycle require prorated billing
- **Service Changes**: Plan changes during billing period need accurate cost allocation
- **Customer Satisfaction**: Transparent billing builds customer trust

### How
The system calculates base cost using proration logic that considers device activation dates and billing period boundaries.

### Algorithm

```
ALGORITHM: CalculateCustomerBaseCost
INPUT: 
    - ratePlan: RatePlan with monthly cost
    - deviceActivationDate: DateTime when device was activated
    - billingPeriod: BillingPeriod with start and end dates
    - usesProration: boolean flag for proration calculation

STEP 1: Determine Billing Days
    IF usesProration = true THEN:
        wasActivatedInThisBillingPeriod ← DateIsInBillingPeriod(deviceActivationDate, billingPeriod)
        
        IF wasActivatedInThisBillingPeriod THEN:
            customerBillingDays ← DaysLeftInBillingPeriod(deviceActivationDate, billingPeriod)
        ELSE:
            customerBillingDays ← billingPeriod.DaysInBillingPeriod
        END IF
    ELSE:
        customerBillingDays ← billingPeriod.DaysInBillingPeriod
    END IF

STEP 2: Calculate Monthly Plan Cost
    monthlyPlanCost ← ratePlan.MonthlyRecurringCharge

STEP 3: Apply Proration Formula
    IF usesProration = true THEN:
        prorationFactor ← customerBillingDays / 30.0
        customerBaseCost ← monthlyPlanCost × prorationFactor
    ELSE:
        customerBaseCost ← monthlyPlanCost
    END IF

STEP 4: Validate and Round
    customerBaseCost ← ROUND(customerBaseCost, 2)
    
    IF customerBaseCost < 0 THEN:
        customerBaseCost ← 0
        LogWarning("Negative base cost calculated, set to 0")
    END IF

OUTPUT: customerBaseCost (decimal value representing prorated monthly cost)
```

### Implementation Details

**File: `AltaworxSimCardCostOptimizerCleanup.cs`**

**Base Cost Field Extraction (Lines 1115-1116)**
```csharp
BaseRateAmount = !rdr.IsDBNull("BaseRateAmt") ? rdr.GetDecimal("BaseRateAmt") : 0,
RateChargeAmount = !rdr.IsDBNull("RateChargeAmt") ? rdr.GetDecimal("RateChargeAmt") : 0,
```

**Billing Period Day Calculation (Lines 1120-1123)**
```csharp
simCard.WasActivatedInThisBillingPeriod = DateIsInBillingPeriod(simCard.DateActivated, billingPeriod);
simCard.DaysActivatedInBillingPeriod = simCard.WasActivatedInThisBillingPeriod
    ? DaysLeftInBillingPeriod(simCard.DateActivated, billingPeriod)
    : billingPeriod.DaysInBillingPeriod;
```

**Proration Flag Usage (Lines 591, 683)**
```csharp
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
var originalRatePools = RatePoolFactory.CreateRatePools(ratePlans, billingPeriod, usesProration, OptimizationChargeType.RateChargeAndOverage);
```

---

## 2. Customer Overage Calculation

### Definition
**Customer Overage**: Excess customer usage × customer overage rate

### What
Calculates additional charges when customer usage exceeds the plan's included data allowance.

### Why
- **Usage-Based Billing**: Ensures customers pay for actual consumption
- **Plan Limit Enforcement**: Maintains plan structure integrity
- **Revenue Management**: Captures additional revenue for excess usage
- **Cost Recovery**: Covers carrier charges for overage usage

### How
The system compares actual usage against plan allowances and applies overage rates to excess consumption.

### Algorithm

```
ALGORITHM: CalculateCustomerOverage
INPUT:
    - customerUsageMB: decimal actual data usage in megabytes
    - ratePlan: RatePlan with data allowance and overage rates
    - billingPeriod: BillingPeriod for proration context
    - usesProration: boolean flag for proration calculation

STEP 1: Determine Plan Data Allowance
    planDataAllowanceMB ← ratePlan.DataAllowanceMB
    
    IF usesProration = true THEN:
        // Prorate the allowance based on billing days
        prorationFactor ← customerBillingDays / 30.0
        effectiveAllowanceMB ← planDataAllowanceMB × prorationFactor
    ELSE:
        effectiveAllowanceMB ← planDataAllowanceMB
    END IF

STEP 2: Calculate Excess Usage
    IF customerUsageMB > effectiveAllowanceMB THEN:
        excessUsageMB ← customerUsageMB - effectiveAllowanceMB
    ELSE:
        excessUsageMB ← 0
        RETURN 0  // No overage charges
    END IF

STEP 3: Apply Overage Rate Structure
    customerOverageRate ← ratePlan.OverageRate
    dataPerOverageCharge ← ratePlan.DataPerOverageCharge
    
    // Validate overage rate configuration
    IF customerOverageRate <= 0 OR dataPerOverageCharge <= 0 THEN:
        LogError("Invalid overage rate configuration", ratePlan.Id)
        RETURN 0
    END IF

STEP 4: Calculate Overage Charges
    IF ratePlan.OverageStructure = "PER_MB" THEN:
        customerOverage ← excessUsageMB × customerOverageRate
    ELSE IF ratePlan.OverageStructure = "PER_BLOCK" THEN:
        overageBlocks ← CEILING(excessUsageMB / dataPerOverageCharge)
        customerOverage ← overageBlocks × customerOverageRate
    ELSE IF ratePlan.OverageStructure = "TIERED" THEN:
        customerOverage ← CalculateTieredOverage(excessUsageMB, ratePlan.TieredRates)
    END IF

STEP 5: Apply Overage Caps and Limits
    IF ratePlan.HasOverageCap THEN:
        maxOverageCharge ← ratePlan.MaxOverageCharge
        customerOverage ← MIN(customerOverage, maxOverageCharge)
    END IF
    
    customerOverage ← ROUND(customerOverage, 2)

OUTPUT: customerOverage (decimal value representing excess usage charges)
```

### Implementation Details

**File: `AltaworxSimCardCostQueueCustomerOptimization.cs`**

**Overage Rate Validation (Lines 573-576)**
```csharp
var zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
if (zeroValueRatePlans.Count > 0)
{
    LogInfo(context, LogTypeConstant.Exception, $"The following rate plans in '{planNameGroup.Key}' has Data per Overage Charge or Overage Rate of 0. Please update to a non-zero value.{Environment.NewLine} {string.Join(',', zeroValueRatePlans.Select(ratePlan => ratePlan.PlanDisplayName))}");
}
```

**File: `AltaworxSimCardCostOptimizerCleanup.cs`**

**Overage Charge Field Extraction (Line 1117)**
```csharp
OverageChargeAmount = !rdr.IsDBNull("OverageChargeAmt") ? rdr.GetDecimal("OverageChargeAmt") : 0,
```

**Database Query for Overage Charges (Lines 857, 915, 963, 1022)**
```sql
SELECT [SmsChargeAmount], deviceResult.[BaseRateAmt], deviceResult.[RateChargeAmt], deviceResult.[OverageChargeAmt]
FROM OptimizationDeviceResult deviceResult
```

---

## 3. Customer Regulatory Fees Calculation

### Definition
**Customer Regulatory Fees**: Customer-specific carrier fees mandated by regulatory authorities

### What
Calculates government-mandated fees that carriers must collect from customers for regulatory compliance.

### Why
- **Regulatory Compliance**: Ensures adherence to government fee requirements
- **Carrier Cost Recovery**: Allows carriers to recover regulatory expenses
- **Transparency**: Separates regulatory fees from service charges
- **Geographic Accuracy**: Applies location-specific regulatory requirements

### How
The system applies regulatory fees based on customer location, service type, and applicable regulatory frameworks.

### Algorithm

```
ALGORITHM: CalculateCustomerRegulatoryFees
INPUT:
    - customer: Customer with location and service details
    - ratePlan: RatePlan with associated service types
    - billingPeriod: BillingPeriod for fee calculation context
    - regulatoryFramework: Applicable regulatory rules and rates

STEP 1: Determine Customer Location Context
    customerState ← customer.ServiceAddress.State
    customerCounty ← customer.ServiceAddress.County
    customerZipCode ← customer.ServiceAddress.ZipCode
    serviceType ← ratePlan.ServiceType  // e.g., "M2M", "Mobility", "Data"

STEP 2: Identify Applicable Regulatory Fees
    applicableFees ← []
    
    // Federal regulatory fees
    federalFees ← GetFederalRegulatoryFees(serviceType, billingPeriod)
    applicableFees.AddRange(federalFees)
    
    // State-specific regulatory fees
    stateFees ← GetStateRegulatoryFees(customerState, serviceType, billingPeriod)
    applicableFees.AddRange(stateFees)
    
    // Local regulatory fees (county/municipal)
    localFees ← GetLocalRegulatoryFees(customerCounty, customerZipCode, serviceType, billingPeriod)
    applicableFees.AddRange(localFees)

STEP 3: Calculate Fee Amounts
    totalRegulatoryFees ← 0
    
    FOR EACH fee IN applicableFees:
        CASE fee.CalculationMethod OF:
            "FLAT_RATE":
                feeAmount ← fee.FlatRate
                
            "PERCENTAGE_OF_SERVICE":
                serviceCharges ← customerBaseCost + customerOverage
                feeAmount ← serviceCharges × (fee.PercentageRate / 100)
                
            "PER_LINE":
                feeAmount ← fee.PerLineRate × customer.ActiveLineCount
                
            "USAGE_BASED":
                feeAmount ← customerUsageMB × fee.UsageRate
                
            "TIERED":
                feeAmount ← CalculateTieredRegulatoryFee(customer, fee.TieredStructure)
        END CASE
        
        // Apply fee caps and minimums
        IF fee.HasMinimum THEN:
            feeAmount ← MAX(feeAmount, fee.MinimumFee)
        END IF
        
        IF fee.HasMaximum THEN:
            feeAmount ← MIN(feeAmount, fee.MaximumFee)
        END IF
        
        totalRegulatoryFees ← totalRegulatoryFees + feeAmount
        
        LogFeeApplication(customer.Id, fee.Name, feeAmount, fee.Authority)
    END FOR

STEP 4: Apply Proration for Partial Billing Periods
    IF usesProration = true AND customerBillingDays < 30 THEN:
        prorationFactor ← customerBillingDays / 30.0
        totalRegulatoryFees ← totalRegulatoryFees × prorationFactor
    END IF
    
    totalRegulatoryFees ← ROUND(totalRegulatoryFees, 2)

OUTPUT: totalRegulatoryFees (decimal value representing regulatory compliance fees)
```

### Implementation Framework

**Regulatory Fee Types**
```csharp
public enum RegulatoryFeeType
{
    FederalUniversalServiceFund,    // FCC Universal Service Fund
    StatePublicUtilityFee,          // State-specific utility fees
    Local911Fee,                    // Emergency services fees
    StateRegulatoryFee,             // State telecommunications fees
    FederalExciseTax,               // Federal telecommunications excise tax
    RegulatoryRecoveryFee           // Carrier regulatory cost recovery
}

public enum FeeCalculationMethod
{
    FlatRate,                       // Fixed amount per billing period
    PercentageOfService,            // Percentage of service charges
    PerLine,                        // Fixed amount per active line
    UsageBased,                     // Based on usage volume
    Tiered                          // Tiered rate structure
}
```

**Fee Application Logic**
```csharp
// Example regulatory fee calculation structure
public class RegulatoryFee
{
    public string Authority { get; set; }           // "FCC", "State of CA", etc.
    public FeeCalculationMethod Method { get; set; }
    public decimal Rate { get; set; }
    public bool HasMinimum { get; set; }
    public decimal MinimumFee { get; set; }
    public bool HasMaximum { get; set; }
    public decimal MaximumFee { get; set; }
    public List<string> ApplicableServiceTypes { get; set; }
    public DateTime EffectiveDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
}
```

---

## 4. Customer Taxes Calculation

### Definition
**Customer Taxes**: Customer location-based tax calculations applied to telecommunications services

### What
Calculates applicable sales taxes, use taxes, and telecommunications-specific taxes based on customer service location and local tax jurisdictions.

### Why
- **Tax Compliance**: Ensures compliance with local, state, and federal tax requirements
- **Accurate Tax Collection**: Properly calculates taxes for remittance to authorities
- **Customer Transparency**: Provides clear breakdown of tax components
- **Geographic Precision**: Applies correct tax rates for specific service locations

### How
The system uses customer service addresses to determine applicable tax jurisdictions and rates, then applies appropriate calculation methods.

### Algorithm

```
ALGORITHM: CalculateCustomerTaxes
INPUT:
    - customer: Customer with service address details
    - customerBaseCost: Base service charges
    - customerOverage: Overage charges
    - customerRegulatoryFees: Regulatory fees
    - taxJurisdictions: Available tax jurisdiction data
    - billingPeriod: Billing period context

STEP 1: Determine Tax Jurisdictions
    serviceAddress ← customer.ServiceAddress
    
    // Geocode service address to determine precise tax jurisdictions
    taxLocation ← GeocodeAddress(serviceAddress)
    
    applicableJurisdictions ← []
    applicableJurisdictions.Add(GetFederalTaxJurisdiction())
    applicableJurisdictions.Add(GetStateTaxJurisdiction(taxLocation.State))
    applicableJurisdictions.AddRange(GetLocalTaxJurisdictions(taxLocation))

STEP 2: Calculate Taxable Base
    // Determine what charges are subject to tax
    taxableServiceCharges ← customerBaseCost + customerOverage
    
    // Some jurisdictions tax regulatory fees, others don't
    FOR EACH jurisdiction IN applicableJurisdictions:
        IF jurisdiction.TaxesRegulatoryFees THEN:
            jurisdiction.TaxableBase ← taxableServiceCharges + customerRegulatoryFees
        ELSE:
            jurisdiction.TaxableBase ← taxableServiceCharges
        END IF
    END FOR

STEP 3: Apply Tax Calculations by Jurisdiction
    totalTaxes ← 0
    taxBreakdown ← []
    
    FOR EACH jurisdiction IN applicableJurisdictions:
        jurisdictionTax ← 0
        
        FOR EACH taxType IN jurisdiction.ApplicableTaxTypes:
            CASE taxType.CalculationMethod OF:
                "PERCENTAGE":
                    taxAmount ← jurisdiction.TaxableBase × (taxType.Rate / 100)
                    
                "FLAT_RATE":
                    taxAmount ← taxType.FlatRate
                    
                "PER_LINE":
                    taxAmount ← taxType.PerLineRate × customer.ActiveLineCount
                    
                "TIERED":
                    taxAmount ← CalculateTieredTax(jurisdiction.TaxableBase, taxType.TieredRates)
                    
                "MINIMUM_TAX":
                    calculatedTax ← jurisdiction.TaxableBase × (taxType.Rate / 100)
                    taxAmount ← MAX(calculatedTax, taxType.MinimumTax)
            END CASE
            
            // Apply tax caps if they exist
            IF taxType.HasMaximum THEN:
                taxAmount ← MIN(taxAmount, taxType.MaximumTax)
            END IF
            
            jurisdictionTax ← jurisdictionTax + taxAmount
            
            // Record tax component for transparency
            taxBreakdown.Add(NEW TaxComponent {
                JurisdictionName: jurisdiction.Name,
                TaxType: taxType.Name,
                TaxRate: taxType.Rate,
                TaxableBase: jurisdiction.TaxableBase,
                TaxAmount: taxAmount
            })
        END FOR
        
        totalTaxes ← totalTaxes + jurisdictionTax
    END FOR

STEP 4: Handle Tax Exemptions
    IF customer.HasTaxExemptions THEN:
        FOR EACH exemption IN customer.TaxExemptions:
            IF exemption.IsValid(billingPeriod.EndDate) THEN:
                exemptionAmount ← CalculateExemptionReduction(exemption, taxBreakdown)
                totalTaxes ← totalTaxes - exemptionAmount
                
                LogTaxExemption(customer.Id, exemption.ExemptionNumber, exemptionAmount)
            END IF
        END FOR
    END IF

STEP 5: Apply Proration for Partial Billing Periods
    IF usesProration = true AND customerBillingDays < 30 THEN:
        prorationFactor ← customerBillingDays / 30.0
        totalTaxes ← totalTaxes × prorationFactor
        
        // Update tax breakdown with prorated amounts
        FOR EACH component IN taxBreakdown:
            component.TaxAmount ← component.TaxAmount × prorationFactor
        END FOR
    END IF
    
    totalTaxes ← ROUND(totalTaxes, 2)

OUTPUT: totalTaxes, taxBreakdown (total tax amount and detailed breakdown)
```

### Implementation Framework

**Tax Jurisdiction Structure**
```csharp
public class TaxJurisdiction
{
    public string JurisdictionCode { get; set; }        // e.g., "US-CA-LAX"
    public string JurisdictionName { get; set; }        // e.g., "Los Angeles County"
    public JurisdictionType Type { get; set; }          // Federal, State, County, City
    public List<TaxType> ApplicableTaxTypes { get; set; }
    public bool TaxesRegulatoryFees { get; set; }
    public decimal TaxableBase { get; set; }
}

public enum JurisdictionType
{
    Federal,
    State,
    County,
    City,
    SpecialDistrict
}
```

**Tax Type Configuration**
```csharp
public class TaxType
{
    public string TaxName { get; set; }                 // "Sales Tax", "Use Tax", etc.
    public TaxCalculationMethod CalculationMethod { get; set; }
    public decimal Rate { get; set; }                   // Tax rate (percentage or flat)
    public bool HasMaximum { get; set; }
    public decimal MaximumTax { get; set; }
    public DateTime EffectiveDate { get; set; }
    public DateTime? ExpirationDate { get; set; }
    public List<string> ApplicableServiceTypes { get; set; }
}

public enum TaxCalculationMethod
{
    Percentage,         // Standard percentage-based tax
    FlatRate,          // Fixed tax amount
    PerLine,           // Tax per service line
    Tiered,            // Tiered tax structure
    MinimumTax         // Minimum tax with percentage calculation
}
```

**Tax Exemption Handling**
```csharp
public class TaxExemption
{
    public string ExemptionNumber { get; set; }
    public string ExemptionType { get; set; }           // "Resale", "Government", etc.
    public List<string> ExemptJurisdictions { get; set; }
    public List<string> ExemptTaxTypes { get; set; }
    public DateTime EffectiveDate { get; set; }
    public DateTime ExpirationDate { get; set; }
    public bool IsValid(DateTime checkDate) => 
        checkDate >= EffectiveDate && checkDate <= ExpirationDate;
}
```

---

## Complete Customer Billing Algorithm

### Integrated Billing Calculation
```
ALGORITHM: CalculateCompleteBilling
INPUT: customer, ratePlan, usage, billingPeriod, regulatoryFramework, taxJurisdictions

STEP 1: Calculate Base Cost
    customerBaseCost ← CalculateCustomerBaseCost(ratePlan, customer.ActivationDate, billingPeriod, usesProration)

STEP 2: Calculate Overage Charges
    customerOverage ← CalculateCustomerOverage(customer.UsageMB, ratePlan, billingPeriod, usesProration)

STEP 3: Calculate Regulatory Fees
    customerRegulatoryFees ← CalculateCustomerRegulatoryFees(customer, ratePlan, billingPeriod, regulatoryFramework)

STEP 4: Calculate Taxes
    customerTaxes ← CalculateCustomerTaxes(customer, customerBaseCost, customerOverage, customerRegulatoryFees, taxJurisdictions, billingPeriod)

STEP 5: Calculate Total Bill
    totalCharges ← customerBaseCost + customerOverage + customerRegulatoryFees + customerTaxes
    
STEP 6: Generate Billing Detail
    billingDetail ← {
        CustomerId: customer.Id,
        BillingPeriod: billingPeriod,
        BaseCost: customerBaseCost,
        OverageCharges: customerOverage,
        RegulatoryFees: customerRegulatoryFees,
        Taxes: customerTaxes,
        TotalCharges: totalCharges,
        BillingDays: customerBillingDays,
        UsageCharges: customer.UsageMB
    }

OUTPUT: totalCharges, billingDetail
```

### Key Implementation Points

**Proration Handling**
- Applied to base cost, regulatory fees, and taxes
- Based on actual billing days vs. standard 30-day period
- Accounts for mid-cycle activations and plan changes

**Validation and Error Handling**
- Zero or negative rate validation
- Tax jurisdiction lookup failures
- Regulatory fee configuration errors
- Customer exemption validation

**Compliance and Auditing**
- Detailed logging of all calculation steps
- Tax breakdown for transparency
- Regulatory fee attribution
- Audit trail for billing disputes