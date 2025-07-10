# Cross-Provider Compilation Process: Algorithmic Analysis

## Overview
This document provides algorithmic breakdowns for cross-provider compilation logic in the Altaworx SIM Card Cost Optimization system, including winning assignment selection, cost savings compilation, Excel report generation, and provider-specific result formatting.

---

## 1. Selects Winning Assignments for Each Customer Rate Pool Group Across Providers

### What
**What:** Select the most cost-effective optimization queue results for each customer rate pool group by comparing total costs across all providers and choosing the winning assignment with minimal total expenditure.

### Why
**Why:** To ensure that customers receive the absolute best optimization results by selecting the lowest-cost solution across all available provider options for each rate pool group.

### How
**How:** The system uses SQL-based cost comparison to identify winning queues ordered by total cost ascending, then performs cleanup of non-winning results to maintain only optimal assignments.

### Algorithm Implementation

```
Algorithm: WinningAssignmentSelection
Input: commGroups[], providers[], optimizationResults[]
Output: winningQueueIds[], cleanedResults[]

1. FOR each commGroup in commGroups:
   a. Get winning queue: winningQueueId = GetWinningQueueId(commGroup.Id)
   b. SQL Query: "SELECT TOP 1 Id FROM OptimizationQueue 
                  WHERE CommPlanGroupId = @commGroupId 
                  AND TotalCost IS NOT NULL 
                  AND RunEndTime IS NOT NULL 
                  ORDER BY TotalCost ASC"

2. Cleanup non-winning results:
   a. EndQueuesForCommGroup(commGroup.Id) // Mark non-winners as complete
   b. CleanupDeviceResultsForCommGroup(commGroup.Id, winningQueueId)
   c. Execute stored procedure: usp_Optimization_DeviceResultAndQueueRatePlan_Cleanup

3. Aggregate winning assignments:
   a. Collect all winningQueueIds
   b. Process winning results for final compilation
   c. Generate comprehensive cross-provider reports
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` - Winning assignment selection

```csharp
// Get winning queue for each communication group based on lowest total cost
protected long GetWinningQueueId(KeySysLambdaContext context, long commGroupId)
{
    LogInfo(context, "SUB", $"GetWinningQueueId({commGroupId})");
    using (var conn = new SqlConnection(context.ConnectionString))
    {
        // Select the queue with the lowest total cost for the communication group
        using (var cmd = new SqlCommand("SELECT TOP 1 Id FROM OptimizationQueue WHERE CommPlanGroupId = @commGroupId AND TotalCost IS NOT NULL AND RunEndTime IS NOT NULL ORDER BY TotalCost ASC", conn))
        {
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@commGroupId", commGroupId);
            conn.Open();

            var rdr = cmd.ExecuteReader();
            while (rdr.Read())
            {
                return long.Parse(rdr["Id"].ToString());
            }
            conn.Close();
        }
    }
    return 0;
}

// Cleanup process for selecting winning assignments across providers
foreach (var commGroup in commGroups)
{
    // Get winning queue for each comm group based on cost optimization
    var winningQueueId = GetWinningQueueId(context, commGroup.Id);

    // End all non-winning queues for this communication group
    EndQueuesForCommGroup(context, commGroup.Id);

    // Clean up all device results except the winning queue
    CleanupDeviceResultsForCommGroup(context, commGroup.Id, winningQueueId);

    // Add winning queue to final results collection
    queueIds.Add(winningQueueId);
}

// End all non-winning queues in communication group
private void EndQueuesForCommGroup(KeySysLambdaContext context, long commGroupId)
{
    LogInfo(context, "SUB", $"EndQueuesForCommGroup({commGroupId})");
    using (var conn = new SqlConnection(context.ConnectionString))
    {
        // Update all non-winning queues to completed with errors status
        using (var cmd = new SqlCommand("UPDATE OptimizationQueue WITH (HOLDLOCK) SET RunEndTime = GETUTCDATE(), RunStatusId = @runStatusId, TotalCost = NULL WHERE CommPlanGroupId = @commGroupId AND RunEndTime IS NULL", conn))
        {
            cmd.CommandTimeout = 900;
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@commGroupId", commGroupId);
            cmd.Parameters.AddWithValue("@runStatusId", (int)OptimizationStatus.CompleteWithErrors);
            conn.Open();
            cmd.ExecuteNonQuery();
            conn.Close();
        }
    }
}

// Cleanup device results for non-winning assignments
private void CleanupDeviceResultsForCommGroup(KeySysLambdaContext context, long commGroupId, long queueId)
{
    LogInfo(context, "SUB", $"CleanupDeviceResultsForCommGroup(CommGroupID:{commGroupId}, Winning Queue ID:{queueId})");

    using (var conn = new SqlConnection(context.ConnectionString))
    {
        // Execute stored procedure to clean up non-winning device results
        using (var cmd = new SqlCommand("usp_Optimization_DeviceResultAndQueueRatePlan_Cleanup", conn))
        {
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@commGroupId", commGroupId);
            cmd.Parameters.AddWithValue("@winningQueueId", queueId);
            cmd.CommandTimeout = 900;
            conn.Open();
            cmd.ExecuteNonQuery();
            conn.Close();
        }
    }
}
```

---

## 2. Compiles Cross-Provider Cost Savings and Optimization Statistics

### What
**What:** Aggregate and compile comprehensive cost savings data and optimization statistics across M2M, Mobility, and CrossProvider platforms to generate unified optimization metrics.

### Why
**Why:** To provide customers with consolidated insights into their cost optimization performance across all provider ecosystems and demonstrate the value of cross-provider optimization strategies.

### How
**How:** The system builds optimization results using provider-specific result compilation, generates rate pool statistics, and creates comprehensive summaries with device counts and cost metrics.

### Algorithm Implementation

```
Algorithm: CrossProviderCostSavingsCompilation
Input: deviceResults[], ratePools[], providers[], billingPeriod
Output: optimizationStatistics, costSavingsReport

1. Initialize compilation structures:
   a. M2MOptimizationResult for M2M statistics
   b. MobilityOptimizationResult for Mobility statistics  
   c. CrossProviderResult for unified statistics

2. Build provider-specific optimization results:
   FOR each provider in [M2M, Mobility, CrossProvider]:
     a. result = BuildProviderOptimizationResult(deviceResults, ratePools, result)
     b. Aggregate device assignments and cost savings
     c. Calculate total device count and optimization metrics

3. Generate cross-provider statistics:
   a. statFileBytes = WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, result)
   b. assignmentFileBytes = WriteRatePoolAssignments(result)
   c. Combine shared pool statistics for comprehensive analysis

4. Calculate cost savings and ROI:
   a. originalCost = sum(device.originalRatePlan.cost)
   b. optimizedCost = sum(device.optimizedRatePlan.cost)
   c. totalSavings = originalCost - optimizedCost
   d. percentageSavings = (totalSavings / originalCost) * 100
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` - Cost savings and statistics compilation

```csharp
// Build M2M optimization results with cost savings compilation
private M2MOptimizationResult BuildM2MOptimizationResult(List<SimCardResult> deviceResults, List<ResultRatePool> ratePools, M2MOptimizationResult result, bool shouldSkipAutoChangeRatePlan = false)
{
    // Add SIM cards to result rate pools for cost calculation
    AddSimCardsToResultRatePools(deviceResults, ratePools, shouldSkipAutoChangeRatePlan);
    var tempRPList = new List<ResultRatePool>(ratePools);
    
    if (shouldSkipAutoChangeRatePlan)
    {
        // Filter out auto-change rate plans for specific compilation scenarios
        tempRPList = tempRPList.Where(ratePool => !ratePool.RatePlan.AutoChangeRatePlan).ToList();
    }
    
    // Create rate pool collection for cost savings analysis
    var collection = new M2MRatePoolCollection(tempRPList);
    result.RawRatePools = new List<M2MRatePoolCollection>() { collection };
    result.CombinedRatePools = collection;
    return result;
}

// Build Mobility optimization results with provider-specific metrics
private MobilityOptimizationResult BuildMobilityOptimizationResult(List<SimCardResult> deviceResults, List<ResultRatePool> ratePools, MobilityOptimizationResult result, bool shouldSkipAutoChangeRatePlan = false)
{
    AddSimCardsToResultRatePools(deviceResults, ratePools);
    var tempRPList = new List<ResultRatePool>(ratePools);
    
    if (shouldSkipAutoChangeRatePlan)
    {
        tempRPList = tempRPList.Where(ratePool => !ratePool.RatePlan.AutoChangeRatePlan).ToList();
    }
    
    var collection = new MobilityRatePoolCollection(tempRPList);
    result.RawRatePools = new List<MobilityRatePoolCollection>() { collection };
    result.CombinedRatePools = collection;
    return result;
}

// Add SIM cards to result rate pools for comprehensive cost analysis
private static void AddSimCardsToResultRatePools(List<SimCardResult> deviceResults, List<ResultRatePool> ratePools, bool shouldSkipAutoChangeRatePlan = false)
{
    foreach (var deviceResult in deviceResults)
    {
        foreach (var pool in ratePools)
        {
            var deviceKey = ResultRatePool.SimCardKeyByType(pool.KeyType, deviceResult);
            
            // Match device to appropriate rate pool based on plan ID and pool name
            if (pool.RatePlan.Id == deviceResult.RatePlanId && pool.RatePoolName == deviceResult.RatePoolName)
            {
                if (shouldSkipAutoChangeRatePlan && pool.RatePlan.AutoChangeRatePlan)
                {
                    break; // Skip auto-change rate plans if specified
                }
                
                // Merge or add device result for cost compilation
                if (pool.SimCards.ContainsKey(deviceKey))
                {
                    pool.SimCards[deviceKey] = pool.SimCards[deviceKey].MergeSimCardResult(deviceResult);
                }
                else
                {
                    pool.AddSimCard(deviceResult);
                }
                break;
            }
            // Handle unassigned devices for comprehensive statistics
            else if (pool.RatePlan.Id == OptimizationConstant.UnassignedRatePlanId)
            {
                if (pool.SimCards.ContainsKey(deviceKey))
                {
                    pool.SimCards[deviceKey] = pool.SimCards[deviceKey].MergeSimCardResult(deviceResult);
                }
                else
                {
                    pool.AddSimCard(deviceResult);
                }
                break;
            }
        }
    }
}

// Cross-provider compilation with comprehensive statistics generation
protected OptimizationInstanceResultFile WriteCrossProviderCustomerResults(KeySysLambdaContext context, OptimizationInstance instance, List<long> queueIds, bool usesProration)
{
    LogInfo(context, CommonConstants.SUB, $"WriteCrossProviderCustomerResults({instance.Id},{string.Join(',', queueIds)})");
    var totalDeviceCount = 0;
    
    // Initialize cross-provider optimization results
    var result = new M2MOptimizationResult();
    var crossCustomerResult = new M2MOptimizationResult();

    // Get cross-provider rate pools for cost compilation
    var crossOptimizationResultRatePools = GetResultRatePools(context, instance, customerBillingPeriod, usesProration, queueIds, true);
    var optimizationResultRatePools = GenerateCustomerSpecificRatePools(crossOptimizationResultRatePools);

    foreach (var queueId in queueIds)
    {
        LogInfo(context, CommonConstants.INFO, $"Building results for Optimization Queue with Id: {queueId}");
        
        // Get device results for cost savings compilation
        var deviceResults = crossProviderOptimizationRepository.GetCrossProviderResults(ParameterizedLog(context), new List<long>() { queueId }, customerBillingPeriod);
        totalDeviceCount += deviceResults.Count;
        
        // Build optimization result with cost savings
        result = BuildM2MOptimizationResult(deviceResults, optimizationResultRatePools, result);
        
        // Get shared pool results for comprehensive analysis
        var sharedPoolDeviceResults = crossProviderOptimizationRepository.GetCrossProviderSharedPoolResults(ParameterizedLog(context), new List<long>() { queueId }, customerBillingPeriod);
        sharedPoolDeviceResults.AddRange(deviceResults);
        crossCustomerResult = BuildM2MOptimizationResult(sharedPoolDeviceResults, crossOptimizationResultRatePools, crossCustomerResult, true);
    }

    // Generate comprehensive statistics and cost savings reports
    var statFileBytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, result);
    var assignmentFileBytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(result);
    
    // Include shared pool statistics if beneficial
    byte[] sharedPoolStatFileBytes = null;
    byte[] sharedPoolAssignmentFileBytes = null;
    if (crossCustomerResult.CombinedRatePools.TotalSimCardCount > result.CombinedRatePools.TotalSimCardCount)
    {
        sharedPoolStatFileBytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, crossCustomerResult);
        sharedPoolAssignmentFileBytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(crossCustomerResult);
    }

    return result;
}
```

---

## 3. Generates Comprehensive Multi-Provider Excel Reports with Device Assignments

### What
**What:** Create detailed Excel workbooks containing device assignments, cost optimization statistics, and provider-specific summaries across M2M, Mobility, and CrossProvider platforms.

### Why
**Why:** To provide customers with actionable, detailed reports that clearly show device-level assignments, cost savings, and optimization recommendations in a professional, easy-to-analyze format.

### How
**How:** The system uses specialized writers to generate Excel files with multiple sheets including statistics, assignments, and shared pool analyses, then packages them into comprehensive reports.

### Algorithm Implementation

```
Algorithm: MultiProviderExcelReportGeneration
Input: optimizationResults[], deviceAssignments[], statistics[]
Output: comprehensiveExcelReport

1. Generate provider-specific report components:
   a. statFileBytes = WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, result)
   b. assignmentFileBytes = WriteRatePoolAssignments(result)
   c. If shared pooling beneficial: generate shared pool statistics

2. Create multi-sheet Excel workbook:
   a. Sheet 1: Optimization Statistics and Cost Savings Summary
   b. Sheet 2: Device Assignment Details with Original vs Optimized Plans  
   c. Sheet 3: Shared Pool Analysis (if applicable)
   d. Sheet 4: Provider Comparison and Recommendations

3. Generate comprehensive Excel file:
   assignmentXlsxBytes = GenerateExcelFileFromByteArrays(
     statFileBytes, assignmentFileBytes, 
     sharedPoolStatFileBytes, sharedPoolAssignmentFileBytes)

4. Package and deliver report:
   a. Save to OptimizationInstanceResultFile database
   b. Attach to email notifications
   c. Make available for download through portal
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` - Excel report generation

```csharp
// Generate comprehensive multi-provider Excel reports
protected OptimizationInstanceResultFile WriteM2MResults(KeySysLambdaContext context, OptimizationInstance instance, List<long> queueIds, BillingPeriod billingPeriod, bool usesProration, bool isCustomerOptimization)
{
    LogInfo(context, LogTypeConstant.Sub, $"WriteM2MResults({instance.Id},{string.Join(',', queueIds)})");
    M2MOptimizationResult result = new M2MOptimizationResult();
    M2MOptimizationResult crossCustomerResult = new M2MOptimizationResult();

    // Get rate pools for comprehensive reporting
    var crossOptimizationResultRatePools = GetResultRatePools(context, instance, billingPeriod, usesProration, queueIds, isCustomerOptimization);
    var optimizationResultRatePools = GenerateCustomerSpecificRatePools(crossOptimizationResultRatePools);

    foreach (var queueId in queueIds)
    {
        LogInfo(context, LogTypeConstant.Info, $"Building results for queue with id: {queueId}.");
        
        // Get device results for Excel report generation
        var deviceResults = GetM2MResults(context, new List<long>() { queueId }, billingPeriod);
        result = BuildM2MOptimizationResult(deviceResults, optimizationResultRatePools, result);
        
        // Include shared pool results for comprehensive analysis
        var sharedPoolDeviceResults = GetM2MSharedPoolResults(context, new List<long>() { queueId }, billingPeriod);
        if (sharedPoolDeviceResults != null && sharedPoolDeviceResults.Count > 0)
        {
            shouldShowCrossPoolingTab = true;
        }
        sharedPoolDeviceResults.AddRange(deviceResults);
        crossCustomerResult = BuildM2MOptimizationResult(sharedPoolDeviceResults, crossOptimizationResultRatePools, crossCustomerResult, true);
    }

    // Generate Excel report components
    var statFileBytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, result);
    var assignmentFileBytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(result);
    
    byte[] sharedPoolStatFileBytes = null;
    byte[] sharedPoolAssignmentFileBytes = null;
    if (shouldShowCrossPoolingTab)
    {
        // Generate shared pool statistics for multi-provider analysis
        sharedPoolStatFileBytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, crossCustomerResult);
        sharedPoolAssignmentFileBytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(crossCustomerResult);
    }

    // Create comprehensive Excel file with multiple sheets
    LogInfo(context, "SUB", $"GenerateExcelFileFromByteArrays({result.QueueId})");
    var assignmentXlsxBytes = RatePoolAssignmentWriter.GenerateExcelFileFromByteArrays(statFileBytes, assignmentFileBytes, sharedPoolStatFileBytes, sharedPoolAssignmentFileBytes);

    // Save comprehensive Excel report to database
    return SaveOptimizationInstanceResultFile(context, instance.Id, assignmentXlsxBytes);
}

// Generate Mobility carrier optimization results with provider-specific Excel formatting
protected OptimizationInstanceResultFile WriteMobilityCarrierResults(KeySysLambdaContext context, OptimizationInstance instance, List<long> queueIds, BillingPeriod billingPeriod, bool usesProration)
{
    LogInfo(context, CommonConstants.SUB, $"WriteMobilityCarrierResults({instance.Id},{string.Join(',', queueIds)})");

    // Get provider-specific data for Excel report
    var deviceAssignments = new List<MobilityCarrierAssignmentExportModel>();
    var summariesByRatePlans = new List<MobilityCarrierSummaryReportModel>();
    var deviceResults = optimizationMobilityDeviceRepository.GetMobilityDeviceResults(context, queueIds, billingPeriod);

    // Map device assignments for Excel export with provider-specific formatting
    deviceAssignments.AddRange(MapToMobilityDeviceAssignmentsFromResult(originalAssignmentCollection, optimizationGroupResultPools, billingPeriod, optimizationGroup));
    summariesByRatePlans.AddRange(MapToSummariesFromResult(optimizationGroupResultPools, optimizationGroup));

    // Generate provider-specific Excel report
    var assignmentXlsxBytes = RatePoolAssignmentWriter.WriteOptimizationResultSheet(deviceAssignments, summariesByRatePlans);

    return SaveOptimizationInstanceResultFile(context, instance.Id, assignmentXlsxBytes);
}

// Save Excel report to database for delivery
protected virtual OptimizationInstanceResultFile SaveOptimizationInstanceResultFile(KeySysLambdaContext context, long instanceId, byte[] assignmentXlsxBytes, int totalDeviceCount = 0)
{
    LogInfo(context, "SUB", $"SaveOptimizationInstanceResultFile({instanceId})");
    var resultFile = new OptimizationInstanceResultFile()
    {
        InstanceId = instanceId,
        AssignmentXlsxBytes = assignmentXlsxBytes,
        TotalDeviceCount = totalDeviceCount
    };

    using (var conn = new SqlConnection(context.ConnectionString))
    {
        // Insert Excel report into database
        using (var cmd = new SqlCommand("INSERT INTO OptimizationInstanceResultFile(InstanceId, AssignmentXlsxBytes, CreatedBy, CreatedDate, IsDeleted) VALUES(@instanceId, @assignmentXlsxBytes, 'System', GETUTCDATE(), 0)", conn))
        {
            cmd.CommandTimeout = 180;
            cmd.CommandType = CommandType.Text;
            cmd.Parameters.AddWithValue("@instanceId", resultFile.InstanceId);
            cmd.Parameters.AddWithValue("@assignmentXlsxBytes", resultFile.AssignmentXlsxBytes);
            conn.Open();
            cmd.ExecuteNonQuery();
            conn.Close();
        }
    }
    return resultFile;
}
```

---

## 4. Cross-Provider Result Formatting and Data Structures

### What
**What:** Handle provider-specific data structures and formatting requirements to ensure consistent, accurate representation of optimization results across M2M, Mobility, and CrossProvider platforms.

### Why
**Why:** To maintain data integrity and provide standardized reporting formats while accommodating the unique characteristics and requirements of each provider ecosystem.

### How
**How:** The system uses provider-specific mapping functions, portal type differentiation, and specialized result processing to format data appropriately for each provider while maintaining cross-provider compatibility.

### Algorithm Implementation

```
Algorithm: ProviderSpecificResultFormatting
Input: rawResults[], portalType, customerType
Output: formattedResults[], providerSpecificStructures[]

1. Determine provider-specific formatting:
   IF (portalType == PortalTypes.M2M):
     - Use M2M-specific data structures and formatting
   ELSE IF (portalType == PortalTypes.Mobility):  
     - Apply Mobility-specific optimization group formatting
   ELSE IF (portalType == PortalTypes.CrossProvider):
     - Combine M2M and Mobility formatting approaches

2. Apply provider-specific result processing:
   a. Map rate plan pool mappings by portal type
   b. Generate provider-specific assignment models
   c. Create portal-appropriate summary reports

3. Format cross-provider compatibility:
   a. Standardize device assignment structures
   b. Normalize cost calculation methods
   c. Ensure consistent reporting formats across providers
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` - Provider-specific formatting

```csharp
// Provider-specific rate plan pool mapping for cross-provider compatibility
public List<RatePlanPoolMapping> GetRatePlanToRatePoolMappingByPortalType(KeySysLambdaContext context, List<long> queueIds, PortalTypes portalType)
{
    if (portalType == PortalTypes.CrossProvider)
    {
        // Cross-provider requires mapping from both M2M and Mobility
        var mappings = GetRatePlanToRatePoolMapping(context, queueIds, PortalTypes.M2M);
        mappings.AddRange(GetRatePlanToRatePoolMapping(context, queueIds, PortalTypes.Mobility));
        return mappings;
    }
    else
    {
        // Single provider mapping
        return GetRatePlanToRatePoolMapping(context, queueIds, portalType);
    }
}

// Provider-specific result processing with appropriate formatting
private OptimizationInstanceResultFile WriteResultByPortalType(KeySysLambdaContext context, bool isCustomerOptimization, OptimizationInstance instance, BillingPeriod billingPeriod, List<long> queueIds, bool usesProration)
{
    if (instance.PortalType == PortalTypes.Mobility)
    {
        // Mobility-specific result formatting with optimization groups
        return WriteMobilityResultsByOptimizationType(context, instance, queueIds, billingPeriod, usesProration, isCustomerOptimization);
    }
    else if (instance.PortalType == PortalTypes.M2M)
    {
        // M2M-specific result formatting with communication plans
        return WriteM2MResults(context, instance, queueIds, billingPeriod, usesProration, isCustomerOptimization);
    }
    else if (instance.PortalType == PortalTypes.CrossProvider)
    {
        // Cross-provider formatting combining M2M and Mobility approaches
        return WriteCrossProviderCustomerResults(context, instance, queueIds, usesProration);
    }
    else
    {
        OptimizationErrorHandler.OnPortalTypeError(context, PortalType, true);
        return null;
    }
}

// Cross-provider result processing with unified formatting
private void ProcessResultForCrossProvider(KeySysLambdaContext context, bool isCustomerOptimization, bool isLastInstance, OptimizationInstance instance, OptimizationInstanceResultFile fileResult)
{
    if (isCustomerOptimization)
    {
        var customer = GetRevCustomerById(context, instance.RevCustomerId.Value);
        
        // Update cross-provider processing status with standardized formatting
        crossProviderOptimizationRepository.UpdateProcessingCustomerOptimizationInstance(ParameterizedLog(context), instance.SessionId.GetValueOrDefault(), instance.Id, null, fileResult.TotalDeviceCount, false, instance.CustomerType, customer.RevCustomerId, instance.AMOPCustomerId);
        
        if (isLastInstance)
        {
            // Queue final cross-provider completion notification
            QueueLastStepOptCustomerCleanup(context, instance.Id, instance.SessionId.Value, true, 0, _optCustomerCleanUpDelaySeconds);
        }
    }
}

// Generate customer processing summary with provider-specific formatting
private string OptCustomerResultsBody(KeySysLambdaContext context, OptimizationInstance instance, List<OptimizationCustomerProcessing> optCustomerProcessing, string runStartTime, string runEndTime, string deviceDetailSyncDate, string deviceUsageSyncDate, string simCount)
{
    var stringBuilder = new StringBuilder($@"
        <html>
        <head>
        <style>
        body {{ background-color: #fff; font-family: ""Lato"", ""Helvetica Neue"", Helvetica, Arial, sans-serif; }}
        tr {{ text-align: left; }}
        th,td {{ padding-right: 10px; }}
        </style>
        </head>

        <div>Here are your optimization Results for Billing Period Ending on {instance.BillingPeriodEndDate.ToShortDateString()} {instance.BillingPeriodEndDate.ToShortTimeString()}. 
        Optimization started on: {runStartTime}. Optimization completed on: {runEndTime}.</div><br/>
        <div>Last Device Detail Sync Date: {deviceDetailSyncDate}<br/>
        Last Device Usage Sync Date: {deviceUsageSyncDate}<br/>
        Total SIM Cards: {simCount}<br/>
        Execution OU: {context.OptimizationSettings.ExecutionOU}</div><br/>
        <table><tr><th>No.</th><th>Customer Name</th></tr>");

    // Format customer names based on provider type
    foreach (var opt in optCustomerProcessing.Select((item, index) => new { item, index }))
    {
        var customerName = opt.item.CustomerName;
        if (instance.CustomerType == SiteTypes.AMOP)
        {
            customerName = opt.item.AMOPCustomerName; // AMOP-specific formatting
        }
        stringBuilder.Append($"<tr><td>{opt.index + 1}</td><td>{customerName}</td></tr>");
    }

    stringBuilder.Append("</table></html>");
    return stringBuilder.ToString();
}
```

This comprehensive compilation framework ensures accurate cross-provider result processing with winning assignment selection, detailed cost savings analysis, professional Excel reporting, and provider-specific formatting that maintains consistency across all telecommunication provider ecosystems.