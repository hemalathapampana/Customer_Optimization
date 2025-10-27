# Rate Plan Discovery & Validation - Complete Guide

## Overview

The Rate Plan Discovery & Validation process is a critical component of customer optimization that ensures only valid, applicable rate plans are used for SIM card cost optimization. This process involves four main stages:

1. **Customer-Specific Rate Plan Retrieval**
2. **Rate Plan Filtering by Eligibility**
3. **Rate Plan Grouping by Auto Change Capability**
4. **Rate Plan Validation**

## 1. Customer-Specific Rate Plan Retrieval

### Purpose
Retrieves rate plans that are specifically available to a customer based on their type, service provider, and billing period. This ensures optimization only uses plans the customer can actually access.

### Process Flow
```
Customer Data → Determine Customer Type → Call Appropriate API → Retrieve Rate Plans → Validate Collection
```

### Customer Types & Methods

#### Rev Customers (Regular Revenue Customers)
- **Portal Type**: M2M (Machine-to-Machine)
- **Method Call**: 
  ```csharp
  GetCustomerRatePlans(context, customerId, billingPeriodId, serviceProviderId, tenantId)
  ```
- **Parameters Used**: All standard customer identification parameters

#### AMOP Customers (Automated Mobile Operator Platform)
- **Portal Type**: M2M 
- **Method Call**:
  ```csharp
  GetCustomerRatePlans(context, Guid.Empty, billingPeriodId, serviceProviderId, tenantId, customerType, AMOPCustomerId)
  ```
- **Key Difference**: Uses `Guid.Empty` for customerId and includes AMOPCustomerId

### Validation Steps
1. **Count Check**: Verify `ratePlans.Count > 0`
2. **Empty Collection Handling**: Log "No Comm Groups and/or Rate Plans for this Instance"
3. **Bill-in-Advance Assessment**: Count plans where `IsBillInAdvanceEligible == true`

### Example Code Flow
```csharp
// Step 1: Determine customer type and retrieve plans
if (CustomerType == Rev && PortalType == M2M) {
    var ratePlans = GetCustomerRatePlans(context, customerId, billingPeriodId, serviceProviderId, tenantId);
}

// Step 2: Validate collection
if (ratePlans.Count == 0) {
    LogError("No rate plans found for customer");
    return;
}

// Step 3: Check bill-in-advance eligibility
var useBillInAdvance = ratePlans.Count(x => x.IsBillInAdvanceEligible) > 0;
```

---

## 2. Rate Plan Filtering by Customer Eligibility

### Purpose
Filters the retrieved rate plans to ensure only valid and compatible plans are used for optimization. This prevents invalid assignments and ensures service provider constraints are met.

### Filter Layers

#### Layer 1: Customer Rate Plan Code Filtering
- **Purpose**: Only process devices that have assigned rate plan codes
- **Logic**: Filter devices where `CustomerRatePlanCode` is not null or empty
- **Code**: 
  ```csharp
  optimizationSimCards = optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList();
  ```

#### Layer 2: Service Provider Compatibility (CrossProvider Only)
- **When Applied**: Only for CrossProvider scenarios with multiple service providers
- **Process**:
  1. Parse `serviceProviderIds` string into a list
  2. Filter rate plans where `ServiceProviderIds` contains all required providers
  3. If no valid plans found, log error and exit
- **Code**:
  ```csharp
  var serviceProviderIdList = serviceProviderIds.Replace(" ", "").Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList();
  autoChangeRatePlans = autoChangeRatePlans.Where(x => 
      x.ServiceProviderIds.Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList()
      .ContainsAllItems(serviceProviderIdList)).ToList();
  ```

#### Layer 3: Rate Plan Code Matching
- **Purpose**: Only use rate plans that match device assignments
- **Process**:
  1. Extract distinct rate plan codes from devices
  2. Filter rate plans to only include those with matching `PlanName`
- **Code**:
  ```csharp
  var ratePlanCodes = simCardsByRatePoolId.Select(x => x.CustomerRatePlanCode).Distinct();
  var ratePlansForPool = ratePlans.Where(x => ratePlanCodes.Contains(x.PlanName));
  ```

### Filtering Flow Diagram
```
All Rate Plans
    ↓
[Filter 1] Customer Rate Plan Codes
    ↓
Rate Plans with Valid Codes
    ↓
[Filter 2] Service Provider Compatibility (if CrossProvider)
    ↓
Service Provider Compatible Plans
    ↓
[Filter 3] Device Rate Plan Code Matching
    ↓
Final Filtered Rate Plans
```

---

## 3. Rate Plan Grouping by Auto Change Capability

### Purpose
Separates rate plans based on their Auto Change capability, which determines the optimization strategy used.

### Two Optimization Strategies

#### Strategy 1: Non-Auto-Change Rate Plans (Customer Rate Pool)
- **Auto Change Setting**: `AutoChangeRatePlan = false`
- **Optimization Method**: Pooled optimization by customer rate pool
- **Characteristics**: Fixed rate plan assignments within customer pools
- **Processing Method**: `ProcessDevicesWithAutoChangeDisabledRatePlans()`

#### Strategy 2: Auto-Change Rate Plans (Algorithmic Optimization)
- **Auto Change Setting**: `AutoChangeRatePlan = true`
- **Optimization Method**: Permutation-based optimization
- **Characteristics**: Dynamic rate plan switching allowed
- **Processing Method**: `ProcessPlanNameGroup()`

### Grouping Process

#### Step 1: Initial Separation
```csharp
var ratePlansByCustomerRatePool = ratePlans.Where(ratePlan => !ratePlan.AutoChangeRatePlan).ToList();
var autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan).ToList();
```

#### Step 2: Further Sub-Grouping
For auto-change plans:
1. **Group by PlanName**: Organize similar rate plan families
2. **Group by AllowsSimPooling**: Separate based on SIM pooling capability

```csharp
var ratePlansByCodes = autoChangeRatePlans.GroupBy(x => x.PlanName);
foreach (var planNameGroup in ratePlansByCodes) {
    foreach (var poolingGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling)) {
        // Process each pooling group separately
    }
}
```

### Grouping Decision Tree
```
Rate Plans
    ↓
AutoChangeRatePlan?
    ↓                    ↓
   No                   Yes
    ↓                    ↓
Customer Rate Pool → Group by PlanName
Optimization            ↓
                   Group by AllowsSimPooling
                        ↓
                 Algorithmic Optimization
```

---

## 4. Rate Plan Validation

### Purpose
Ensures rate plans have valid values for critical properties required for cost calculations. Prevents division-by-zero errors and incorrect cost computations.

### Validation Criteria

#### Critical Properties
1. **DataPerOverageCharge**: Must be > 0.0M
2. **OverageRate**: Must be > 0.0M

#### Why These Matter
- **DataPerOverageCharge**: Used for calculating data overage costs
- **OverageRate**: Used for billing calculations
- **Zero Values**: Would break optimization algorithms and cause calculation errors

### Validation Process

#### Step 1: Identify Invalid Plans
```csharp
var zeroValueRatePlans = groupRatePlans.FindAll(x => 
    x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
```

#### Step 2: Handle Validation Results
```csharp
if (zeroValueRatePlans.Count > 0) {
    // Log detailed error with plan names
    LogInfo(context, LogTypeConstant.Exception, 
        $"The following rate plans in '{planNameGroup.Key}' has Data per Overage Charge or Overage Rate of 0. " +
        $"Please update to a non-zero value.{Environment.NewLine}" +
        $"{string.Join(',', zeroValueRatePlans.Select(ratePlan => ratePlan.PlanDisplayName))}");
    return true; // Stop processing
}
```

#### Step 3: Continue or Stop
- **If Validation Fails**: Stop optimization process, log errors
- **If Validation Passes**: Continue with optimization

### Validation Flow
```
Rate Plans
    ↓
Check DataPerOverageCharge > 0
    ↓
Check OverageRate > 0
    ↓
Any Zero Values?
    ↓              ↓
   Yes            No
    ↓              ↓
Log Error    Continue Processing
Stop Process
```

---

## Complete Process Flow

### High-Level Overview
```
1. Load Customer Data
    ↓
2. Retrieve Customer-Specific Rate Plans
    ↓
3. Apply Multi-Layer Filtering
    ↓
4. Group by Auto Change Capability
    ↓
5. Validate Rate Plan Values
    ↓
6. Proceed to Optimization (Rate Pool Collection)
```

### Detailed Process Flow
```
Customer Request
    ↓
[1.1] Determine Customer Type (Rev/AMOP)
    ↓
[1.2] Call Appropriate GetCustomerRatePlans Method
    ↓
[1.3] Validate Rate Plan Collection (Count > 0)
    ↓
[1.4] Assess Bill-in-Advance Eligibility
    ↓
[2.1] Filter by Customer Rate Plan Codes
    ↓
[2.2] Filter by Service Provider Compatibility (if CrossProvider)
    ↓
[2.3] Filter by Device Rate Plan Code Matching
    ↓
[3.1] Separate by AutoChangeRatePlan Property
    ↓
[3.2] Group Auto-Change Plans by PlanName
    ↓
[3.3] Sub-Group by AllowsSimPooling
    ↓
[4.1] Validate DataPerOverageCharge > 0
    ↓
[4.2] Validate OverageRate > 0
    ↓
[4.3] Handle Validation Results
    ↓
Ready for Rate Pool Collection & Optimization
```

## Key Takeaways

### Critical Success Factors
1. **Correct Customer Type Identification**: Determines which API method to call
2. **Thorough Filtering**: Ensures only valid, applicable rate plans are used
3. **Proper Grouping**: Enables appropriate optimization strategy selection
4. **Rigorous Validation**: Prevents calculation errors and invalid assignments

### Common Issues & Solutions
| Issue | Solution |
|-------|----------|
| No rate plans retrieved | Check customer type, billing period, service provider parameters |
| Zero-value rate plans | Update rate plan configuration with non-zero values |
| CrossProvider filtering fails | Verify service provider ID compatibility |
| Auto-change grouping issues | Check AutoChangeRatePlan property settings |

### Next Steps
After successful Rate Plan Discovery & Validation, the process continues to:
1. **Rate Pool Collection Creation**
2. **Permutation Sequence Generation**
3. **Optimization Algorithm Execution**

This foundation ensures that all subsequent optimization processes work with valid, properly categorized rate plans that are appropriate for the specific customer context.