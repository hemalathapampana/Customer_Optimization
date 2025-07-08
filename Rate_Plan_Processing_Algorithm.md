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

### 2. Customer Filtering
**Purpose**: Filter customers and devices based on multiple criteria including customer type, authentication, and rate plan assignments

```
FUNCTION FilterCustomers(message, context)
    INPUT:
        - message: SQS message containing customer data
        - context: Lambda execution context
    
    PROCESS:
        // 2.1 Customer Type Classification
        customerType = ExtractCustomerType(message)
        
        CASE customerType OF:
            SiteTypes.Rev: // Revenue/Commercial customers
                customerId = ExtractRevCustomerId(message)
                IF customerId == Guid.Empty OR customerId == null:
                    LOG ERROR "Invalid Revenue customer ID"
                    RETURN FilterResult.InvalidCustomer
                
                revAccountNumber = GetRevAccountNumber(context, customerId)
                integrationAuthenticationId = ExtractIntegrationAuthId(message)
                ProcessCustomerId(customerId, revAccountNumber, integrationAuthenticationId)
            
            SiteTypes.AMOP: // AMOP (Automated Mobile Optimization Platform) customers
                amopCustomerId = ExtractAMOPCustomerId(message)
                IF amopCustomerId == null OR amopCustomerId <= 0:
                    LOG ERROR "Invalid AMOP customer ID"
                    RETURN FilterResult.InvalidCustomer
                
                ProcessAMOPCustomerId(amopCustomerId)
            
            SiteTypes.CrossProvider: // Cross-provider customers
                customerIdentifier = ExtractCustomerIdentifier(message)
                serviceProviderIds = ExtractServiceProviderIds(message)
                ProcessCrossProviderCustomer(customerIdentifier, serviceProviderIds)
        
        // 2.2 Tenant Context Filtering
        tenantId = ExtractTenantId(message)
        IF tenantId <= 0:
            LOG ERROR "Invalid tenant ID"
            RETURN FilterResult.InvalidTenant
        
        // 2.3 Billing Period Validation
        billingPeriodId = ExtractBillingPeriodId(message)
        IF billingPeriodId <= 0:
            LOG ERROR "Invalid billing period ID"
            RETURN FilterResult.InvalidBillingPeriod
        
        // 2.4 Service Provider Validation
        serviceProviderId = ExtractOrDeriveServiceProviderId(message, billingPeriodId)
        IF serviceProviderId == null:
            LOG ERROR "Unable to determine service provider"
            RETURN FilterResult.InvalidServiceProvider
    
    OUTPUT: Validated customer context with filtering results
```

### 3. Device-Level Customer Filtering
**Purpose**: Filter SIM cards and devices based on customer rate plan assignments and eligibility

```
FUNCTION FilterDevicesByCustomer(context, customer, billingPeriod, tenantId)
    INPUT:
        - context: Execution context
        - customer: Customer information (type, ID, account details)
        - billingPeriod: Billing period context
        - tenantId: Tenant identifier
    
    PROCESS:
        // 3.1 Retrieve Customer Devices
        CASE customer.Type OF:
            SiteTypes.Rev:
                devices = GetOptimizationSimCards(context, null, 
                    billingPeriod.ServiceProviderId, customer.RevAccountNumber,
                    customer.IntegrationAuthenticationId, billingPeriod.Id, 
                    tenantId, customer.Type, null)
            
            SiteTypes.AMOP:
                devices = GetOptimizationSimCards(context, null,
                    billingPeriod.ServiceProviderId, null, null,
                    billingPeriod.Id, tenantId, customer.Type, customer.AMOPCustomerId)
            
            SiteTypes.CrossProvider:
                devices = GetCrossProviderCustomerSimCards(context,
                    customer.Type, customer.CustomerId, customer.RevAccountNumber,
                    customer.IntegrationAuthenticationId, billingPeriod,
                    customer.ServiceProviderIds)
        
        // 3.2 Filter by Customer Rate Plan Codes
        IF customer.Type == SiteTypes.Rev OR customer.Type == SiteTypes.AMOP:
            // Only include devices with valid customer rate plan codes
            devices = devices.Where(s => !IsNullOrWhiteSpace(s.CustomerRatePlanCode))
        
        IF customer.Type == SiteTypes.CrossProvider:
            // Cross-provider devices must have rate plan codes
            devices = devices.Where(s => !IsNullOrWhiteSpace(s.CustomerRatePlanCode))
        
        // 3.3 Group by Customer Rate Pool
        devicesByRatePool = devices.GroupBy(x => x.CustomerRatePoolId)
        
        // 3.4 Filter by Rate Plan Code Matching
        FOR EACH ratePoolGroup IN devicesByRatePool:
            ratePlanCodes = ratePoolGroup.Select(x => x.CustomerRatePlanCode).Distinct()
            
            // Match devices to eligible rate plans
            eligibleRatePlans = ratePlans.Where(x => ratePlanCodes.Contains(x.PlanName))
            
            IF eligibleRatePlans.Count == 0:
                LOG WARNING "No matching rate plans for device group"
                CONTINUE
        
        // 3.5 Handle Unassigned Devices
        unassignedDevices = devices.Where(c => IsNullOrWhiteSpace(c.CustomerRatePlanCode))
        IF unassignedDevices.Count > 0:
            LOG INFO "Processing {count} devices with no customer rate plan codes"
            ProcessNoRatePlanDevices(unassignedDevices)
    
    OUTPUT: Filtered and grouped devices eligible for rate plan processing
```

### 4. Cross-Provider Customer Filtering
**Purpose**: Special filtering logic for customers spanning multiple service providers

```
FUNCTION FilterCrossProviderCustomers(customer, serviceProviderIds, ratePlans)
    INPUT:
        - customer: Cross-provider customer information
        - serviceProviderIds: Comma-separated list of service provider IDs
        - ratePlans: Available rate plans
    
    PROCESS:
        // 4.1 Parse Service Provider IDs
        serviceProviderIdList = serviceProviderIds.Replace(" ", "")
                               .Split(COMMA_SEPARATOR)
                               .ToList()
        
        // 4.2 Filter Auto-Change Rate Plans by Service Provider
        autoChangeRatePlans = ratePlans.Where(rp => rp.AutoChangeRatePlan)
        
        IF autoChangeRatePlans.Any() AND !IsNullOrWhiteSpace(serviceProviderIds):
            // Check if rate plan supports all required service providers
            autoChangeRatePlans = autoChangeRatePlans.Where(x => 
                x.ServiceProviderIds.Split(COMMA_SEPARATOR)
                                   .ToList()
                                   .ContainsAllItems(serviceProviderIdList))
            
            IF !autoChangeRatePlans.Any():
                LOG ERROR "No valid cross-provider rate plans found for service providers: {serviceProviderIds}"
                RETURN FilterResult.NoValidRatePlans
        
        // 4.3 Validate Customer Information
        ArgumentNullException.ThrowIfNull(customer)
        
        // 4.4 Retrieve Cross-Provider Devices
        devices = GetCrossProviderCustomerSimCards(context,
            customer.CustomerType, customer.CustomerId,
            customer.RevAccountNumber, customer.IntegrationAuthenticationId,
            billingPeriod, serviceProviderIds)
        
        // 4.5 Filter by Rate Plan Codes
        devices = devices.Where(s => !IsNullOrWhiteSpace(s.CustomerRatePlanCode))
    
    OUTPUT: Filtered cross-provider customer data and devices
```

### 5. Customer Authentication and Authorization
**Purpose**: Validate customer access and permissions for rate plan processing

```
FUNCTION ValidateCustomerAccess(customer, context, tenantId)
    INPUT:
        - customer: Customer information
        - context: Execution context
        - tenantId: Tenant identifier
    
    PROCESS:
        // 5.1 Integration Authentication Validation
        IF customer.IntegrationAuthenticationId != null:
            authResult = ValidateIntegrationAuthentication(
                customer.IntegrationAuthenticationId, tenantId)
            IF authResult.Failed:
                LOG ERROR "Integration authentication failed"
                RETURN AuthResult.Failed
        
        // 5.2 Customer Type Authorization
        CASE customer.Type OF:
            SiteTypes.Rev:
                IF customer.RevCustomerId == null OR customer.RevCustomerId == Guid.Empty:
                    LOG ERROR "Invalid Revenue customer identifier"
                    RETURN AuthResult.InvalidCustomer
                
                // Validate account number access
                IF !HasAccountAccess(customer.RevAccountNumber, tenantId):
                    LOG ERROR "Insufficient account access permissions"
                    RETURN AuthResult.AccessDenied
            
            SiteTypes.AMOP:
                IF customer.AMOPCustomerId == null OR customer.AMOPCustomerId <= 0:
                    LOG ERROR "Invalid AMOP customer identifier"
                    RETURN AuthResult.InvalidCustomer
                
                // Validate AMOP customer access
                amopCustomer = GetAMOPCustomerById(context, customer.AMOPCustomerId)
                IF amopCustomer == null:
                    LOG ERROR "AMOP customer not found"
                    RETURN AuthResult.CustomerNotFound
        
        // 5.3 Tenant Context Validation
        IF !HasTenantAccess(customer, tenantId):
            LOG ERROR "Customer not associated with tenant"
            RETURN AuthResult.TenantMismatch
        
        // 5.4 Billing Period Access
        IF !HasBillingPeriodAccess(customer, billingPeriodId):
            LOG ERROR "Customer not authorized for billing period"
            RETURN AuthResult.BillingPeriodAccessDenied
    
    OUTPUT: Customer authorization result
```

### 6. Rate Plan Filtering by Customer Eligibility and Service Provider
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

### 7. Rate Plan Grouping by Auto Change Rate Plan Capability
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

### 8. Rate Plan Validation (Overage Rates and Data Charges)
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
    
    // Step 1: Customer Filtering and Validation
    customerFilterResult = FilterCustomers(message, context)
    IF customerFilterResult.Failed:
        RETURN ProcessingResult.CustomerFilterFailed
    
    // Step 2: Retrieve customer-specific rate plans
    ratePlans = GetCustomerRatePlans(customerId, billingPeriodId, serviceProviderId, tenantId)
    
    IF ratePlans.Count = 0:
        LOG ERROR "No rate plans found for customer"
        RETURN ProcessingResult.NoRatePlans
    
    // Step 3: Filter devices by customer criteria
    filteredDevices = FilterDevicesByCustomer(context, customer, billingPeriod, tenantId)
    
    // Step 4: Filter by customer eligibility and service provider
    filteredRatePlans = FilterRatePlansByEligibility(ratePlans, customer, serviceProvider)
    
    // Step 5: Group by Auto Change Rate Plan capability
    (staticRatePlans, dynamicRatePlans) = GroupRatePlansByAutoChange(filteredRatePlans)
    
    // Step 6: Process static rate plans (Customer Rate Pool)
    IF staticRatePlans.Count > 0:
        validationResult = ValidateRatePlanCharges(staticRatePlans)
        IF validationResult = Failed:
            RETURN ProcessingResult.ValidationFailed
        
        ProcessDevicesWithAutoChangeDisabledRatePlans(staticRatePlans)
    
    // Step 7: Process dynamic rate plans (Auto Change)
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
    
    // Step 8: Handle devices with no rate plans
    ProcessNoRatePlanDevices()
    
    RETURN ProcessingResult.Success
```

## Customer Filtering Criteria

### Customer Type Classification
- **SiteTypes.Rev**: Revenue/Commercial customers
  - Identified by `RevCustomerId` (GUID)
  - Requires valid account number (`RevAccountNumber`)
  - Uses integration authentication ID for API access
  
- **SiteTypes.AMOP**: AMOP platform customers  
  - Identified by `AMOPCustomerId` (integer)
  - Managed through AMOP platform
  - Different device retrieval and processing logic
  
- **SiteTypes.CrossProvider**: Multi-provider customers
  - Spans multiple service providers
  - Requires service provider ID validation
  - Special rate plan compatibility checks

### Device Filtering Rules
- **Rate Plan Code Requirement**: Devices must have valid `CustomerRatePlanCode`
- **Rate Pool Grouping**: Devices grouped by `CustomerRatePoolId`
- **Service Provider Matching**: Rate plans must support device's service providers
- **Tenant Context**: All operations scoped to specific tenant

### Authentication Requirements
- **Integration Authentication**: API-based customer access validation
- **Tenant Authorization**: Customer must belong to processing tenant
- **Billing Period Access**: Customer authorized for specific billing periods
- **Account Permissions**: Granular access control for customer accounts

## Key Business Rules

### Customer Filtering Rules
- **Customer Type Validation**: Must be valid SiteType (Rev, AMOP, CrossProvider)
- **Customer ID Validation**: 
  - Rev customers: Non-empty GUID
  - AMOP customers: Positive integer
  - CrossProvider: Valid customer identifier
- **Rate Plan Code Assignment**: Devices without rate plan codes processed separately
- **Service Provider Compatibility**: Cross-provider rate plans must support all required providers

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

### Customer Filtering Errors
- **Invalid Customer Type**: Reject processing with appropriate error message
- **Missing Customer ID**: Log error and skip customer processing
- **Authentication Failure**: Return authorization error
- **Tenant Mismatch**: Prevent cross-tenant data access

### Validation Failures
- **Zero Value Charges**: Stop processing and log error with plan details
- **Exceeded Limits**: Skip group and continue with next group
- **No Valid Plans**: Return appropriate error status

### Processing Errors
- **Database Connectivity**: Handle connection failures gracefully
- **Missing Data**: Process remaining valid data where possible
- **Optimization Failures**: Mark instance as completed with errors

## Performance Considerations

### Customer Filtering Optimizations
- **Batch Customer Validation**: Process multiple customers in parallel
- **Cached Authentication**: Cache authentication results for repeated access
- **Database Connection Pooling**: Reuse connections for customer data retrieval
- **Index Optimization**: Ensure proper indexing on customer lookup fields

### Optimization Strategies
- **Device Count Thresholds**: Skip optimization for groups with ≤ 1 device
- **Parallel Processing**: Process multiple rate plan groups concurrently
- **Caching**: Use Redis cache for frequently accessed data
- **Queue Management**: Limit concurrent optimization runs via `QueuesPerInstance`

### Scalability Features
- **Permutation Generation**: Generate rate plan sequences for optimization
- **Batch Processing**: Process devices in batches to manage memory usage
- **Background Processing**: Use SQS for asynchronous processing