# Customer Type Detection Analysis

## Overview
The Altaworx SimCard Cost Optimization system uses a customer type detection mechanism to distinguish between two types of customers: **Rev Customers** and **AMOP Customers**. This detection drives different authentication methods, identification systems, and processing workflows.

## Customer Type Definitions

### Rev Customers
- **Identification**: GUID-based customer identification (`Guid customerId`)
- **Authentication**: Integration authentication with `IntegrationAuthenticationId` 
- **Account Management**: Uses `RevAccountNumber` for account identification
- **Processing**: Requires full customer ID validation and integration authentication

### AMOP Customers  
- **Identification**: Integer-based customer identification (`int AMOPCustomerId`)
- **Authentication**: Simplified authentication (no integration authentication required)
- **Account Management**: Uses integer customer ID directly
- **Processing**: Streamlined workflow with fewer authentication requirements

## Why Different Customer Types?

### Historical Context
The system appears to support legacy Rev customers while transitioning to a newer AMOP (likely "Altaworx Mobile Operations Platform") system:

1. **Rev Customers** represent the legacy system with:
   - Complex GUID-based identification
   - Required integration authentication layers
   - Account number-based billing systems

2. **AMOP Customers** represent the modernized system with:
   - Simplified integer IDs
   - Streamlined authentication
   - Direct customer ID management

### Business Benefits
- **Backward Compatibility**: Maintains support for existing Rev customers
- **Migration Path**: Allows gradual transition to AMOP system
- **Simplified Management**: AMOP provides easier customer management
- **Authentication Efficiency**: Reduces authentication overhead for AMOP customers

## How Customer Type Detection Works

### Algorithmic Detection Process

The customer type detection follows this algorithm:

```
1. READ SQS Message Attributes
2. EXTRACT CustomerType from message.MessageAttributes["CustomerType"]
3. CONVERT to SiteTypes enum: SiteTypes.Rev OR SiteTypes.AMOP
4. BRANCH Processing:
   IF customerType == SiteTypes.Rev:
       - Require CustomerId (GUID)
       - Require IntegrationAuthenticationId
       - Call ProcessCustomerId()
   ELSE (AMOP):
       - Require AMOPCustomerId (Integer)
       - No integration authentication needed
       - Call ProcessAMOPCustomerId()
```

### Detection Logic Flow

```mermaid
graph TD
    A[SQS Message Received] --> B[Extract CustomerType Attribute]
    B --> C{CustomerType == SiteTypes.Rev?}
    C -->|Yes| D[Extract CustomerId (GUID)]
    C -->|No| E[Extract AMOPCustomerId (Integer)]
    D --> F[Validate GUID != Empty]
    E --> G[Validate Integer != null]
    F --> H[Extract IntegrationAuthenticationId]
    G --> I[No Auth Required]
    H --> J[ProcessCustomerId Method]
    I --> K[ProcessAMOPCustomerId Method]
    J --> L[Use RevAccountNumber]
    K --> M[Use AMOPCustomerId directly]
```

## Code Locations and Implementation

### Primary Detection Logic
**File**: `AltaworxSimCardCostQueueCustomerOptimization.cs`

#### 1. Customer Type Extraction
```csharp
// Lines 102-103
SiteTypes customerType = (SiteTypes)int.Parse(message.MessageAttributes["CustomerType"].StringValue);
```

#### 2. Customer Type Validation and Branching  
```csharp
// Lines 188-196
if (customerType == SiteTypes.Rev)
{
    var integrationAuthenticationId = int.Parse(message.MessageAttributes["IntegrationAuthenticationId"].StringValue);
    await ProcessCustomerId(context, tenantId, customerId, serviceProviderId, billingPeriodId, messageId, integrationAuthenticationId, optimizationSessionId, usesProration, isLastInstance, customerType, additionalData);
}
else
{
    ArgumentNullException.ThrowIfNull(amopCustomerId);
    await ProcessAMOPCustomerId(context, tenantId, customerType, amopCustomerId.Value, serviceProviderId, billingPeriodId, messageId, optimizationSessionId, usesProration, isLastInstance, additionalData);
}
```

#### 3. Rev Customer ID Validation
```csharp
// Lines 157-164
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

#### 4. AMOP Customer ID Extraction
```csharp
// Lines 168-172
int? amopCustomerId = null;
if (message.MessageAttributes.ContainsKey(SQSMessageKeyConstant.AMOP_CUSTOMER_ID))
{
    amopCustomerId = int.Parse(message.MessageAttributes[SQSMessageKeyConstant.AMOP_CUSTOMER_ID].StringValue);
}
```

### Processing Methods

#### Rev Customer Processing
**Method**: `ProcessCustomerId()` (Lines 275-394)
- **Parameters**: `Guid customerId`, `int integrationAuthenticationId`
- **Account Resolution**: `GetRevAccountNumber(context, customerId)`
- **Rate Plans**: `GetCustomerRatePlans(context, customerId, billingPeriodId, serviceProviderId, tenantId)`

#### AMOP Customer Processing  
**Method**: `ProcessAMOPCustomerId()` (Lines 396-508)
- **Parameters**: `int AMOPCustomerId`
- **No Account Resolution**: Uses AMOPCustomerId directly
- **Rate Plans**: `GetCustomerRatePlans(context, Guid.Empty, billingPeriodId, serviceProviderId, tenantId, customerType, AMOPCustomerId)`

### Supporting Infrastructure

#### Enum Definition Usage
**File**: `AltaworxSimCardCostOptimizerCleanup.cs`
- Lines 1755, 1767: Shows `SiteTypes.AMOP` and `SiteTypes.Rev` enum values
- Line 2056: Customer type checking: `if (instance.CustomerType == SiteTypes.AMOP)`

#### Cross-Provider Support
**File**: `AltaworxSimCardCostQueueCustomerOptimization.cs`
- Lines 205-248: `ProcessCrossProviderCustomerOptimization()` method
- Line 687: `GetOptimizationCustomer(context, customerId, customerType)`

## Error Handling

### Rev Customer Errors
- Missing CustomerId: "No Customer Id provided in message"
- Empty GUID: "Blank Customer Id provided in message"  
- Missing Integration Auth: Expects `IntegrationAuthenticationId` in message attributes

### AMOP Customer Errors
- Missing AMOPCustomerId: `ArgumentNullException.ThrowIfNull(amopCustomerId)`
- No additional authentication validation required

## Integration Points

### Message Queue Integration
- **SQS Message Attributes**: Customer type passed via `CustomerType` attribute
- **Portal Types**: System supports `PortalTypes.M2M` and `PortalTypes.CrossProvider`
- **Optimization Sessions**: Both customer types support optimization session tracking

### Database Integration
- **Rev Customers**: Query using GUID-based customer IDs
- **AMOP Customers**: Query using integer-based customer IDs
- **Shared Tables**: Both types use same optimization result tables with different ID columns

### API Integration
- **AMOP 2.0 API**: `OptimizationAmopApiTrigger.SendResponseToAMOP20()` for both customer types
- **Error Notifications**: Different notification patterns based on customer type

## Performance Considerations

### Rev Customers
- **Higher Overhead**: GUID parsing and validation
- **Authentication Complexity**: Integration authentication required
- **Account Resolution**: Additional database lookup for account numbers

### AMOP Customers  
- **Lower Overhead**: Simple integer ID handling
- **Streamlined Processing**: Direct customer ID usage
- **Faster Authentication**: No integration authentication step

## Future Considerations

The system appears designed to support migration from Rev to AMOP customers:
- Dual processing paths maintain compatibility
- AMOP path is optimized for better performance
- Cross-provider optimization supports both customer types
- Infrastructure allows for eventual Rev customer migration to AMOP