# Customer Filtering Algorithm: Filters devices by customer rate plan codes

## Overview
**What**: Customer Filtering removes SIM devices that lack valid customer rate plan code assignments before rate plan optimization processing.

**Why**: Devices without customer rate plan codes cannot be properly matched to eligible rate plans and would cause processing errors during optimization.

**How**: The system filters the device collection using LINQ to exclude devices where `CustomerRatePlanCode` is null, empty, or whitespace.

## Algorithmic Representation

### Main Algorithm Flow
```
ALGORITHM: FilterDevicesByCustomerRatePlanCodes
INPUT: 
    - devices: Collection<OptimizationSimCard>
    - customerType: SiteTypes (Rev, AMOP, CrossProvider)
    - context: ExecutionContext

STEP 1: Retrieve Customer Devices
    devices ← GetOptimizationSimCards(context, serviceProvider, customer, billingPeriod, tenant)

STEP 2: Apply Rate Plan Code Filter
    IF customerType ∈ {SiteTypes.Rev, SiteTypes.AMOP} THEN
        validDevices ← devices.Where(d → !IsNullOrWhiteSpace(d.CustomerRatePlanCode))
    
    IF customerType = SiteTypes.CrossProvider THEN
        validDevices ← devices.Where(d → !IsNullOrWhiteSpace(d.CustomerRatePlanCode))

STEP 3: Identify Unassigned Devices
    unassignedDevices ← devices.Where(d → IsNullOrWhiteSpace(d.CustomerRatePlanCode))
    
STEP 4: Process Valid Devices
    FOR EACH ratePoolGroup IN validDevices.GroupBy(d → d.CustomerRatePoolId) DO
        ratePlanCodes ← ratePoolGroup.Select(d → d.CustomerRatePlanCode).Distinct()
        eligibleRatePlans ← ratePlans.Where(rp → ratePlanCodes.Contains(rp.PlanName))
        ProcessRatePoolGroup(ratePoolGroup, eligibleRatePlans)
    END FOR

STEP 5: Handle Unassigned Devices
    IF unassignedDevices.Count > 0 THEN
        ProcessNoRatePlanDevices(unassignedDevices)
    END IF

OUTPUT: Collection of devices with valid customer rate plan codes grouped by rate pools
```

### Detailed Sub-Algorithms

#### Sub-Algorithm 1: Device Retrieval by Customer Type
```
ALGORITHM: GetDevicesByCustomerType
INPUT: customerType, customerData, billingPeriod, context

CASE customerType OF:
    SiteTypes.Rev:
        devices ← GetOptimizationSimCards(
            context: context,
            serviceProviderId: billingPeriod.ServiceProviderId,
            revAccountNumber: customer.RevAccountNumber,
            integrationAuthId: customer.IntegrationAuthenticationId,
            billingPeriodId: billingPeriod.Id,
            tenantId: tenantId,
            customerType: SiteTypes.Rev,
            amopCustomerId: null
        )
    
    SiteTypes.AMOP:
        devices ← GetOptimizationSimCards(
            context: context,
            serviceProviderId: billingPeriod.ServiceProviderId,
            revAccountNumber: null,
            integrationAuthId: null,
            billingPeriodId: billingPeriod.Id,
            tenantId: tenantId,
            customerType: SiteTypes.AMOP,
            amopCustomerId: customer.AMOPCustomerId
        )
    
    SiteTypes.CrossProvider:
        devices ← GetCrossProviderCustomerSimCards(
            context: context,
            customerType: customer.CustomerType,
            customerId: customer.CustomerId,
            revAccountNumber: customer.RevAccountNumber,
            integrationAuthId: customer.IntegrationAuthenticationId,
            billingPeriod: billingPeriod,
            serviceProviderIds: customer.ServiceProviderIds
        )

RETURN devices
```

#### Sub-Algorithm 2: Rate Plan Code Validation
```
ALGORITHM: ValidateRatePlanCodes
INPUT: device

VALIDATION_RULE: CustomerRatePlanCodeExists
    RETURN NOT (device.CustomerRatePlanCode IS NULL OR 
                device.CustomerRatePlanCode = "" OR 
                device.CustomerRatePlanCode = WHITESPACE_ONLY)

RETURN ValidationResult
```

#### Sub-Algorithm 3: Unassigned Device Processing
```
ALGORITHM: ProcessNoRatePlanDevices
INPUT: unassignedDevices, context, billingPeriod

STEP 1: Create Communication Plan Group
    commPlanGroupId ← CreateCommPlanGroup(context, instanceId)

STEP 2: Create Processing Queue
    queueId ← CreateQueue(context, instanceId, commPlanGroupId, null, usesProration)

STEP 3: Start Queue Processing
    StartQueue(context, queueId, "")

STEP 4: Project Data Usage
    processedDevices ← ProjectDataUsageAndSaveDevices(context, instanceId, unassignedDevices, billingPeriod, false)

STEP 5: Record Results
    RecordRatePool(context, connectionString, queueId, billingPeriodId, processedDevices)
    RecordTotalCost(context, connectionString, queueId, DEFAULT_UNASSIGNED_TOTAL_COST)

STEP 6: Complete Processing
    StopQueue(context, queueId)

RETURN ProcessingResult.Success
```

## Code Locations

### Primary Implementation Files

#### File: `AltaworxSimCardCostQueueCustomerOptimization.cs`

**Main Device Filtering Logic:**
```
Lines 509-520: ProcessDevicesByCustomerRatePlans method
    - Device retrieval and initial filtering
    - Customer rate plan code validation

Line 511: GetOptimizationSimCards call
    var optimizationSimCards = GetOptimizationSimCards(context, null, billingPeriod.ServiceProviderId, 
        revAccountNumber, integrationAuthenticationId, billingPeriod.Id, tenantId, customerType, AMOPCustomerId);

Lines 512-515: Rate plan code filtering
    if (revAccountNumber != null || AMOPCustomerId != null)
    {
        optimizationSimCards = optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList();
    }
```

**Cross-Provider Customer Filtering:**
```
Lines 784-788: ProcessCrossProviderDevicesByCustomerRatePlans method
    - Cross-provider device retrieval and filtering

Line 786: Cross-provider device retrieval
    var optimizationSimCards = crossProviderOptimizationRepository.GetCrossProviderCustomerSimCards(
        ParameterizedLog(context), customer.CustomerType, customer.CustomerId, 
        customer.RevAccountNumber, customer.IntegrationAuthenticationId, billingPeriod, serviceProviderIds);

Line 788: Cross-provider rate plan code filtering
    optimizationSimCards = optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList();
```

**Unassigned Device Processing:**
```
Lines 661-680: ProcessNoRatePlanDevices method
    - Handles devices without customer rate plan codes

Lines 663-666: Identify unassigned devices
    var unusedOptimizationSimCards = GetOptimizationSimCards(context, null, serviceProviderId, 
        revAccountNumber, integrationAuthenticationId, billingPeriod.Id, tenantId, customerType, AMOPCustomerId);
    var noRatePlanCodes = unusedOptimizationSimCards
        .Where(c => string.IsNullOrWhiteSpace(c.CustomerRatePlanCode))
        .ToList();

Lines 668-680: Process unassigned devices
    - Create communication plan group
    - Create processing queue
    - Record default costs for unassigned devices
```

**Cross-Provider Unassigned Device Processing:**
```
Lines 852-872: ProcessNoRatePlanCrossProviderDevices method
    - Handles unassigned devices for cross-provider customers

Lines 854-857: Cross-provider unassigned device identification
    var unusedOptimizationSimCards = crossProviderOptimizationRepository.GetCrossProviderCustomerSimCards(...);
    var noRatePlanCodes = unusedOptimizationSimCards
        .Where(c => string.IsNullOrWhiteSpace(c.CustomerRatePlanCode))
        .ToList();
```

**Device Grouping and Rate Pool Processing:**
```
Lines 528-556: Device grouping by rate pool ID
    var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();

Lines 537-543: Rate plan code matching
    var ratePlanCodes = simCardsByRatePoolId.Select(x => x.CustomerRatePlanCode).Distinct();
    var ratePlansForPool = ratePlans.Where(x => ratePlanCodes.Contains(x.PlanName));
```

### Supporting Code Patterns

#### LINQ Filtering Pattern
```csharp
// Standard filtering pattern used throughout
devices.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList()

// Inverse filtering for unassigned devices
devices.Where(c => string.IsNullOrWhiteSpace(c.CustomerRatePlanCode)).ToList()
```

#### Customer Type Switch Pattern
```csharp
// Customer type-based processing (Lines 509-520)
CASE customer.Type OF:
    SiteTypes.Rev → Process with revAccountNumber and integrationAuthenticationId
    SiteTypes.AMOP → Process with AMOPCustomerId
    SiteTypes.CrossProvider → Use GetCrossProviderCustomerSimCards
```

#### Device Grouping Pattern
```csharp
// Rate pool grouping (Line 528)
var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct()

// Rate plan code extraction (Line 537)
var ratePlanCodes = simCardsByRatePoolId.Select(x => x.CustomerRatePlanCode).Distinct()
```

## Filter Execution Flow

```
Message Processing → Customer Type Detection → Device Retrieval → Rate Plan Code Filtering → Device Grouping → Rate Pool Processing
       ↓                      ↓                      ↓                        ↓                      ↓                ↓
Line 88-144 →          Line 102 →           Lines 511,786 →         Lines 512-515,788 →    Line 528 →     Lines 542-556
```

## Error Handling Locations

```
Invalid Customer Type: Lines 90-92
No Rate Plan Codes Found: Lines 668-670 (logged as INFO)
Cross-Provider Validation: Lines 810-815
Empty Device Collections: Lines 583-586
```

## Performance Considerations

**Filtering Optimizations:**
- Line 512-515: Conditional filtering only when customer identifiers present
- Line 528: Single GroupBy operation for rate pool organization  
- Line 537: Distinct operation on rate plan codes to minimize database queries
- Lines 668-680: Separate processing pipeline for unassigned devices to avoid main algorithm overhead