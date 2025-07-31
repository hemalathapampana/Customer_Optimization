# Device Count Analysis - OptimizationApiController.cs

## Overview
This document provides a comprehensive analysis of how `deviceCount` is retrieved and used in the `OptimizationApiController.cs`, particularly in the `StartConfirm` method's while loop that monitors optimization progress.

## Key Question Answered
**Where does the `deviceCount` in the OptimizationApiController.cs while loop come from when the lambda starts processing?**

The `deviceCount` comes from the `vwOptimizationInstances` database view, which is populated by the QueueCustomerOptimization lambda during the cleanup process.

## Complete Flow Analysis

### 1. Initial Request in OptimizationApiController.cs

#### StartConfirm Method (Lines 106-272)
```csharp:237-238
deviceCount = GetOptimizationDeviceCountByInstance(Convert.ToInt32(optimizationSessionId));
Log.Info($"Device Count: " + deviceCount);
```

The while loop calls `GetOptimizationDeviceCountByInstance` to retrieve the current device count.

#### GetOptimizationDeviceCountByInstance Method (Lines 2014-2026)
```csharp:2018-2019
deviceCount = altaWrxDb.vwOptimizationInstances.Where(os => os.OptimizationSessionId == optimizationSessionId)
    .Sum(x => x.DeviceCount ?? 0);
```

**Source Table/View**: `vwOptimizationInstances`
- **Field**: `DeviceCount`
- **Query**: Sums all DeviceCount values for instances matching the optimizationSessionId

### 2. Lambda Processing - QueueCustomerOptimization

#### File: AltaworxSimCardCostQueueCustomerOptimization.cs

The QueueCustomerOptimization lambda processes optimization requests and creates optimization instances, but the device count is not immediately populated during this stage.

### 3. Lambda Processing - OptimizerCleanup (Where DeviceCount is Calculated)

#### File: AltaworxSimCardCostOptimizerCleanup.cs

The device count is calculated and stored during the cleanup process after optimization completion.

#### Device Count Calculation Methods:

**For Rev Customers (Lines 1512-1533):**
```csharp:1520-1523
cmd.CommandText = "dbo.usp_Device_GetTotalSimCountByAccountNumber";
cmd.Parameters.AddWithValue("@RevCustomerNumbers", revAccountNumber);
cmd.Parameters.AddWithValue("@TenantId", tenantId);
```

**For Rev Customers - Mobility (Lines 1535-1556):**
```csharp:1543-1546
cmd.CommandText = "dbo.usp_MobilityDevice_GetTotalSimCountByAccountNumber";
cmd.Parameters.AddWithValue("@RevCustomerNumber", revAccountNumber);
cmd.Parameters.AddWithValue("@TenantId", tenantId);
```

**For AMOP Customers (Lines 1588-1609):**
```csharp:1596-1599
cmd.CommandText = "dbo.usp_Device_GetTotalSimCountBySiteId";
cmd.Parameters.AddWithValue("@SiteIds", amopCustomerId.ToString());
cmd.Parameters.AddWithValue("@TenantId", tenantId);
```

**For AMOP Customers - Mobility (Lines 1611-1632):**
```csharp:1619-1622
cmd.CommandText = "dbo.usp_MobilityDevice_GetTotalSimCountBySiteId";
cmd.Parameters.AddWithValue("@SiteIds", amopCustomerId.ToString());
cmd.Parameters.AddWithValue("@TenantId", tenantId);
```

#### Device Count Update Process (Lines 1750-1768):

```csharp:1750-1753
var totalM2MSimCount = GetTotalSimCountForAMOPCustomerId(context, instance.AMOPCustomerId.Value, instance.TenantId);
var totalMobilitySimCount = GetTotalMobilitySimCountForAMOPCustomerId(context, instance.AMOPCustomerId.Value, instance.TenantId);
syncResults.DeviceCount = totalM2MSimCount.GetValueOrDefault() + totalMobilitySimCount.GetValueOrDefault();
```

The device count is the sum of:
- **M2M SIM Count** (from Device table)
- **Mobility SIM Count** (from MobilityDevice table)

#### Database Update (Lines 1778-1820):

```csharp:1782-1806
var query = @"UPDATE [OptimizationCustomerProcessing]
                SET [DeviceCount] = @deviceCount,
                    [IsProcessed] = @isProcessing,
                    [EndTime] = @endTime,
                    [InstanceId] = @instanceId
                WHERE {0}
                AND [ServiceProviderId] = @serviceProviderId
                AND [SessionId] = @sessionId";
```

**Target Table**: `OptimizationCustomerProcessing`
- **Field Updated**: `DeviceCount`
- **When**: During cleanup process after optimization completion

## Database Tables and Views Involved

### Primary Tables for Device Count Calculation:

1. **Device** - Contains M2M devices/SIMs
   - Accessed via: `usp_Device_GetTotalSimCountByAccountNumber`
   - Accessed via: `usp_Device_GetTotalSimCountBySiteId`

2. **MobilityDevice** - Contains Mobility devices/SIMs
   - Accessed via: `usp_MobilityDevice_GetTotalSimCountByAccountNumber`
   - Accessed via: `usp_MobilityDevice_GetTotalSimCountBySiteId`

3. **OptimizationCustomerProcessing** - Stores processing status and device count
   - **Fields**: `DeviceCount`, `IsProcessed`, `EndTime`, `InstanceId`

### Views:

4. **vwOptimizationInstances** - View that aggregates optimization instance data
   - **Field**: `DeviceCount`
   - **Purpose**: Used by the controller to retrieve current device count during monitoring

## Process Timeline

```
1. User triggers optimization (StartConfirm)
   ↓
2. Optimization session created
   ↓
3. QueueCustomerOptimization lambda processes request
   ↓
4. Optimization algorithm runs
   ↓
5. OptimizerCleanup lambda executes
   ↓
6. Device count calculated from Device + MobilityDevice tables
   ↓
7. OptimizationCustomerProcessing table updated with DeviceCount
   ↓
8. vwOptimizationInstances view reflects updated device count
   ↓
9. Controller's while loop reads from vwOptimizationInstances
```

## Key Stored Procedures for Device Count

| Stored Procedure | Purpose | Parameters |
|------------------|---------|------------|
| `usp_Device_GetTotalSimCountByAccountNumber` | M2M devices by Rev account | @RevCustomerNumbers, @TenantId |
| `usp_MobilityDevice_GetTotalSimCountByAccountNumber` | Mobility devices by Rev account | @RevCustomerNumber, @TenantId |
| `usp_Device_GetTotalSimCountBySiteId` | M2M devices by AMOP site | @SiteIds, @TenantId |
| `usp_MobilityDevice_GetTotalSimCountBySiteId` | Mobility devices by AMOP site | @SiteIds, @TenantId |

## Code Evidence Summary

### Controller Code (OptimizationApiController.cs):
- **Line 237**: `deviceCount = GetOptimizationDeviceCountByInstance(Convert.ToInt32(optimizationSessionId));`
- **Line 2018**: `deviceCount = altaWrxDb.vwOptimizationInstances.Where(os => os.OptimizationSessionId == optimizationSessionId).Sum(x => x.DeviceCount ?? 0);`

### Lambda Code (AltaworxSimCardCostOptimizerCleanup.cs):
- **Lines 1750-1753**: Device count calculation (M2M + Mobility)
- **Line 1755**: `UpdateOptCustomerProcessing(context, amopCustomerId.ToString(), DateTime.UtcNow, (int)syncResults.DeviceCount, serviceProviderId, SiteTypes.AMOP, instance);`
- **Lines 1782-1806**: Database update query for OptimizationCustomerProcessing table

## Conclusion

The `deviceCount` in the OptimizationApiController.cs while loop comes from the `vwOptimizationInstances` database view. This view gets its data from the `OptimizationCustomerProcessing` table, which is populated by the OptimizerCleanup lambda after it calculates the total device count by querying the Device and MobilityDevice tables using specific stored procedures based on customer type (Rev vs AMOP).

The device count represents the total number of SIM cards/devices (both M2M and Mobility) associated with the customer being optimized, and it's updated only after the optimization processing is complete during the cleanup phase.