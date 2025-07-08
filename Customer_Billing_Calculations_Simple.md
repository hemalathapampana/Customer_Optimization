# Customer Billing Calculations - Simple Algorithms

## Overview
Four simple formulas calculate customer billing charges for telecommunications services.

---

## 1. Customer Base Cost

**Formula**: `Monthly plan cost × (customer billing days / 30)`

### Algorithm
```
INPUT: monthlyPlanCost, customerBillingDays

STEP 1: Calculate proration factor
    prorationFactor = customerBillingDays ÷ 30

STEP 2: Apply proration
    baseCost = monthlyPlanCost × prorationFactor

STEP 3: Round result
    baseCost = ROUND(baseCost, 2)

OUTPUT: baseCost
```

### Example
- Monthly plan cost: $50.00
- Customer billing days: 15 days
- Calculation: $50.00 × (15 ÷ 30) = $25.00

---

## 2. Customer Overage

**Formula**: `Excess customer usage × customer overage rate`

### Algorithm
```
INPUT: customerUsageMB, planAllowanceMB, overageRate

STEP 1: Calculate excess usage
    IF customerUsageMB > planAllowanceMB THEN:
        excessUsage = customerUsageMB - planAllowanceMB
    ELSE:
        excessUsage = 0
    END IF

STEP 2: Calculate overage charge
    overageCharge = excessUsage × overageRate

STEP 3: Round result
    overageCharge = ROUND(overageCharge, 2)

OUTPUT: overageCharge
```

### Example
- Customer usage: 1200 MB
- Plan allowance: 1000 MB
- Overage rate: $0.05 per MB
- Calculation: (1200 - 1000) × $0.05 = $10.00

---

## 3. Customer Regulatory Fees

**Formula**: `Sum of applicable government-mandated fees`

### Algorithm
```
INPUT: customerLocation, serviceType, baseCharges

STEP 1: Identify applicable fees
    applicableFees = GetRegulatoryFees(customerLocation, serviceType)

STEP 2: Calculate total fees
    totalFees = 0
    FOR EACH fee IN applicableFees:
        IF fee.type = "FLAT_RATE" THEN:
            feeAmount = fee.rate
        ELSE IF fee.type = "PERCENTAGE" THEN:
            feeAmount = baseCharges × (fee.rate ÷ 100)
        END IF
        totalFees = totalFees + feeAmount
    END FOR

STEP 3: Round result
    totalFees = ROUND(totalFees, 2)

OUTPUT: totalFees
```

### Example
- Base charges: $35.00
- Federal USF fee: 3.5% = $35.00 × 0.035 = $1.23
- State regulatory fee: $0.50 flat
- Total regulatory fees: $1.23 + $0.50 = $1.73

---

## 4. Customer Taxes

**Formula**: `Taxable charges × applicable tax rates`

### Algorithm
```
INPUT: baseCost, overageCharge, regulatoryFees, customerLocation

STEP 1: Calculate taxable base
    taxableCharges = baseCost + overageCharge
    IF location.taxesRegulatoryFees THEN:
        taxableCharges = taxableCharges + regulatoryFees
    END IF

STEP 2: Apply tax rates
    totalTax = 0
    taxRates = GetTaxRates(customerLocation)
    FOR EACH rate IN taxRates:
        taxAmount = taxableCharges × (rate ÷ 100)
        totalTax = totalTax + taxAmount
    END FOR

STEP 3: Round result
    totalTax = ROUND(totalTax, 2)

OUTPUT: totalTax
```

### Example
- Taxable charges: $35.00
- State sales tax: 6.5% = $35.00 × 0.065 = $2.28
- Local tax: 2.0% = $35.00 × 0.020 = $0.70
- Total taxes: $2.28 + $0.70 = $2.98

---

## Complete Billing Calculation

### Master Algorithm
```
INPUT: customer, ratePlan, usage, billingDays

STEP 1: Calculate base cost
    baseCost = ratePlan.monthlyCost × (billingDays ÷ 30)

STEP 2: Calculate overage
    IF usage > ratePlan.allowance THEN:
        overage = (usage - ratePlan.allowance) × ratePlan.overageRate
    ELSE:
        overage = 0
    END IF

STEP 3: Calculate regulatory fees
    regulatoryFees = SUM of applicable government fees

STEP 4: Calculate taxes
    taxes = (baseCost + overage) × applicable tax rates

STEP 5: Calculate total bill
    totalBill = baseCost + overage + regulatoryFees + taxes

OUTPUT: totalBill
```

### Complete Example
**Customer Plan**: $50/month, 1000 MB allowance, $0.05/MB overage
**Usage**: 1200 MB for 15 days

```
Step 1: Base Cost = $50 × (15 ÷ 30) = $25.00
Step 2: Overage = (1200 - 1000) × $0.05 = $10.00  
Step 3: Regulatory Fees = $1.73 (from example above)
Step 4: Taxes = ($25.00 + $10.00) × 8.5% = $2.98
Step 5: Total Bill = $25.00 + $10.00 + $1.73 + $2.98 = $39.71
```

---

## Key Points

- **Proration**: Always divide billing days by 30 for monthly plans
- **Overage**: Only charge for usage above plan allowance  
- **Validation**: Ensure overage rates and data charges > 0
- **Rounding**: Round all monetary values to 2 decimal places
- **Location**: Customer service address determines fees and taxes