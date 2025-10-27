-- =============================================
-- Author: System Generated  
-- Create date: 2024
-- Description: Enhanced stored procedure to retrieve device count with performance metrics and breakdown
-- =============================================

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
                DATEDIFF(MILLISECOND, @StartTime, GETUTCDATE()) AS ExecutionTimeMs,
                @OptimizationSessionId AS OptimizationSessionId,
                @TenantId AS TenantId,
                @PortalType AS FilteredPortalType;
        END
        
        -- Return success status
        SELECT 'SUCCESS' AS Status, NULL AS ErrorMessage;
        
    END TRY
    BEGIN CATCH
        SET @ErrorMessage = ERROR_MESSAGE();
        
        -- Return error information
        SELECT 0 AS TotalDeviceCount;
        
        IF @IncludeBreakdown = 1
        BEGIN
            SELECT 
                0 AS M2MDeviceCount,
                0 AS MobilityDeviceCount,
                0 AS TotalDeviceCount,
                DATEDIFF(MILLISECOND, @StartTime, GETUTCDATE()) AS ExecutionTimeMs,
                @OptimizationSessionId AS OptimizationSessionId,
                @TenantId AS TenantId,
                @PortalType AS FilteredPortalType;
        END
        
        SELECT 'ERROR' AS Status, @ErrorMessage AS ErrorMessage;
        
    END CATCH
END
GO

-- Grant execute permissions
GRANT EXECUTE ON [dbo].[usp_GetOptimizationDeviceCountWithMetrics] TO [OptimizationRole];
GO