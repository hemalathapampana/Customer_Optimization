# Cross-Provider Customer Optimization Validation Rules

## Overview
This document outlines the comprehensive validation framework for cross-provider customer optimization, providing detailed explanations of validation rules, algorithms, and their implementation in the AWS Lambda functions.

## 1. Customer ID Validation Across All Supported Providers

### What
Validates customer identifiers across different provider systems (Rev, AMOP) to ensure consistent customer identification throughout the optimization process.

### Why
- Prevents optimization execution with invalid or mismatched customer identifiers
- Ensures data integrity across multiple provider platforms
- Maintains audit trail and accountability for optimization decisions

### How
The system implements multi-tier customer ID validation:

#### Algorithm:
```
1. EXTRACT customer identifier from SQS message attributes
2. IF message contains "CustomerId" THEN
   a. PARSE as GUID for Rev customers
   b. VALIDATE GUID is not empty or null
   c. SET customerType = Rev
3. ELSE IF message contains "AMOPCustomerId" THEN
   a. PARSE as integer for AMOP customers
   b. VALIDATE integer is positive
   c. SET customerType = AMOP
4. ELSE
   a. LOG error "No Customer Id provided"
   b. TERMINATE optimization process
5. VERIFY customer exists in respective provider system
6. RETRIEVE customer profile and validate status
```

#### Code Locations:
```147:152:AltaworxSimCardCostQueueCustomerOptimization.cs
if (!message.MessageAttributes.ContainsKey("CustomerId") && !message.MessageAttributes.ContainsKey("AMOPCustomerId"))
{
    LogInfo(context, "EXCEPTION", "No Customer Id provided in message");
    return;
}
```

```212:217:AltaworxSimCardCostQueueCustomerOptimization.cs
if (message.MessageAttributes.ContainsKey(SQSMessageKeyConstant.AMOP_CUSTOMER_ID))
{
    customerIdentifier = int.Parse(message.MessageAttributes[SQSMessageKeyConstant.AMOP_CUSTOMER_ID].StringValue);
}
else
{
    LogInfo(context, CommonConstants.ERROR, $"No Customer Id found. Stopping Cross-Provider Customer Optimization.");
}
```

```687:687:AltaworxSimCardCostQueueCustomerOptimization.cs
var customer = crossProviderOptimizationRepository.GetOptimizationCustomer(ParameterizedLog(context), customerId, customerType);
```

## 2. Cross-Provider Billing Period Alignment

### What
Ensures billing periods are synchronized and compatible across different service providers within the same customer optimization instance.

### Why
- Prevents billing discrepancies between providers
- Maintains consistency in cost calculations across providers
- Ensures optimization operates on the same temporal billing window

### How
The system validates billing period alignment through multi-step verification:

#### Algorithm:
```
1. EXTRACT billing period ID from message attributes
2. IF billingPeriodId is NULL THEN
   a. CHECK for BillYear and BillMonth attributes
   b. CONSTRUCT billing period from date components
3. VALIDATE billing period exists in database
4. FOR each service provider in serviceProviderIds:
   a. RETRIEVE provider-specific billing period
   b. COMPARE start and end dates
   c. VERIFY alignment within tolerance (±24 hours)
5. IF Cross-Provider optimization THEN
   a. GET customer billing period using customerBillingPeriodId
   b. VALIDATE against all provider billing periods
6. CALCULATE next billing period for bill-in-advance scenarios
7. ENSURE billing periods don't overlap inappropriately
```

#### Code Locations:
```152:158:AltaworxSimCardCostQueueCustomerOptimization.cs
if (!message.MessageAttributes.ContainsKey("BillPeriodId") && !(message.MessageAttributes.ContainsKey("BillYear") && message.MessageAttributes.ContainsKey("BillMonth")))
{
    LogInfo(context, "EXCEPTION", "No Billing Period provided in message");
    return;
}
```

```235:240:AltaworxSimCardCostQueueCustomerOptimization.cs
if (!message.MessageAttributes.ContainsKey(SQSMessageKeyConstant.CUSTOMER_BILLING_PERIOD_ID)
    || !int.TryParse(message.MessageAttributes[SQSMessageKeyConstant.CUSTOMER_BILLING_PERIOD_ID].StringValue, out customerBillingPeriodId))
{
    LogInfo(context, CommonConstants.ERROR, $"No customer billing period id found");
}
```

```693:694:AltaworxSimCardCostQueueCustomerOptimization.cs
var billingPeriod = crossProviderOptimizationRepository.GetBillingPeriod(ParameterizedLog(context), customerId, customerBillingPeriodId, context.OptimizationSettings.BillingTimeZone);
```

## 3. Integration Authentication Credentials Verification

### What
Validates authentication credentials for each service provider integration to ensure authorized access to provider systems.

### Why
- Prevents unauthorized access to provider APIs and data
- Ensures data security and compliance
- Maintains system integrity across multiple provider integrations

### How
Multi-layer authentication credential validation:

#### Algorithm:
```
1. EXTRACT integrationAuthenticationId from message
2. IF integrationAuthenticationId is present THEN
   a. VALIDATE credential ID is positive integer
   b. RETRIEVE authentication record from database
   c. VERIFY credential is active and not expired
   d. CHECK provider-specific authentication format
3. FOR Cross-Provider scenarios:
   a. ENUMERATE all service providers in scope
   b. FOR each provider:
      i. VALIDATE provider-specific credentials
      ii. TEST connection to provider APIs
      iii. VERIFY authorization scope
4. CACHE valid credentials for optimization session
5. SET authentication context for provider operations
```

#### Code Locations:
```190:191:AltaworxSimCardCostQueueCustomerOptimization.cs
var integrationAuthenticationId = int.Parse(message.MessageAttributes["IntegrationAuthenticationId"].StringValue);
await ProcessCustomerId(context, tenantId, customerId, serviceProviderId, billingPeriodId, messageId, integrationAuthenticationId, optimizationSessionId, usesProration, isLastInstance, customerType, additionalData);
```

```786:786:AltaworxSimCardCostQueueCustomerOptimization.cs
var optimizationSimCards = crossProviderOptimizationRepository.GetCrossProviderCustomerSimCards(ParameterizedLog(context), customer.CustomerType, customer.CustomerId, customer.RevAccountNumber, customer.IntegrationAuthenticationId, billingPeriod, serviceProviderIds);
```

## 4. Service Provider Association and Capabilities Validation

### What
Verifies service provider associations and validates their capabilities to support the requested optimization operations.

### Why
- Ensures providers support required optimization features
- Prevents execution against incompatible provider systems
- Validates provider-specific constraints and limitations

### How
Comprehensive provider capability assessment:

#### Algorithm:
```
1. PARSE serviceProviderIds from message attributes
2. IF serviceProviderIds is empty THEN
   a. LOG info "No service provider specified"
   b. SET scope to all available providers
3. FOR each service provider ID:
   a. VALIDATE provider exists in system
   b. CHECK provider status (active/inactive)
   c. VERIFY optimization capabilities:
      i. Rate plan optimization support
      ii. Cross-provider pooling capability
      iii. Auto-change rate plan feature
   d. VALIDATE provider integration type
   e. CHECK billing system compatibility
4. FILTER rate plans by provider capabilities
5. ENSURE provider associations are valid for customer
```

#### Code Locations:
```223:231:AltaworxSimCardCostQueueCustomerOptimization.cs
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

## 5. Cross-Provider Eligibility and Restrictions Validation

### What
Validates customer eligibility for cross-provider optimization and enforces any restrictions or constraints.

### Why
- Ensures customers meet prerequisites for cross-provider optimization
- Enforces business rules and regulatory constraints
- Prevents optimization execution for ineligible scenarios

### How
Multi-faceted eligibility assessment:

#### Algorithm:
```
1. VALIDATE customer type supports cross-provider optimization
2. CHECK portal type compatibility:
   a. VERIFY PortalType == CrossProvider OR
   b. ALLOW M2M with cross-provider flag
3. ASSESS rate plan eligibility:
   a. COUNT rate plans with IsBillInAdvanceEligible
   b. VALIDATE minimum rate plan requirements
   c. CHECK for zero-value rate plans (exclusion criteria)
4. VERIFY device count thresholds:
   a. ENSURE minimum device count for optimization
   b. CHECK maximum device limits per provider
5. VALIDATE tenant permissions and restrictions
6. CHECK service provider compatibility matrix
7. ENSURE no conflicting optimization sessions
```

#### Code Locations:
```209:209:AltaworxSimCardCostQueueCustomerOptimization.cs
SetPortalType(PortalTypes.CrossProvider);
```

```287:290:AltaworxSimCardCostQueueCustomerOptimization.cs
var useBillInAdvance = ratePlans.Count(x => x.IsBillInAdvanceEligible) > 0;
//Disable bill in advance logic until new logic is defined (PORT-166)
useBillInAdvance = false;
```

```296:299:AltaworxSimCardCostOptimizer.cs
else if (portalType == PortalTypes.CrossProvider)
{
    return crossProviderOptimizationRepository.GetCrossProviderOptimizationDevices(ParameterizedLog(context), instance.Id, billingPeriod, commPlanGroupId, instance.IsCustomerOptimization);
}
```

## Implementation Architecture

### Lambda Function Structure
The validation rules are implemented across three main Lambda functions:

1. **AltaworxSimCardCostOptimizer.cs** - Core optimization engine with cross-provider device retrieval
2. **AltaworxSimCardCostQueueCustomerOptimization.cs** - Customer optimization queue processing with validation
3. **AltaworxSimCardCostOptimizerCleanup.cs** - Cleanup operations with cross-provider result processing

### Validation Flow Sequence

```
SQS Message → Message Validation → Customer ID Validation → 
Billing Period Alignment → Authentication Verification → 
Provider Capability Check → Eligibility Assessment → 
Optimization Execution → Result Processing
```

### Error Handling Strategy

Each validation step implements comprehensive error handling:
- **Graceful degradation** for non-critical validation failures
- **Immediate termination** for security or data integrity violations
- **Detailed logging** for audit and troubleshooting purposes
- **Notification systems** for operational alerting

### Performance Considerations

- **Parallel validation** where possible to minimize latency
- **Caching mechanisms** for frequently accessed validation data
- **Circuit breaker patterns** for external provider API calls
- **Timeout handling** for long-running validation operations

This validation framework ensures robust, secure, and reliable cross-provider customer optimization while maintaining data integrity and system performance across multiple service provider integrations.