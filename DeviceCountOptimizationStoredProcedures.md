# Device Count Retrieval During Optimization - Stored Procedures and Code Evidence

## Overview
This document provides evidence of how device count is retrieved during optimization processes and includes an optimized stored procedure for better performance.

## Current Implementation Analysis

### 1. Primary Device Count Retrieval Method

**Location**: `OptimizationApiController.cs` (Lines 2014-2026)

```csharp
public int GetOptimizationDeviceCountByInstance(int optimizationSessionId)
{
    int deviceCount = 0;
    try
    {
        deviceCount = altaWrxDb.vwOptimizationInstances.Where(os => os.OptimizationSessionId == optimizationSessionId).Sum(x => x.DeviceCount ?? 0);
    }
    catch (Exception ex)
    {
        Log.Error("Exception - " + ex.Message.ToString());
    }
    return deviceCount;
}
```

**Evidence**: This method queries the `vwOptimizationInstances` view to sum device counts for a specific optimization session.

### 2. Existing Device Count Stored Procedures

#### A. M2M Device Count by Account Number
**Location**: `AltaworxSimCardCostOptimizerCleanup.cs` (Lines 1520-1526)

```csharp
cmd.CommandType = CommandType.StoredProcedure;
cmd.CommandText = "dbo.usp_Device_GetTotalSimCountByAccountNumber";
cmd.Parameters.AddWithValue("@RevCustomerNumbers", revAccountNumber);
cmd.Parameters.AddWithValue("@TenantId", tenantId);
result = (Int32)cmd.ExecuteScalar();
```

#### B. Mobility Device Count by Account Number
**Location**: `AltaworxSimCardCostOptimizerCleanup.cs` (Lines 1543-1549)

```csharp
cmd.CommandType = CommandType.StoredProcedure;
cmd.CommandText = "dbo.usp_MobilityDevice_GetTotalSimCountByAccountNumber";
cmd.Parameters.AddWithValue("@RevCustomerNumber", revAccountNumber);
cmd.Parameters.AddWithValue("@TenantId", tenantId);
result = (Int32)cmd.ExecuteScalar();
```

#### C. M2M Device Count by Site ID
**Location**: `AltaworxSimCardCostOptimizerCleanup.cs` (Lines 1596-1602)

```csharp
cmd.CommandType = CommandType.StoredProcedure;
cmd.CommandText = "dbo.usp_Device_GetTotalSimCountBySiteId";
cmd.Parameters.AddWithValue("@SiteIds", amopCustomerId.ToString());
cmd.Parameters.AddWithValue("@TenantId", tenantId);
result = (Int32)cmd.ExecuteScalar();
```

#### D. Mobility Device Count by Site ID
**Location**: `AltaworxSimCardCostOptimizerCleanup.cs` (Lines 1620-1625)

```csharp
cmd.CommandType = CommandType.StoredProcedure;
cmd.CommandText = "dbo.usp_MobilityDevice_GetTotalSimCountBySiteId";
cmd.Parameters.AddWithValue("@SiteIds", amopCustomerId.ToString());
cmd.Parameters.AddWithValue("@TenantId", tenantId);
result = (Int32)cmd.ExecuteScalar();
```

### 3. Device Count Update During Optimization

**Location**: `AltaworxSimCardCostOptimizerCleanup.cs` (Lines 1782-1789)

```csharp
var query = @"UPDATE [OptimizationCustomerProcessing]
                SET [DeviceCount] = @deviceCount,
                    [IsProcessed] = @isProcessing,
                    [EndTime] = @endTime,
                    [InstanceId] = @instanceId
                WHERE {0}
                AND [ServiceProviderId] = @serviceProviderId
                AND [SessionId] = @sessionId";
```

**Evidence**: Device count is stored in the `OptimizationCustomerProcessing` table during optimization execution.

### 4. Device Count Calculation Logic

**Location**: `AltaworxSimCardCostOptimizerCleanup.cs` (Lines 1715, 1726, 1752, 1764)

```csharp
syncResults.DeviceCount = totalM2MSimCount.GetValueOrDefault() + totalMobilitySimCount.GetValueOrDefault();
```

**Evidence**: The total device count is calculated by combining M2M and Mobility device counts.

## Optimized Stored Procedure

### usp_GetOptimizationDeviceCountOptimized

```sql
CREATE PROCEDURE [dbo].[usp_GetOptimizationDeviceCountOptimized]
    @OptimizationSessionId BIGINT,
    @TenantId INT = NULL,
    @IncludeInstanceDetails BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @DeviceCount INT = 0;
    DECLARE @ErrorMessage NVARCHAR(MAX);
    
    BEGIN TRY
        -- If detailed instance information is requested
        IF @IncludeInstanceDetails = 1
        BEGIN
            SELECT 
                oi.Id AS InstanceId,
                oi.OptimizationSessionId,
                oi.ServiceProviderId,
                oi.DeviceCount,
                oi.PortalType,
                oi.IsCustomerOptimization,
                sp.Name AS ServiceProviderName,
                os.Name AS OptimizationStatus,
                oi.StartTime,
                oi.EndTime
            FROM vwOptimizationInstances oi
            LEFT JOIN ServiceProvider sp ON oi.ServiceProviderId = sp.Id
            LEFT JOIN OptimizationStatus os ON oi.OptimizationStatusId = os.Id
            WHERE oi.OptimizationSessionId = @OptimizationSessionId
                AND (@TenantId IS NULL OR oi.TenantId = @TenantId)
            ORDER BY oi.Id;
        END
        
        -- Get total device count with optimized query
        SELECT @DeviceCount = ISNULL(SUM(ISNULL(oi.DeviceCount, 0)), 0)
        FROM vwOptimizationInstances oi WITH (NOLOCK)
        WHERE oi.OptimizationSessionId = @OptimizationSessionId
            AND (@TenantId IS NULL OR oi.TenantId = @TenantId);
            
        -- Return total device count
        SELECT @DeviceCount AS TotalDeviceCount;
        
        -- Return success status
        SELECT 'SUCCESS' AS Status, NULL AS ErrorMessage;
        
    END TRY
    BEGIN CATCH
        SET @ErrorMessage = ERROR_MESSAGE();
        
        -- Return error information
        SELECT 0 AS TotalDeviceCount;
        SELECT 'ERROR' AS Status, @ErrorMessage AS ErrorMessage;
        
        -- Log error (if logging table exists)
        -- INSERT INTO ErrorLog (Procedure, ErrorMessage, ErrorDate) 
        -- VALUES ('usp_GetOptimizationDeviceCountOptimized', @ErrorMessage, GETUTCDATE());
        
    END CATCH
END
```

### Enhanced Version with Performance Monitoring

```sql
CREATE PROCEDURE [dbo].[usp_GetOptimizationDeviceCountWithMetrics]
    @OptimizationSessionId BIGINT,
    @TenantId INT = NULL,
    @PortalType INT = NULL,  -- 1 = M2M, 2 = Mobility
    @IncludeBreakdown BIT = 0
AS
BEGIN
    SET NOCOUNT ON;
    
    DECLARE @StartTime DATETIME2 = GETUTCDATE();
    DECLARE @DeviceCount INT = 0;
    DECLARE @M2MCount INT = 0;
    DECLARE @MobilityCount INT = 0;
    DECLARE @ErrorMessage NVARCHAR(MAX);
    
    BEGIN TRY
        -- Get device count breakdown by portal type
        SELECT 
            @M2MCount = ISNULL(SUM(CASE WHEN oi.PortalType = 1 THEN ISNULL(oi.DeviceCount, 0) ELSE 0 END), 0),
            @MobilityCount = ISNULL(SUM(CASE WHEN oi.PortalType = 2 THEN ISNULL(oi.DeviceCount, 0) ELSE 0 END), 0),
            @DeviceCount = ISNULL(SUM(ISNULL(oi.DeviceCount, 0)), 0)
        FROM vwOptimizationInstances oi WITH (NOLOCK)
        WHERE oi.OptimizationSessionId = @OptimizationSessionId
            AND (@TenantId IS NULL OR oi.TenantId = @TenantId)
            AND (@PortalType IS NULL OR oi.PortalType = @PortalType);
        
        -- Return main result
        SELECT @DeviceCount AS TotalDeviceCount;
        
        -- Return breakdown if requested
        IF @IncludeBreakdown = 1
        BEGIN
            SELECT 
                @M2MCount AS M2MDeviceCount,
                @MobilityCount AS MobilityDeviceCount,
                @DeviceCount AS TotalDeviceCount,
                DATEDIFF(MILLISECOND, @StartTime, GETUTCDATE()) AS ExecutionTimeMs;
        END
        
        -- Return success status
        SELECT 'SUCCESS' AS Status, NULL AS ErrorMessage;
        
    END TRY
    BEGIN CATCH
        SET @ErrorMessage = ERROR_MESSAGE();
        
        -- Return error information
        SELECT 0 AS TotalDeviceCount;
        SELECT 'ERROR' AS Status, @ErrorMessage AS ErrorMessage;
        
    END CATCH
END
```

## Usage Examples

### C# Implementation Using the Optimized Stored Procedure

```csharp
public async Task<int> GetOptimizationDeviceCountOptimized(int optimizationSessionId, int? tenantId = null)
{
    int deviceCount = 0;
    
    try
    {
        using (var connection = new SqlConnection(connectionString))
        {
            using (var command = new SqlCommand("dbo.usp_GetOptimizationDeviceCountOptimized", connection))
            {
                command.CommandType = CommandType.StoredProcedure;
                command.Parameters.AddWithValue("@OptimizationSessionId", optimizationSessionId);
                command.Parameters.AddWithValue("@TenantId", tenantId ?? (object)DBNull.Value);
                command.Parameters.AddWithValue("@IncludeInstanceDetails", false);
                
                await connection.OpenAsync();
                
                using (var reader = await command.ExecuteReaderAsync())
                {
                    if (await reader.ReadAsync())
                    {
                        deviceCount = reader.GetInt32("TotalDeviceCount");
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Log.Error($"Error getting optimization device count: {ex.Message}");
        throw;
    }
    
    return deviceCount;
}
```

## Performance Improvements

1. **NOLOCK Hint**: Added to prevent blocking during read operations
2. **Single Query**: Consolidates multiple operations into one database call
3. **Error Handling**: Comprehensive error handling with status returns
4. **Optional Parameters**: Flexible parameter structure for different use cases
5. **Metrics**: Built-in performance monitoring capabilities

## Migration Strategy

1. **Phase 1**: Deploy the optimized stored procedure alongside existing code
2. **Phase 2**: Update `GetOptimizationDeviceCountByInstance` method to use new stored procedure
3. **Phase 3**: Monitor performance and validate results
4. **Phase 4**: Remove old implementation after validation

## Related Tables and Views

- `vwOptimizationInstances` - Primary view for optimization instance data
- `OptimizationCustomerProcessing` - Stores device count during processing
- `ServiceProvider` - Service provider information
- `OptimizationStatus` - Optimization status lookup

## Performance Considerations

- The stored procedure uses `WITH (NOLOCK)` for read operations to minimize blocking
- Indexes on `OptimizationSessionId` and `TenantId` should be maintained for optimal performance
- Consider partitioning large tables if the dataset grows significantly
- Monitor execution plans and adjust as needed for specific workloads