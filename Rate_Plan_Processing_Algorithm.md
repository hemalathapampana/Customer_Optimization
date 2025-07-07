# Rate Plan Processing Algorithm

## Overview
Rate Plan Processing is a critical component in the Altaworx SIM Card Cost Optimization system that handles customer-specific rate plan retrieval, filtering, grouping, and validation for billing optimization.

## Algorithm Steps

### 1. Rate Plan Retrieval from Billing Period
**Purpose**: Retrieve customer-specific rate plans for a given billing period

```
FUNCTION GetCustomerRatePlans(customerId, billingPeriodId, serviceProviderId, tenantId)
    INPUT: 
        - customerId: Unique customer identifier
        - billingPeriodId: Billing period for which to retrieve rate plans
        - serviceProviderId: Service provider identifier (optional)
        - tenantId: Tenant context
    
    PROCESS:
        1. Query database for rate plans associated with:
           - Customer ID
           - Billing period ID
           - Service provider (if specified)
           - Tenant context
        2. Return List<RatePlan>
    
    OUTPUT: Collection of customer rate plans
```

### 2. Rate Plan Filtering by Customer Eligibility and Service Provider
**Purpose**: Filter rate plans based on customer eligibility criteria and service provider constraints

```
FUNCTION FilterRatePlansByEligibility(ratePlans, customer, serviceProvider)
    INPUT:
        - ratePlans: Collection of retrieved rate plans
        - customer: Customer information with eligibility criteria
        - serviceProvider: Service provider constraints
    
    PROCESS:
        1. Filter by Bill-in-Advance eligibility:
           useBillInAdvance = ratePlans.Count(x => x.IsBillInAdvanceEligible) > 0
        
        2. Filter by service provider compatibility:
           IF cross-provider optimization:
               ratePlans = ratePlans.Where(x => x.ServiceProviderIds matches serviceProviderIds)
        
        3. Filter by customer rate plan codes:
           optimizationSimCards = devices.Where(s => !IsNullOrWhiteSpace(s.CustomerRatePlanCode))
        
        4. Apply tenant-specific filtering based on customer type
    
    OUTPUT: Filtered collection of eligible rate plans
```

### 3. Rate Plan Grouping by Auto Change Rate Plan Capability
**Purpose**: Separate rate plans into groups based on Auto Change Rate Plan capability for different processing paths

```
FUNCTION GroupRatePlansByAutoChange(ratePlans)
    INPUT: ratePlans - Filtered collection of rate plans
    
    PROCESS:
        1. Split rate plans into two groups:
           
           GROUP A: Customer Rate Pool Plans (Static Plans)
           ratePlansByCustomerRatePool = ratePlans.Where(ratePlan => !ratePlan.AutoChangeRatePlan)
           
           GROUP B: Auto Change Rate Plans (Dynamic Plans)
           autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan)
        
        2. For Auto Change Rate Plans, further group by:
           a. Plan Name (PlanName)
           b. SIM Pooling capability (AllowsSimPooling)
           
           ratePlansByCodes = autoChangeRatePlans.GroupBy(x => x.PlanName)
           FOR EACH planNameGroup IN ratePlansByCodes:
               ratePlanGroup = planNameGroup.GroupBy(x => x.AllowsSimPooling)
    
    OUTPUT: 
        - Static rate plan groups for customer rate pool processing
        - Dynamic rate plan groups for auto-change optimization
```

### 4. Rate Plan Validation (Overage Rates and Data Charges)
**Purpose**: Validate that rate plans have non-zero overage rates and data charges

```
FUNCTION ValidateRatePlanCharges(ratePlans)
    INPUT: ratePlans - Collection of rate plans to validate
    
    PROCESS:
        FOR EACH ratePlan IN ratePlans:
            1. Check overage rate validation:
               IF ratePlan.OverageRate <= 0:
                   ADD ratePlan TO zeroValueRatePlans
            
            2. Check data per overage charge validation:
               IF ratePlan.DataPerOverageCharge <= 0:
                   ADD ratePlan TO zeroValueRatePlans
        
        IF zeroValueRatePlans.Count > 0:
            LOG ERROR with rate plan details
            RETURN ValidationResult.Failed
        
        RETURN ValidationResult.Success
    
    OUTPUT: Validation result (Success/Failed with error details)
```

## Complete Rate Plan Processing Algorithm

```
FUNCTION ProcessRatePlans(customerId, billingPeriodId, serviceProviderId, tenantId)
    
    // Step 1: Retrieve customer-specific rate plans
    ratePlans = GetCustomerRatePlans(customerId, billingPeriodId, serviceProviderId, tenantId)
    
    IF ratePlans.Count = 0:
        LOG ERROR "No rate plans found for customer"
        RETURN ProcessingResult.NoRatePlans
    
    // Step 2: Filter by customer eligibility and service provider
    filteredRatePlans = FilterRatePlansByEligibility(ratePlans, customer, serviceProvider)
    
    // Step 3: Group by Auto Change Rate Plan capability
    (staticRatePlans, dynamicRatePlans) = GroupRatePlansByAutoChange(filteredRatePlans)
    
    // Step 4: Process static rate plans (Customer Rate Pool)
    IF staticRatePlans.Count > 0:
        validationResult = ValidateRatePlanCharges(staticRatePlans)
        IF validationResult = Failed:
            RETURN ProcessingResult.ValidationFailed
        
        ProcessDevicesWithAutoChangeDisabledRatePlans(staticRatePlans)
    
    // Step 5: Process dynamic rate plans (Auto Change)
    FOR EACH planNameGroup IN dynamicRatePlans:
        FOR EACH poolingGroup IN planNameGroup.GroupBy(AllowsSimPooling):
            
            // Validate overage rates and data charges
            validationResult = ValidateRatePlanCharges(poolingGroup)
            IF validationResult = Failed:
                CONTINUE to next group
            
            // Check rate plan limits
            IF poolingGroup.Count > MAX_RATE_PLAN_LIMIT (15):
                LOG ERROR "Rate plan count exceeds limit"
                CONTINUE to next group
            
            IF poolingGroup.Count <= MIN_RATE_PLAN_LIMIT (1):
                LOG INFO "Minimum rate plan limit reached"
                CONTINUE to next group
            
            // Process auto-change optimization
            ProcessAutoChangeRatePlanGroup(poolingGroup)
    
    // Step 6: Handle devices with no rate plans
    ProcessNoRatePlanDevices()
    
    RETURN ProcessingResult.Success
```

## Key Business Rules

### Rate Plan Validation Rules
- **Overage Rate**: Must be > 0
- **Data Per Overage Charge**: Must be > 0
- **Rate Plan Count Limits**: 
  - Maximum: 15 rate plans per group
  - Minimum: 2 rate plans for auto-change optimization

### Grouping Rules
- **Static Plans**: `AutoChangeRatePlan = false` → Processed as customer rate pools
- **Dynamic Plans**: `AutoChangeRatePlan = true` → Processed with optimization algorithms
- **SIM Pooling**: Plans grouped by `AllowsSimPooling` capability
- **Plan Name**: Auto-change plans grouped by `PlanName` for optimization

### Filtering Rules
- **Service Provider**: Cross-provider plans must match all required service provider IDs
- **Customer Eligibility**: Plans filtered by customer-specific eligibility criteria
- **Rate Plan Codes**: Devices must have valid customer rate plan codes
- **Bill-in-Advance**: Special handling for eligible rate plans

## Error Handling

### Validation Failures
- **Zero Value Charges**: Stop processing and log error with plan details
- **Exceeded Limits**: Skip group and continue with next group
- **No Valid Plans**: Return appropriate error status

### Processing Errors
- **Database Connectivity**: Handle connection failures gracefully
- **Missing Data**: Process remaining valid data where possible
- **Optimization Failures**: Mark instance as completed with errors

## Performance Considerations

### Optimization Strategies
- **Device Count Thresholds**: Skip optimization for groups with ≤ 1 device
- **Parallel Processing**: Process multiple rate plan groups concurrently
- **Caching**: Use Redis cache for frequently accessed data
- **Queue Management**: Limit concurrent optimization runs via `QueuesPerInstance`

### Scalability Features
- **Permutation Generation**: Generate rate plan sequences for optimization
- **Batch Processing**: Process devices in batches to manage memory usage
- **Background Processing**: Use SQS for asynchronous processing