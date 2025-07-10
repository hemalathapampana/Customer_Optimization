# Cross-Provider Customer Processing Logic

## Overview
This document provides detailed analysis of the cross-provider customer processing logic, explaining how different customer types are processed, authenticated, and optimized across multiple service providers with provider-specific constraints.

## 1. Rev Customers: GUID-based Identification with Provider-Specific Integration Authentication

### What
Rev customers are identified using GUID (Globally Unique Identifier) format and require provider-specific integration authentication for each service provider they interact with.

### Why
- **Data Integrity**: GUIDs ensure unique customer identification across distributed systems
- **Security**: Provider-specific authentication prevents unauthorized access to sensitive customer data
- **Compliance**: Maintains audit trails for regulatory requirements
- **Scalability**: Supports multiple provider integrations without identifier conflicts

### How
Rev customer processing implements a multi-step authentication and identification workflow:

#### Algorithm:
```
1. EXTRACT CustomerId from SQS message attributes
2. VALIDATE CustomerId format as GUID:
   a. PARSE message.MessageAttributes["CustomerId"].StringValue as GUID
   b. CHECK if GUID is not empty or null
   c. IF invalid THEN log "Blank Customer Id provided" and TERMINATE
3. SET customerType = SiteTypes.Rev
4. RETRIEVE Rev account number:
   a. CALL GetRevAccountNumber(context, customerId)
   b. VALIDATE account exists in Rev system
5. EXTRACT integrationAuthenticationId from message
6. VALIDATE authentication credentials:
   a. VERIFY integrationAuthenticationId is positive integer
   b. RETRIEVE authentication record from database
   c. CONFIRM credentials are active and not expired
7. PROCESS customer rate plans with authentication context:
   a. CALL GetCustomerRatePlans(context, customerId, billingPeriodId, serviceProviderId, tenantId)
   b. FILTER rate plans by provider-specific constraints
8. INITIATE optimization instance with Rev customer context
9. EXECUTE optimization with provider-specific integration authentication
```

#### Code Locations:
```157:164:AltaworxSimCardCostQueueCustomerOptimization.cs
Guid customerId = Guid.Empty;
if (message.MessageAttributes.ContainsKey("CustomerId"))
{
    customerId = Guid.Parse(message.MessageAttributes["CustomerId"].StringValue);
}
if (customerType == SiteTypes.Rev && (string.IsNullOrEmpty(customerId.ToString()) || customerId == Guid.Empty))
{
    LogInfo(context, "EXCEPTION", "Blank Customer Id provided in message");
    return;
}
```

```275:282:AltaworxSimCardCostQueueCustomerOptimization.cs
private async Task ProcessCustomerId(KeySysLambdaContext context, int tenantId, Guid customerId,
    int? serviceProviderId, int? billingPeriodId, string messageId, int integrationAuthenticationId,
    long optimizationSessionId, bool usesProration, bool isLastInstance, SiteTypes customerType, string additionalData)
{
    LogInfo(context, "SUB", $"ProcessCustomerId({tenantId},{customerId},{serviceProviderId},{billingPeriodId},{messageId},{integrationAuthenticationId})");
    
    // get customer account number
    var revAccountNumber = GetRevAccountNumber(context, customerId);
```

```190:191:AltaworxSimCardCostQueueCustomerOptimization.cs
var integrationAuthenticationId = int.Parse(message.MessageAttributes["IntegrationAuthenticationId"].StringValue);
await ProcessCustomerId(context, tenantId, customerId, serviceProviderId, billingPeriodId, messageId, integrationAuthenticationId, optimizationSessionId, usesProration, isLastInstance, customerType, additionalData);
```

## 2. AMOP Customers: Integer-based Identification with Unified Cross-Provider Authentication

### What
AMOP customers use integer-based identification and leverage unified authentication that works across multiple service providers simultaneously.

### Why
- **Simplified Integration**: Single authentication mechanism reduces complexity
- **Performance**: Integer lookups are faster than GUID operations
- **Cross-Provider Efficiency**: Unified authentication eliminates per-provider credential management
- **Operational Simplicity**: Reduces authentication overhead for multi-provider scenarios

### How
AMOP customer processing uses streamlined identification and unified authentication:

#### Algorithm:
```
1. EXTRACT AMOPCustomerId from SQS message attributes
2. VALIDATE AMOPCustomerId format as integer:
   a. PARSE message.MessageAttributes[SQSMessageKeyConstant.AMOP_CUSTOMER_ID].StringValue as int
   b. VERIFY integer is positive
   c. IF invalid THEN throw ArgumentNullException
3. SET customerType = SiteTypes.AMOP
4. RETRIEVE customer information:
   a. CALL GetAMOPCustomerById(context, AMOPCustomerId)
   b. VALIDATE customer exists in AMOP system
5. USE unified authentication context:
   a. NO provider-specific integrationAuthenticationId required
   b. LEVERAGE customer's built-in cross-provider credentials
6. PROCESS customer rate plans with unified context:
   a. CALL GetCustomerRatePlans(context, Guid.Empty, billingPeriodId, serviceProviderId, tenantId, customerType, AMOPCustomerId)
   b. APPLY cross-provider rate plan filtering
7. INITIATE optimization instance with AMOP customer context
8. EXECUTE optimization with unified cross-provider authentication
```

#### Code Locations:
```168:172:AltaworxSimCardCostQueueCustomerOptimization.cs
int? amopCustomerId = null;
if (message.MessageAttributes.ContainsKey(SQSMessageKeyConstant.AMOP_CUSTOMER_ID))
{
    amopCustomerId = int.Parse(message.MessageAttributes[SQSMessageKeyConstant.AMOP_CUSTOMER_ID].StringValue);
}
```

```396:403:AltaworxSimCardCostQueueCustomerOptimization.cs
private async Task ProcessAMOPCustomerId(KeySysLambdaContext context, int tenantId, SiteTypes customerType, int AMOPCustomerId,
    int? serviceProviderId, int? billingPeriodId, string messageId,
    long optimizationSessionId, bool usesProration, bool isLastInstance, string additionalData)
{
    LogInfo(context, "SUB", $"ProcessAMOPCustomerId({tenantId},{AMOPCustomerId},{serviceProviderId},{billingPeriodId},{messageId})");

    // get customer rate plans
    var ratePlans = GetCustomerRatePlans(context, Guid.Empty, (int)billingPeriodId, serviceProviderId, tenantId, customerType, AMOPCustomerId);
```

```1557:1571:AltaworxSimCardCostOptimizerCleanup.cs
private Site GetAMOPCustomerById(KeySysLambdaContext context, int amopCustomerId)
{
    LogInfo(context, "SUB", $"GetAMOPCustomerById(,{amopCustomerId})");
    var customer = new Site
    {
        Id = amopCustomerId
    };
    using (var conn = new SqlConnection(context.ConnectionString))
    {
        using (var cmd = new SqlCommand("SELECT TOP 1 id, Name FROM [Site] WHERE id = @amopCustomerId", conn))
        {
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@amopCustomerId", amopCustomerId);
```

## 3. Multi-Provider Processing: Simultaneous Processing Across Multiple Carriers

### What
The system processes optimization requests simultaneously across multiple service providers including Verizon, AT&T, T-Mobile, and other supported carriers.

### Why
- **Cost Optimization**: Identifies best rates across all available providers
- **Flexibility**: Allows customers to leverage multiple carrier relationships
- **Efficiency**: Reduces time-to-completion through parallel processing
- **Comprehensive Analysis**: Ensures optimal rate plan selection across entire provider ecosystem

### How
Multi-provider processing coordinates optimization across multiple carriers:

#### Algorithm:
```
1. EXTRACT serviceProviderIds from SQS message attributes
2. PARSE provider list:
   a. IF serviceProviderIds is provided THEN
      i. SPLIT serviceProviderIds by comma delimiter
      ii. VALIDATE each provider ID exists in system
   b. ELSE
      i. LOG "No service provider specified"
      ii. SET scope to all available providers for customer
3. FOR each service provider in scope:
   a. VALIDATE provider status (active/inactive)
   b. CHECK provider capabilities and constraints
   c. RETRIEVE provider-specific rate plans
   d. VALIDATE billing period alignment
4. COORDINATE cross-provider optimization:
   a. CALL GetCrossProviderCustomerRatePlans(serviceProviderIds, customerType, customerIds, billingPeriod, tenantId)
   b. FILTER rate plans by provider capabilities
   c. ENSURE provider compatibility for rate plan changes
5. EXECUTE simultaneous optimization across all providers:
   a. CREATE optimization instance with multiple provider context
   b. PROCESS devices and rate plans across all providers
   c. COORDINATE result aggregation across providers
6. GENERATE comprehensive optimization results
```

#### Code Locations:
```222:231:AltaworxSimCardCostQueueCustomerOptimization.cs
var serviceProviderIds = string.Empty;
if (message.MessageAttributes.ContainsKey(SQSMessageKeyConstant.SERVICE_PROVIDER_IDS))
{
    serviceProviderIds = message.MessageAttributes[SQSMessageKeyConstant.SERVICE_PROVIDER_IDS].StringValue;
}
else
{
    LogInfo(context, CommonConstants.INFO, $"No service provider specified. Running Cross-Provider Customer Optimization for all service provider");
}
```

```697:697:AltaworxSimCardCostQueueCustomerOptimization.cs
var ratePlans = customerRatePlanRepository.GetCrossProviderCustomerRatePlans(ParameterizedLog(context), serviceProviderIds, customerType, new List<int> { customerId }, billingPeriod, tenantId);
```

```807:813:AltaworxSimCardCostQueueCustomerOptimization.cs
if (autoChangeRatePlans.Any() && !string.IsNullOrWhiteSpace(serviceProviderIds))
{
    var serviceProviderIdList = serviceProviderIds.Replace(" ", "").Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList();
    autoChangeRatePlans = autoChangeRatePlans.Where(x => x.ServiceProviderIds.Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList().ContainsAllItems(serviceProviderIdList)).ToList();
    LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.CROSS_PROVIDER_CUSTOMER_RATE_PLAN_FOUND, serviceProviderIds));
}
```

```725:727:AltaworxSimCardCostQueueCustomerOptimization.cs
var instanceId = crossProviderOptimizationRepository.StartCrossProviderOptimizationInstance(ParameterizedLog(context), tenantId, messageId,
    customer, PortalTypes.CrossProvider, optimizationSessionId,
    useBillInAdvance, billingPeriod, nextBillingPeriod, serviceProviderIds);
```

## 4. Provider-Specific Constraints: Individual Provider Business Rules and Limitations

### What
The system enforces individual business rules and limitations specific to each service provider, including rate plan constraints, integration types, and optimization capabilities.

### Why
- **Compliance**: Ensures adherence to provider-specific business rules
- **Data Integrity**: Prevents invalid rate plan assignments
- **Optimization Accuracy**: Applies correct constraints for each provider
- **Risk Management**: Avoids optimization configurations that violate provider policies

### How
Provider-specific constraint enforcement through validation and filtering:

#### Algorithm:
```
1. IDENTIFY integration type for each provider:
   a. RETRIEVE integrationType from instance.IntegrationId
   b. VALIDATE integration type supports optimization
   c. CHECK provider-specific capabilities
2. VALIDATE rate plan constraints:
   a. FOR each rate plan in optimization scope:
      i. CHECK DataPerOverageCharge != 0.0M
      ii. CHECK OverageRate != 0.0M
      iii. IF zero-value found THEN log error and exclude from optimization
   b. VALIDATE rate plan count limits:
      i. CHECK rate plan count <= OptimizationConstant.RatePlanLimit (15)
      ii. CHECK rate plan count >= OptimizationConstant.RatePlanMinimumLimit
3. APPLY provider-specific business rules:
   a. IF provider is Jasper OR POD19 OR TMobileJasper OR Rogers THEN
      i. ENABLE auto-update rate plans capability
      ii. APPLY provider-specific rate plan update logic
   b. FILTER rate plans by provider capabilities
   c. VALIDATE SIM pooling support per provider
4. ENFORCE billing constraints:
   a. VALIDATE billing period compatibility
   b. CHECK provider-specific billing rules
   c. ENSURE proration support where required
5. VALIDATE device count thresholds:
   a. CHECK minimum device count for optimization
   b. VERIFY provider-specific device limits
6. COORDINATE constraint enforcement across multiple providers:
   a. ENSURE compatible constraints across provider mix
   b. RESOLVE conflicts in provider-specific rules
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

```396:400:AltaworxSimCardCostOptimizerCleanup.cs
if ((integrationType == IntegrationType.Jasper
    || integrationType == IntegrationType.POD19
    || integrationType == IntegrationType.TMobileJasper
    || integrationType == IntegrationType.Rogers)
    && context.OptimizationSettings.CanAutoUpdateRatePlans && instance.RevCustomerId == null && !instance.AMOPCustomerId.HasValue)
```

```631:640:AltaworxSimCardCostQueueCustomerOptimization.cs
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

```251:267:AltaworxSimCardCostQueueCustomerOptimization.cs
private int? GetServiceProviderIdFromBillingPeriod(KeySysLambdaContext context, int? billingPeriodId)
{
    int? serviceProviderId = null;
    using (var conn = new SqlConnection(context.ConnectionString))
    {
        using (var cmd = new SqlCommand("SELECT ServiceProviderId FROM BillingPeriod bp WHERE bp.id = @billingPeriodId", conn))
        {
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@billingPeriodId", billingPeriodId);
            conn.Open();

            SqlDataReader rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                serviceProviderId = int.Parse(rdr[0].ToString());
            }

            conn.Close();
        }
    }

    return serviceProviderId;
}
```

## Implementation Architecture

### Customer Type Processing Flow
```
SQS Message → Customer Type Detection → 
Rev Path (GUID + Provider Auth) OR AMOP Path (Integer + Unified Auth) → 
Service Provider Resolution → Rate Plan Retrieval → 
Constraint Validation → Optimization Execution → Result Processing
```

### Authentication Strategy
- **Rev Customers**: Provider-specific integration authentication per service provider
- **AMOP Customers**: Unified authentication mechanism across all providers
- **Cross-Provider**: Coordinated authentication across multiple provider APIs

### Constraint Enforcement Hierarchy
1. **System-Level Constraints**: Hard limits (rate plan counts, device thresholds)
2. **Provider-Level Constraints**: Business rules specific to each carrier
3. **Customer-Level Constraints**: Account-specific limitations and preferences
4. **Integration-Level Constraints**: API and technical limitations per provider

### Error Handling and Failover
- **Graceful degradation** when individual providers are unavailable
- **Partial optimization** execution when some constraints cannot be satisfied
- **Comprehensive logging** for audit and troubleshooting across all providers
- **Provider-specific error handling** tailored to each integration type

This processing logic ensures robust, secure, and efficient cross-provider customer optimization while maintaining the unique requirements and constraints of each service provider and customer type.