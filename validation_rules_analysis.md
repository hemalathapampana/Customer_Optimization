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
INPUT: SQS Message, Customer Type
OUTPUT: Validation Result (Success/Failure), Customer Data

Step 1: Check for Required Customer Identifiers
       Examine the incoming message for either CustomerId or AMOPCustomerId attributes
       If neither identifier is present in the message attributes
       Then log exception "No Customer Id provided" and return failure

Step 2: Validate Rev Customer Requirements
       If the customer type is Rev
       Then check if CustomerId attribute exists in message
            If CustomerId exists, parse it as a GUID format
            If the parsed GUID is empty or null
            Then log exception "Blank Customer Id provided" and return failure
            If CustomerId does not exist for Rev customer type
            Then log exception "Rev customer requires CustomerId" and return failure

Step 3: Validate AMOP Customer Identifier
       If AMOPCustomerId attribute exists in the message
       Then parse the value as an integer
       If the parsed integer is less than or equal to zero
       Then log exception "Invalid AMOP Customer Id" and return failure

Step 4: Ensure Appropriate Customer Type Processing
       If customer type is not Rev and no AMOP Customer ID was found
       Then throw an argument exception for missing required AMOP Customer ID

Step 5: Return Successful Validation
       Return success with the extracted and validated customer identifiers
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
INPUT: SQS Message
OUTPUT: Validation Result (Success/Failure), Billing Period Data

Step 1: Check for Billing Period Information
       Examine the message attributes for billing period identifiers
       Look for either BillPeriodId or both BillYear and BillMonth attributes
       If none of these required billing period identifiers are found
       Then log exception "No Billing Period provided" and return failure

Step 2: Validate Billing Period ID Format
       If BillPeriodId attribute exists in the message
       Then extract the string value from the attribute
       Attempt to parse the string value as an integer
       If the parsing fails or produces an invalid number
       Then log exception "Invalid Billing Period provided" and return failure

Step 3: Verify Billing Period Exists in Database
       Query the database using the parsed billing period ID
       Search for a matching billing period record
       If no billing period record is found in the database
       Then log error "Billing Period not found in database" and return failure

Step 4: Retrieve Associated Service Provider
       Use the billing period ID to lookup the associated service provider
       Query the database to find the service provider linked to this billing period
       If no service provider is found for the billing period
       Then log error "Service Provider not found for billing period" and return failure

Step 5: Return Successful Validation
       Return success with the validated billing period ID, billing period object, and service provider ID
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
INPUT: SQS Message, Customer Type
OUTPUT: Validation Result (Success/Failure), Authentication ID

Step 1: Determine Authentication Requirements
       Check if the customer type is Rev
       If customer type is not Rev (AMOP customers)
       Then integration authentication is not required, return success with null authentication ID

Step 2: Verify Authentication Attribute Presence
       For Rev customers, check if IntegrationAuthenticationId attribute exists in message
       If the IntegrationAuthenticationId attribute is missing
       Then log exception "Integration Authentication Id required for Rev customers" and return failure

Step 3: Validate Authentication ID Format
       Extract the IntegrationAuthenticationId value as a string from the message attributes
       Attempt to parse the string value as an integer
       If the parsing fails or produces an invalid format
       Then log exception "Invalid Integration Authentication Id format" and return failure

Step 4: Validate Authentication ID Value Range
       Check if the parsed authentication ID is a positive number
       If the authentication ID is less than or equal to zero
       Then log exception "Authentication Id must be positive" and return failure

Step 5: Verify Authentication Credentials in System
       Query the authentication system to verify the credentials exist
       Check if the authentication ID corresponds to valid credentials in the system
       If the authentication credentials are not found in the system
       Then log exception "Integration Authentication credentials not found" and return failure

Step 6: Return Successful Validation
       Return success with the validated authentication ID for further processing
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
INPUT: SQS Message, Billing Period ID, Customer ID
OUTPUT: Validation Result (Success/Failure), Service Provider Data

Step 1: Attempt Primary Service Provider Resolution
       Try to extract the service provider ID directly from the message attributes
       If no service provider ID is found in the message and a billing period ID exists
       Then query the database to derive the service provider from the billing period

Step 2: Validate Service Provider Existence
       Check if a valid service provider ID was obtained from either source
       If no service provider ID could be determined
       Then log error "No service provider found for optimization" and return failure

Step 3: Handle Cross-Provider Optimization Mode
       If the system is running in cross-provider optimization mode
       Then check if SERVICE_PROVIDER_IDS attribute exists in the message
            If the attribute exists, parse the comma-separated list of provider IDs
            For each provider ID in the list, verify it exists in the system
            If any provider ID is invalid, log error "Invalid service provider in list" and return failure
            If no specific providers are specified, use all authorized providers for the customer

Step 4: Verify Customer-Provider Association
       Check the authorization system for customer-provider relationships
       Verify that the customer is authorized to use the specified service provider
       If the customer is not associated with the service provider
       Then log error "Customer not associated with service provider" and return failure

Step 5: Return Successful Validation
       Return success with the validated primary service provider ID and any additional provider list
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