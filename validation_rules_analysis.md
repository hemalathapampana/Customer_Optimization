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
INPUT: M = SQS Message, T = Customer Type
OUTPUT: (Valid, CustomerData) where Valid ∈ {True, False}

Step 1: Presence Verification
       Let A = {CustomerId ∈ M.attributes}
       Let B = {AMOPCustomerId ∈ M.attributes}
       If A ∪ B = ∅, then Return (False, ∅)

Step 2: Rev Customer Validation  
       If T = Rev, then:
           If A ≠ ∅, then:
               Parse CustomerId → GUID format
               If GUID = Empty ∨ GUID = NULL, then Return (False, ∅)
           Else Return (False, ∅)

Step 3: AMOP Customer Validation
       If B ≠ ∅, then:
           Parse AMOPCustomerId → Integer format  
           If Integer ≤ 0, then Return (False, ∅)

Step 4: Type-Specific Requirements
       If T ≠ Rev ∧ B = ∅, then Throw Exception
       
Step 5: Success Case
       Return (True, {ParsedCustomerId, ParsedAMOPCustomerId})
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
INPUT: M = SQS Message
OUTPUT: (Valid, BillingData) where Valid ∈ {True, False}

Step 1: Attribute Set Definition
       Let P = {BillPeriodId ∈ M.attributes}
       Let Y = {BillYear ∈ M.attributes}  
       Let M₀ = {BillMonth ∈ M.attributes}
       If P ∪ (Y ∩ M₀) = ∅, then Return (False, ∅)

Step 2: Period ID Validation
       If P ≠ ∅, then:
           Extract BillPeriodId value → String S
           Parse S → Integer I
           If Parse(S) fails, then Return (False, ∅)

Step 3: Database Existence Verification  
       Let DB = Database billing period records
       If ∄ record ∈ DB where record.id = I, then Return (False, ∅)

Step 4: Service Provider Association
       Let SP = ServiceProvider(I)
       If SP = ∅, then Return (False, ∅)

Step 5: Success Case
       Return (True, {I, BillingPeriod(I), SP})
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
INPUT: M = SQS Message, T = Customer Type  
OUTPUT: (Valid, AuthID) where Valid ∈ {True, False}, AuthID ∈ ℕ ∪ {∅}

Step 1: Type-Based Requirement Check
       If T ≠ Rev, then Return (True, ∅)

Step 2: Authentication Attribute Verification
       Let A = {IntegrationAuthenticationId ∈ M.attributes}
       If A = ∅, then Return (False, ∅)

Step 3: Format Validation  
       Extract IntegrationAuthenticationId value → String S
       Parse S → Integer I
       If Parse(S) fails, then Return (False, ∅)

Step 4: Value Range Validation
       If I ≤ 0, then Return (False, ∅)

Step 5: System Existence Verification
       Let AuthSystem = Set of valid authentication records
       If I ∉ AuthSystem, then Return (False, ∅)

Step 6: Success Case
       Return (True, I)
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
INPUT: M = SQS Message, B = Billing Period ID, C = Customer ID
OUTPUT: (Valid, ProviderData) where Valid ∈ {True, False}

Step 1: Primary Provider Resolution
       Let P₁ = ExtractServiceProvider(M.attributes)
       If P₁ = ∅ ∧ B ≠ ∅, then P₁ = ServiceProvider(B)

Step 2: Provider Existence Validation  
       If P₁ = ∅, then Return (False, ∅)

Step 3: Cross-Provider Mode Handling
       If CrossProviderMode = True, then:
           Let S = {SERVICE_PROVIDER_IDS ∈ M.attributes}
           If S ≠ ∅, then:
               Parse S → Set P = {p₁, p₂, ..., pₙ}
               For each pᵢ ∈ P: If pᵢ ∉ ValidProviders, then Return (False, ∅)
           Else P = AllAuthorizedProviders

Step 4: Customer-Provider Association Verification
       Let Associations = {(c,p) | customer c authorized for provider p}
       If (C, P₁) ∉ Associations, then Return (False, ∅)

Step 5: Success Case  
       Return (True, {P₁, P})
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