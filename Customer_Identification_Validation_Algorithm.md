# Customer Identification Validation Algorithm

## Algorithm
**ALGORITHM: ValidateCustomerIdentification**  
**INPUT:** SQS Message, Customer Type  
**OUTPUT:** Validation Result (Success/Failure), Customer Data  

**Step 1: Check for Required Customer Identifiers**  
Examine the incoming message for either CustomerId or AMOPCustomerId attributes  
If neither identifier is present in the message attributes  
Then log exception "No Customer Id provided" and return failure  

**Step 2: Validate Rev Customer Requirements**  
If the customer type is Rev  
Then check if CustomerId attribute exists in message  
     If CustomerId exists, parse it as a GUID format  
     If the parsed GUID is empty or null  
     Then log exception "Blank Customer Id provided" and return failure  
     If CustomerId does not exist for Rev customer type  
     Then log exception "Rev customer requires CustomerId" and return failure  

**Step 3: Validate AMOP Customer Identifier**  
If AMOPCustomerId attribute exists in the message  
Then parse the value as an integer  
If the parsed integer is less than or equal to zero  
Then log exception "Invalid AMOP Customer Id" and return failure  

**Step 4: Ensure Appropriate Customer Type Processing**  
If customer type is not Rev and no AMOP Customer ID was found  
Then throw an argument exception for missing required AMOP Customer ID  

**Step 5: Return Successful Validation**  
Return success with the extracted and validated customer identifiers  

## Code Location

### File: `AltaworxSimCardCostQueueCustomerOptimization.cs`

**Lines 147-151: Initial Customer ID Validation**
```csharp
if (!message.MessageAttributes.ContainsKey("CustomerId") && !message.MessageAttributes.ContainsKey("AMOPCustomerId"))
{
    LogInfo(context, "EXCEPTION", "No Customer Id provided in message");
    return;
}
```

**Lines 158-161: Rev Customer ID Extraction**
```csharp
if (message.MessageAttributes.ContainsKey("CustomerId"))
{
    customerId = Guid.Parse(message.MessageAttributes["CustomerId"].StringValue);
}
```

**Lines 162-166: Rev Customer ID Validation**
```csharp
if (customerType == SiteTypes.Rev && (string.IsNullOrEmpty(customerId.ToString()) || customerId == Guid.Empty))
{
    LogInfo(context, "EXCEPTION", "Blank Customer Id provided in message");
    return;
}
```

**Lines 168-172: AMOP Customer ID Extraction**
```csharp
int? amopCustomerId = null;
if (message.MessageAttributes.ContainsKey(SQSMessageKeyConstant.AMOP_CUSTOMER_ID))
{
    amopCustomerId = int.Parse(message.MessageAttributes[SQSMessageKeyConstant.AMOP_CUSTOMER_ID].StringValue);
}
```

**Lines 185-198: Customer Type Processing Logic**
```csharp
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

**Lines 210-217: Cross-Provider Customer ID Validation**
```csharp
var customerIdentifier = 0;
if (message.MessageAttributes.ContainsKey(SQSMessageKeyConstant.AMOP_CUSTOMER_ID))
{
    customerIdentifier = int.Parse(message.MessageAttributes[SQSMessageKeyConstant.AMOP_CUSTOMER_ID].StringValue);
}
else
{
    LogInfo(context, CommonConstants.ERROR, $"No Customer Id found. Stopping Cross-Provider Customer Optimization.");
}
```

**Line 194: AMOP Customer ID Null Check**
```csharp
ArgumentNullException.ThrowIfNull(amopCustomerId);
```