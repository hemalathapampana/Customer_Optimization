-- =============================================
-- Author: System Generated
-- Create date: 2024
-- Description: Optimized stored procedure to retrieve device count during optimization
-- =============================================

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
GO

-- Grant execute permissions
GRANT EXECUTE ON [dbo].[usp_GetOptimizationDeviceCountOptimized] TO [OptimizationRole];
GO