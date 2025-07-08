# Customer Identification Validation - 5 Lines Algorithm

## Algorithm
```
1. Check message contains CustomerId OR AMOPCustomerId attributes
2. Parse CustomerId as GUID if customer type is Rev 
3. Validate GUID is not empty for Rev customers
4. Parse AMOPCustomerId as integer if present
5. Throw exception if no valid customer ID found
```

## Code Location

### File: `AltaworxSimCardCostQueueCustomerOptimization.cs`

**Line 147-151: Check Customer ID Exists**
```csharp
if (!message.MessageAttributes.ContainsKey("CustomerId") && !message.MessageAttributes.ContainsKey("AMOPCustomerId"))
{
    LogInfo(context, "EXCEPTION", "No Customer Id provided in message");
    return;
}
```

**Line 158-161: Parse CustomerId**
```csharp
if (message.MessageAttributes.ContainsKey("CustomerId"))
{
    customerId = Guid.Parse(message.MessageAttributes["CustomerId"].StringValue);
}
```

**Line 162-166: Validate Rev Customer GUID**
```csharp
if (customerType == SiteTypes.Rev && (string.IsNullOrEmpty(customerId.ToString()) || customerId == Guid.Empty))
{
    LogInfo(context, "EXCEPTION", "Blank Customer Id provided in message");
    return;
}
```

**Line 168-172: Parse AMOP Customer ID**
```csharp
int? amopCustomerId = null;
if (message.MessageAttributes.ContainsKey(SQSMessageKeyConstant.AMOP_CUSTOMER_ID))
{
    amopCustomerId = int.Parse(message.MessageAttributes[SQSMessageKeyConstant.AMOP_CUSTOMER_ID].StringValue);
}
```

**Line 194: Validate AMOP Customer ID**
```csharp
ArgumentNullException.ThrowIfNull(amopCustomerId);
```