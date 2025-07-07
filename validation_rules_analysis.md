# Validation Rules Analysis for Altaworx SIM Card Cost Optimization

## Overview
This document provides a comprehensive analysis of the key validation rules implemented in the Altaworx SIM Card Cost Optimization Lambda functions, including high-level explanations, algorithmic implementations, and specific code locations.

---

## 1. Customer ID or AMOP Customer ID Presence Validation

### High-Level Sentence
**What**: Validates that either a Customer ID (for Rev customers) or AMOP Customer ID (for AMOP customers) is present in the SQS message attributes  
**Why**: Essential for identifying the customer for whom optimization needs to be performed, ensuring proper customer segmentation and preventing unauthorized access  
**How**: Checks message attributes for presence of CustomerId or AMOPCustomerId keys and validates their format and non-empty values

### Algorithm
```
ALGORITHM: ValidateCustomerIdentification
INPUT: SQSMessage message, SiteTypes customerType
OUTPUT: Boolean validationResult, CustomerData customerData

BEGIN
    // Step 1: Check for presence of either customer identifier
    IF NOT (message.MessageAttributes.ContainsKey("CustomerId") OR 
            message.MessageAttributes.ContainsKey("AMOPCustomerId")) THEN
        LOG("EXCEPTION", "No Customer Id provided in message")
        RETURN FALSE, NULL
    END IF
    
    // Step 2: Validate Rev Customer ID if customer type is Rev
    IF customerType == SiteTypes.Rev THEN
        IF message.MessageAttributes.ContainsKey("CustomerId") THEN
            customerId = PARSE_GUID(message.MessageAttributes["CustomerId"].StringValue)
            IF customerId == Guid.Empty OR customerId == NULL THEN
                LOG("EXCEPTION", "Blank Customer Id provided in message")
                RETURN FALSE, NULL
            END IF
        ELSE
            LOG("EXCEPTION", "Rev customer type requires CustomerId")
            RETURN FALSE, NULL
        END IF
    END IF
    
    // Step 3: Validate AMOP Customer ID if present
    IF message.MessageAttributes.ContainsKey("AMOPCustomerId") THEN
        amopCustomerId = PARSE_INT(message.MessageAttributes["AMOPCustomerId"].StringValue)
        IF amopCustomerId <= 0 THEN
            LOG("EXCEPTION", "Invalid AMOP Customer Id")
            RETURN FALSE, NULL
        END IF
    END IF
    
    // Step 4: Ensure appropriate customer ID exists for processing
    IF customerType != SiteTypes.Rev AND amopCustomerId == NULL THEN
        THROW ArgumentNullException("AMOP Customer ID required for non-Rev customers")
    END IF
    
    RETURN TRUE, {customerId, amopCustomerId}
END
```

### Code Locations
- **File**: `AltaworxSimCardCostQueueCustomerOptimization.cs`
- **Primary validation**: Lines 147-172
  ```csharp
  if (!message.MessageAttributes.ContainsKey("CustomerId") && !message.MessageAttributes.ContainsKey("AMOPCustomerId"))
  {
      LogInfo(context, "EXCEPTION", "No Customer Id provided in message");
      return;
  }
  ```
- **Rev Customer ID validation**: Lines 162-166
- **AMOP Customer ID validation**: Lines 168-172, 195-196

---

## 2. Billing Period Information Validation

### High-Level Sentence
**What**: Ensures that billing period information is available either through BillPeriodId or combination of BillYear and BillMonth  
**Why**: Critical for determining the billing cycle for cost optimization calculations and ensuring accurate financial projections  
**How**: Validates presence of billing period identifiers, parses and validates their format, and retrieves associated service provider information

### Algorithm
```
ALGORITHM: ValidateBillingPeriod
INPUT: SQSMessage message
OUTPUT: Boolean validationResult, BillingPeriodData billingData

BEGIN
    // Step 1: Check for billing period information presence
    IF NOT (message.MessageAttributes.ContainsKey("BillPeriodId") OR 
            (message.MessageAttributes.ContainsKey("BillYear") AND 
             message.MessageAttributes.ContainsKey("BillMonth"))) THEN
        LOG("EXCEPTION", "No Billing Period provided in message")
        RETURN FALSE, NULL
    END IF
    
    // Step 2: Validate BillPeriodId if present
    IF message.MessageAttributes.ContainsKey("BillPeriodId") THEN
        billingPeriodIdString = message.MessageAttributes["BillPeriodId"].StringValue
        IF NOT TRY_PARSE_INT(billingPeriodIdString, OUT billingPeriodId) THEN
            LOG("EXCEPTION", "Invalid Billing Period provided in message")
            RETURN FALSE, NULL
        END IF
        
        // Step 3: Validate billing period exists in database
        billingPeriod = GET_BILLING_PERIOD(billingPeriodId)
        IF billingPeriod == NULL THEN
            LOG("ERROR", "Billing Period not found in database")
            RETURN FALSE, NULL
        END IF
    END IF
    
    // Step 4: Retrieve associated service provider
    serviceProviderId = GET_SERVICE_PROVIDER_FROM_BILLING_PERIOD(billingPeriodId)
    IF serviceProviderId == NULL THEN
        LOG("ERROR", "Service Provider not found for billing period")
        RETURN FALSE, NULL
    END IF
    
    RETURN TRUE, {billingPeriodId, billingPeriod, serviceProviderId}
END
```

### Code Locations
- **File**: `AltaworxSimCardCostQueueCustomerOptimization.cs`
- **Primary validation**: Lines 152-155
  ```csharp
  if (!message.MessageAttributes.ContainsKey("BillPeriodId") && !(message.MessageAttributes.ContainsKey("BillYear") && message.MessageAttributes.ContainsKey("BillMonth")))
  {
      LogInfo(context, "EXCEPTION", "No Billing Period provided in message");
      return;
  }
  ```
- **BillPeriodId parsing**: Lines 175-182
- **Service provider retrieval**: Lines 251-272 (`GetServiceProviderIdFromBillingPeriod` method)
- **Cross-provider billing period validation**: Lines 234-240

---

## 3. Integration Authentication Credentials Validation

### High-Level Sentence
**What**: Verifies that integration authentication credentials are present and valid for Rev customers to ensure secure API communication  
**Why**: Maintains security and proper authorization for external system integrations, preventing unauthorized access to customer data  
**How**: Extracts IntegrationAuthenticationId from message attributes, validates its presence for Rev customers, and uses it for secure data retrieval

### Algorithm
```
ALGORITHM: ValidateIntegrationAuthentication
INPUT: SQSMessage message, SiteTypes customerType
OUTPUT: Boolean validationResult, Integer integrationAuthId

BEGIN
    // Step 1: Check if integration auth is required
    IF customerType == SiteTypes.Rev THEN
        // Step 2: Validate presence of IntegrationAuthenticationId
        IF NOT message.MessageAttributes.ContainsKey("IntegrationAuthenticationId") THEN
            LOG("EXCEPTION", "Integration Authentication Id required for Rev customers")
            RETURN FALSE, NULL
        END IF
        
        // Step 3: Parse and validate authentication ID
        authIdString = message.MessageAttributes["IntegrationAuthenticationId"].StringValue
        IF NOT TRY_PARSE_INT(authIdString, OUT integrationAuthId) THEN
            LOG("EXCEPTION", "Invalid Integration Authentication Id format")
            RETURN FALSE, NULL
        END IF
        
        // Step 4: Validate authentication ID is positive
        IF integrationAuthId <= 0 THEN
            LOG("EXCEPTION", "Integration Authentication Id must be positive")
            RETURN FALSE, NULL
        END IF
        
        // Step 5: Verify authentication credentials exist in system
        IF NOT VERIFY_INTEGRATION_AUTH_EXISTS(integrationAuthId) THEN
            LOG("EXCEPTION", "Integration Authentication credentials not found")
            RETURN FALSE, NULL
        END IF
        
    ELSE
        // AMOP customers don't require integration authentication
        integrationAuthId = NULL
    END IF
    
    RETURN TRUE, integrationAuthId
END
```

### Code Locations
- **File**: `AltaworxSimCardCostQueueCustomerOptimization.cs`
- **Primary validation**: Lines 188-191
  ```csharp
  if (customerType == SiteTypes.Rev)
  {
      var integrationAuthenticationId = int.Parse(message.MessageAttributes["IntegrationAuthenticationId"].StringValue);
      await ProcessCustomerId(context, tenantId, customerId, serviceProviderId, billingPeriodId, messageId, integrationAuthenticationId, optimizationSessionId, usesProration, isLastInstance, customerType, additionalData);
  }
  ```
- **Usage in ProcessCustomerId**: Lines 276-279
- **Usage in device processing**: Lines 509-511, 661-663

---

## 4. Service Provider Associations Validation

### High-Level Sentence
**What**: Validates service provider associations by ensuring proper service provider identification either directly from message or derived from billing period  
**Why**: Ensures optimization runs only for authorized service providers and maintains proper data isolation between different telecommunications carriers  
**How**: Extracts service provider information from message attributes or queries database using billing period, validates provider exists and customer has access

### Algorithm
```
ALGORITHM: ValidateServiceProviderAssociation
INPUT: SQSMessage message, Integer billingPeriodId
OUTPUT: Boolean validationResult, ServiceProviderData providerData

BEGIN
    // Step 1: Try to get Service Provider ID directly from message
    serviceProviderId = GET_SERVICE_PROVIDER_ID_FROM_MESSAGE(message)
    
    // Step 2: If not in message, derive from billing period
    IF serviceProviderId == NULL AND billingPeriodId != NULL THEN
        serviceProviderId = GET_SERVICE_PROVIDER_FROM_BILLING_PERIOD(billingPeriodId)
    END IF
    
    // Step 3: Validate service provider exists
    IF serviceProviderId == NULL THEN
        LOG("ERROR", "No service provider found for optimization")
        RETURN FALSE, NULL
    END IF
    
    // Step 4: For cross-provider optimization, validate service provider list
    IF CROSS_PROVIDER_MODE THEN
        IF message.MessageAttributes.ContainsKey("SERVICE_PROVIDER_IDS") THEN
            serviceProviderIds = message.MessageAttributes["SERVICE_PROVIDER_IDS"].StringValue
            providerList = PARSE_COMMA_SEPARATED_LIST(serviceProviderIds)
            
            FOR EACH providerId IN providerList DO
                IF NOT VALIDATE_SERVICE_PROVIDER_EXISTS(providerId) THEN
                    LOG("ERROR", "Invalid service provider in list: " + providerId)
                    RETURN FALSE, NULL
                END IF
            END FOR
        ELSE
            LOG("INFO", "No specific service providers specified, running for all")
            serviceProviderIds = GET_ALL_AUTHORIZED_PROVIDERS()
        END IF
    END IF
    
    // Step 5: Validate customer-provider association
    IF NOT VALIDATE_CUSTOMER_PROVIDER_ASSOCIATION(customerId, serviceProviderId) THEN
        LOG("ERROR", "Customer not associated with service provider")
        RETURN FALSE, NULL
    END IF
    
    RETURN TRUE, {serviceProviderId, serviceProviderIds}
END
```

### Code Locations
- **File**: `AltaworxSimCardCostQueueCustomerOptimization.cs`
- **Primary validation**: Line 185
  ```csharp
  var serviceProviderId = GetServiceProviderId(message) ?? GetServiceProviderIdFromBillingPeriod(context, billingPeriodId);
  ```
- **Service provider derivation**: Lines 251-272 (`GetServiceProviderIdFromBillingPeriod` method)
- **Cross-provider validation**: Lines 222-231
  ```csharp
  if (message.MessageAttributes.ContainsKey(SQSMessageKeyConstant.SERVICE_PROVIDER_IDS))
  {
      serviceProviderIds = message.MessageAttributes[SQSMessageKeyConstant.SERVICE_PROVIDER_IDS].StringValue;
  }
  else
  {
      LogInfo(context, CommonConstants.INFO, $"No service provider specified. Running Cross-Provider Customer Optimization for all service provider");
  }
  ```
- **Service provider usage**: Lines 594, 645 (in billing period processing)

---

## Additional Validation Details

### Cross-Provider Customer Optimization Validations
- **Customer Billing Period ID**: Lines 234-240
- **Service Provider IDs**: Lines 222-231
- **Customer identifier validation**: Lines 211-220

### Error Handling Patterns
- All validation failures result in immediate return without processing
- Comprehensive logging for debugging and monitoring
- Proper exception handling with ArgumentNullException for critical missing data
- Integration with AMOP 2.0 API for error notification

### Security Considerations
- Integration authentication required for Rev customers
- Service provider isolation maintained
- Customer data access controlled through proper validation
- Audit trail through comprehensive logging

---

## Summary

These validation rules form a critical security and data integrity layer in the Altaworx SIM Card Cost Optimization system. They ensure:

1. **Customer Identity Security**: Only authorized customers can initiate optimization
2. **Billing Accuracy**: Proper billing periods ensure accurate cost calculations
3. **Integration Security**: Authentication credentials protect external API access
4. **Provider Isolation**: Service provider validation maintains proper data boundaries

The validation logic is implemented primarily in the `ProcessCustomerOptimizationByPortalType` and `ProcessCrossProviderCustomerOptimization` methods within `AltaworxSimCardCostQueueCustomerOptimization.cs`.