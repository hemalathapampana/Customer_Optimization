# DeviceCount Analysis in QueueCustomerOptimization Lambda

## Overview
This document analyzes how `DeviceCount` is handled in the QueueCustomerOptimization Lambda, tracing its origin, flow, and updates throughout the optimization process.

## Initial State in QueueCustomerOptimization Lambda

### 1. Initial DeviceCount Value
**Location**: `AltaworxSimCardCostQueueCustomerOptimization.cs`, lines 105-121

In the QueueCustomerOptimization Lambda, `DeviceCount` is initially set to **0** when creating the `additionalDataObject`:

```csharp
var additionalDataObject = new
{
    data = new
    {
        BillPeriodId = "",
        SiteId = 0,
        ServiceProviderId = "",
        OptimizationType = 0,
        OptimizationFrom = "group",
        BillingPeriodStartDate = "",
        BillingPeriodEndDate = "",
        DeviceCount = 0,  // Initially set to 0
        TenantId = tenantId,
    }
};
```

**Evidence**: Line 117 in `AltaworxSimCardCostQueueCustomerOptimization.cs`

## DeviceCount Calculation and Sources

### 2. DeviceCount Calculation in Cleanup Process
**Location**: `AltaworxSimCardCostOptimizerCleanup.cs`, lines 1750-1765

The actual `DeviceCount` is calculated during the cleanup process by summing:
- **M2M SIM count** (`totalM2MSimCount`)
- **Mobility SIM count** (`totalMobilitySimCount`)

#### For AMOP Customers:
```csharp
var totalM2MSimCount = GetTotalSimCountForAMOPCustomerId(context, instance.AMOPCustomerId.Value, instance.TenantId);
var totalMobilitySimCount = GetTotalMobilitySimCountForAMOPCustomerId(context, instance.AMOPCustomerId.Value, instance.TenantId);
syncResults.DeviceCount = totalM2MSimCount.GetValueOrDefault() + totalMobilitySimCount.GetValueOrDefault();
```

#### For Rev Customers:
```csharp
var totalM2MSimCount = GetTotalSimCountForCustomer(context, customer.RevCustomerId, instance.TenantId);
var totalMobilitySimCount = GetTotalMobilitySimCountForCustomer(context, customer.RevCustomerId, instance.TenantId);
syncResults.DeviceCount = totalM2MSimCount.GetValueOrDefault() + totalMobilitySimCount.GetValueOrDefault();
```

**Evidence**: Lines 1750-1765 in `AltaworxSimCardCostOptimizerCleanup.cs`

### 3. Database Sources for DeviceCount

#### M2M Device Count Sources:
1. **For AMOP Customers**: 
   - **Stored Procedure**: `dbo.usp_Device_GetTotalSimCountBySiteId`
   - **Parameter**: `@SiteIds` (AMOP Customer ID)
   - **Location**: Lines 1596-1597 in `AltaworxSimCardCostOptimizerCleanup.cs`

2. **For Rev Customers**:
   - **Stored Procedure**: `dbo.usp_Device_GetTotalSimCountByAccountNumber`
   - **Parameter**: `@RevCustomerNumbers` (Rev Account Number)
   - **Location**: Lines 1520-1521 in `AltaworxSimCardCostOptimizerCleanup.cs`

#### Mobility Device Count Sources:
1. **For AMOP Customers**:
   - **Stored Procedure**: `dbo.usp_MobilityDevice_GetTotalSimCountBySiteId`
   - **Parameter**: `@SiteIds` (AMOP Customer ID)
   - **Location**: Lines 1619-1620 in `AltaworxSimCardCostOptimizerCleanup.cs`

2. **For Rev Customers**:
   - **Stored Procedure**: `dbo.usp_MobilityDevice_GetTotalSimCountByAccountNumber`
   - **Parameter**: `@RevCustomerNumber` (Rev Account Number)
   - **Location**: Lines 1543-1544 in `AltaworxSimCardCostOptimizerCleanup.cs`

## DeviceCount Updates and Storage

### 4. Database Update Process
**Location**: `AltaworxSimCardCostOptimizerCleanup.cs`, lines 1777-1805

The calculated `DeviceCount` is updated in the `OptimizationCustomerProcessing` table:

```csharp
private void UpdateOptCustomerProcessing(KeySysLambdaContext context, string customerId, DateTime endTime, int deviceCount, int serviceProviderId, SiteTypes siteType, OptimizationInstance instance)
{
    var query = @"UPDATE [OptimizationCustomerProcessing]
                    SET [DeviceCount] = @deviceCount,
                        [IsProcessed] = @isProcessing,
                        [EndTime] = @endTime,
                        [InstanceId] = @instanceId
                    WHERE {0}
                    AND [ServiceProviderId] = @serviceProviderId
                    AND [SessionId] = @sessionId";
    
    // ... SQL execution with @deviceCount parameter
    cmd.Parameters.AddWithValue("@deviceCount", deviceCount);
}
```

**Evidence**: Lines 1782-1805 in `AltaworxSimCardCostOptimizerCleanup.cs`

### 5. DeviceCount Retrieval for API Responses
**Location**: `OptimizationApiController.cs`, lines 2014-2025

The `DeviceCount` is retrieved from the `vwOptimizationInstances` view for API responses:

```csharp
public int GetOptimizationDeviceCountByInstance(int optimizationSessionId)
{
    int deviceCount = 0;
    try
    {
        deviceCount = altaWrxDb.vwOptimizationInstances
            .Where(os => os.OptimizationSessionId == optimizationSessionId)
            .Sum(x => x.DeviceCount ?? 0);
    }
    catch (Exception ex)
    {
        Log.Error("Exception - " + ex.Message.ToString());
    }
    return deviceCount;
}
```

**Evidence**: Lines 2018-2019 in `OptimizationApiController.cs`

## DeviceCount Usage and Flow

### 6. Initial API Request Flow
**Location**: `OptimizationApiController.cs`, lines 140-150

When an optimization request is made via API, `DeviceCount` comes from the request object:

```csharp
var additionalDataObject = new
{
    data = new
    {
        // ... other properties
        DeviceCount = request.DeviceCount,  // From API request
        TenantId = xTenantId,
    }
};
```

**Evidence**: Lines 146-147 in `OptimizationApiController.cs`

### 7. Progress Reporting to AMOP 2.0
**Location**: `OptimizationApiController.cs`, lines 2028-2053

The `DeviceCount` is sent to AMOP 2.0 for progress reporting:

```csharp
public static async Task SendResponseToAMOP20(string jobName, string optimizationSessionId, string optimizationSessionGuid, int deviceCount, string errorMessage = null, int progress = 0, string customerId = null, string additionalJson = null)
{
    var requestData = new
    {
        data = new
        {
            // ... other properties
            DeviceCount = deviceCount  // Sent to AMOP 2.0
        }
    };
}
```

**Evidence**: Lines 2051-2052 in `OptimizationApiController.cs`

## Summary of DeviceCount Flow

1. **Initial State**: `DeviceCount` starts as **0** in the QueueCustomerOptimization Lambda
2. **Source Calculation**: Actual count is calculated during cleanup by summing M2M and Mobility SIM counts from database stored procedures
3. **Database Update**: Final count is stored in `OptimizationCustomerProcessing` table
4. **Retrieval**: Count is retrieved from `vwOptimizationInstances` view for API responses
5. **Reporting**: Count is sent to AMOP 2.0 for progress tracking

## Key Database Objects

- **Tables**: `OptimizationCustomerProcessing`
- **Views**: `vwOptimizationInstances`
- **Stored Procedures**:
  - `dbo.usp_Device_GetTotalSimCountBySiteId`
  - `dbo.usp_Device_GetTotalSimCountByAccountNumber`
  - `dbo.usp_MobilityDevice_GetTotalSimCountBySiteId`
  - `dbo.usp_MobilityDevice_GetTotalSimCountByAccountNumber`

## Customer Type Handling

The system handles two customer types differently:
- **AMOP Customers**: Uses Site ID for device counting
- **Rev Customers**: Uses Account Number for device counting

Both types aggregate M2M and Mobility device counts to get the final `DeviceCount`.