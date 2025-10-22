# Customer Compilation Process Algorithm

## Overview
The customer compilation process finalizes customer optimization by selecting winning assignments, compiling cost savings and statistics, generating Excel reports, and creating customer optimization summaries for stakeholder communication.

---

## 1. Select Winning Customer Assignment for Each Customer Rate Pool Group

### What
Identifies and selects the optimal customer assignment strategy for each customer rate pool group based on total cost analysis.

### Why
- **Cost Optimization**: Ensures the most cost-effective assignment is implemented
- **Resource Efficiency**: Eliminates suboptimal assignments to reduce resource waste
- **Customer Value**: Delivers maximum savings to each customer rate pool group
- **Implementation Ready**: Provides clear assignment instructions for execution

### How
The system compares total costs across all optimization strategies and selects the queue with the lowest cost for each communication plan group.

### Algorithm
```
INPUT: commGroupIds, optimizationInstance

STEP 1: Iterate Through Communication Plan Groups
    FOR EACH commGroupId IN commGroupIds:
        
        STEP 1.1: Get All Strategy Results
            queueResults ← GetAllQueuesForCommGroup(commGroupId)
            
            validResults ← []
            FOR EACH queue IN queueResults:
                IF queue.TotalCost IS NOT NULL AND queue.RunEndTime IS NOT NULL:
                    validResults.Add(queue)
                END IF
            END FOR
        
        STEP 1.2: Select Winning Assignment
            winningQueueId ← GetWinningQueueId(commGroupId)
            
            IF winningQueueId = 0:
                LogError("No winning queue found for commGroup " + commGroupId)
                CONTINUE
            END IF
        
        STEP 1.3: Mark Winner and Cleanup
            MarkQueueAsWinner(winningQueueId)
            EndQueuesForCommGroup(commGroupId)
            CleanupDeviceResultsForCommGroup(commGroupId, winningQueueId)
            
            LogInfo("Selected winning queue " + winningQueueId + " for commGroup " + commGroupId)
            
            winningQueueIds.Add(winningQueueId)
    END FOR

OUTPUT: winningQueueIds (list of selected optimal queues)
```

### Code Location
**File: `AltaworxSimCardCostOptimizerCleanup.cs`**
```csharp
// Lines 338-348: Winning assignment selection process
foreach (var commGroup in commGroups)
{
    var winningQueueId = GetWinningQueueId(context, commGroup.Id);
    EndQueuesForCommGroup(context, commGroup.Id);
    CleanupDeviceResultsForCommGroup(context, commGroup.Id, winningQueueId);
    queueIds.Add(winningQueueId);
}

// Lines 2070-2082: GetWinningQueueId implementation
protected long GetWinningQueueId(KeySysLambdaContext context, long commGroupId)
{
    using (var cmd = new SqlCommand("SELECT TOP 1 Id FROM OptimizationQueue WHERE CommPlanGroupId = @commGroupId AND TotalCost IS NOT NULL AND RunEndTime IS NOT NULL ORDER BY TotalCost ASC", conn))
    {
        // Returns queue with lowest total cost
    }
}
```

---

## 2. Compile Customer Cost Savings and Optimization Statistics

### What
Aggregates and compiles comprehensive customer cost savings data and optimization performance statistics.

### Why
- **Financial Reporting**: Provides accurate cost savings metrics for business decisions
- **Performance Analysis**: Tracks optimization effectiveness across customer segments
- **ROI Calculation**: Enables return on investment analysis for optimization efforts
- **Stakeholder Communication**: Delivers quantifiable results to customers and management

### How
The system processes device results, calculates cost differences, and compiles optimization metrics into structured data collections.

### Algorithm
```
INPUT: winningQueueIds, optimizationInstance, billingPeriod

STEP 1: Initialize Statistics Collections
    totalOriginalCost ← 0
    totalOptimizedCost ← 0
    totalDeviceCount ← 0
    optimizationStatistics ← []
    
STEP 2: Process Each Winning Queue
    FOR EACH queueId IN winningQueueIds:
        
        STEP 2.1: Get Device Results
            deviceResults ← GetDeviceResults(queueId, billingPeriod)
            
        STEP 2.2: Calculate Cost Statistics
            queueOriginalCost ← 0
            queueOptimizedCost ← 0
            
            FOR EACH device IN deviceResults:
                originalCost ← device.BaseRateAmount + device.RateChargeAmount + device.OverageChargeAmount
                optimizedCost ← CalculateOptimizedCost(device, selectedRatePlan)
                
                queueOriginalCost ← queueOriginalCost + originalCost
                queueOptimizedCost ← queueOptimizedCost + optimizedCost
                
                totalDeviceCount ← totalDeviceCount + 1
            END FOR
        
        STEP 2.3: Compile Queue Statistics
            queueSavings ← queueOriginalCost - queueOptimizedCost
            queueSavingsPercentage ← (queueSavings / queueOriginalCost) × 100
            
            queueStatistics ← {
                QueueId: queueId,
                DeviceCount: deviceResults.Count,
                OriginalCost: queueOriginalCost,
                OptimizedCost: queueOptimizedCost,
                TotalSavings: queueSavings,
                SavingsPercentage: queueSavingsPercentage
            }
            
            optimizationStatistics.Add(queueStatistics)
            
            totalOriginalCost ← totalOriginalCost + queueOriginalCost
            totalOptimizedCost ← totalOptimizedCost + queueOptimizedCost
    END FOR

STEP 3: Calculate Overall Statistics
    overallSavings ← totalOriginalCost - totalOptimizedCost
    overallSavingsPercentage ← (overallSavings / totalOriginalCost) × 100
    
    compiledStatistics ← {
        TotalDeviceCount: totalDeviceCount,
        TotalOriginalCost: totalOriginalCost,
        TotalOptimizedCost: totalOptimizedCost,
        TotalSavings: overallSavings,
        SavingsPercentage: overallSavingsPercentage,
        QueueStatistics: optimizationStatistics,
        OptimizationDate: DateTime.UtcNow,
        BillingPeriod: billingPeriod
    }

OUTPUT: compiledStatistics
```

### Code Location
**File: `AltaworxSimCardCostOptimizerCleanup.cs`**
```csharp
// Lines 1363-1390: Optimization result building
private MobilityOptimizationResult BuildMobilityOptimizationResult(List<SimCardResult> deviceResults, List<ResultRatePool> ratePools, MobilityOptimizationResult result, bool shouldSkipAutoChangeRatePlan = false)
{
    AddSimCardsToResultRatePools(deviceResults, ratePools);
    var collection = new MobilityRatePoolCollection(tempRPList);
    result.CombinedRatePools = collection;
    return result;
}

// Lines 1391-1433: Statistics compilation
private static void AddSimCardsToResultRatePools(List<SimCardResult> deviceResults, List<ResultRatePool> ratePools, bool shouldSkipAutoChangeRatePlan = false)
{
    foreach (var deviceResult in deviceResults)
    {
        // Compile device statistics into rate pools
        // Calculate cost savings and optimization metrics
    }
}
```

---

## 3. Generate Customer-Specific Excel Reports with Device Assignments

### What
Creates comprehensive Excel reports containing detailed device assignment data, cost analysis, and optimization recommendations for each customer.

### Why
- **Customer Communication**: Provides detailed optimization results in accessible format
- **Implementation Guide**: Contains specific device assignment instructions
- **Audit Trail**: Maintains detailed records of optimization decisions
- **Business Reporting**: Enables further analysis and reporting by customer teams

### How
The system processes optimization results and generates multi-sheet Excel workbooks with statistics, device assignments, and shared pool information.

### Algorithm
```
INPUT: optimizationResults, customerInfo, deviceAssignments

STEP 1: Prepare Report Data
    statFileBytes ← RatePoolStatisticsWriter.WriteRatePoolStatistics(optimizationResults)
    assignmentFileBytes ← RatePoolAssignmentWriter.WriteRatePoolAssignments(optimizationResults)
    
    // Generate shared pool data if applicable
    sharedPoolStatFileBytes ← NULL
    sharedPoolAssignmentFileBytes ← NULL
    
    IF crossCustomerResult.TotalSimCardCount > result.TotalSimCardCount:
        sharedPoolStatFileBytes ← RatePoolStatisticsWriter.WriteRatePoolStatistics(crossCustomerResult)
        sharedPoolAssignmentFileBytes ← RatePoolAssignmentWriter.WriteRatePoolAssignments(crossCustomerResult)
    END IF

STEP 2: Create Excel Workbook Structure
    workbook ← CreateNewWorkbook()
    
    // Sheet 1: Executive Summary
    summarySheet ← CreateSummarySheet(optimizationResults, customerInfo)
    AddToWorkbook(workbook, summarySheet)
    
    // Sheet 2: Device Assignments
    assignmentSheet ← CreateAssignmentSheet(deviceAssignments)
    AddToWorkbook(workbook, assignmentSheet)
    
    // Sheet 3: Rate Pool Statistics
    statisticsSheet ← CreateStatisticsSheet(statFileBytes)
    AddToWorkbook(workbook, statisticsSheet)
    
    // Sheet 4: Shared Pool Results (if applicable)
    IF sharedPoolStatFileBytes IS NOT NULL:
        sharedPoolSheet ← CreateSharedPoolSheet(sharedPoolStatFileBytes, sharedPoolAssignmentFileBytes)
        AddToWorkbook(workbook, sharedPoolSheet)
    END IF

STEP 3: Format Excel Report
    ApplyCustomerBranding(workbook, customerInfo)
    ApplyStandardFormatting(workbook)
    AddCharts(workbook, optimizationResults)
    SetColumnWidths(workbook)
    ProtectSheets(workbook)

STEP 4: Generate Final Excel File
    assignmentXlsxBytes ← RatePoolAssignmentWriter.GenerateExcelFileFromByteArrays(
        statFileBytes, 
        assignmentFileBytes, 
        sharedPoolStatFileBytes, 
        sharedPoolAssignmentFileBytes
    )

OUTPUT: assignmentXlsxBytes (Excel file as byte array)
```

### Enhanced Excel Generation Framework
```csharp
public class CustomerExcelReport
{
    public byte[] GenerateCustomerOptimizationReport(
        OptimizationResult results, 
        CustomerInfo customer, 
        List<DeviceAssignment> assignments)
    {
        var workbook = new XLWorkbook();
        
        // Executive Summary Sheet
        var summarySheet = workbook.Worksheets.Add("Executive Summary");
        CreateExecutiveSummary(summarySheet, results, customer);
        
        // Device Assignments Sheet  
        var assignmentSheet = workbook.Worksheets.Add("Device Assignments");
        CreateDeviceAssignments(assignmentSheet, assignments);
        
        // Cost Analysis Sheet
        var costSheet = workbook.Worksheets.Add("Cost Analysis");
        CreateCostAnalysis(costSheet, results);
        
        // Rate Plan Recommendations Sheet
        var recommendationSheet = workbook.Worksheets.Add("Recommendations");
        CreateRecommendations(recommendationSheet, results);
        
        using (var stream = new MemoryStream())
        {
            workbook.SaveAs(stream);
            return stream.ToArray();
        }
    }
}
```

### Code Location
**File: `AltaworxSimCardCostOptimizerCleanup.cs`**
```csharp
// Lines 622-640: Excel report generation for M2M results
var statFileBytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, result);
var assignmentFileBytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(result);

if (crossCustomerResult.CombinedRatePools.TotalSimCardCount > result.CombinedRatePools.TotalSimCardCount)
{
    sharedPoolStatFileBytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, crossCustomerResult);
    sharedPoolAssignmentFileBytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(crossCustomerResult);
}

var assignmentXlsxBytes = RatePoolAssignmentWriter.GenerateExcelFileFromByteArrays(statFileBytes, assignmentFileBytes, sharedPoolStatFileBytes, sharedPoolAssignmentFileBytes);

// Lines 711: Mobility carrier results Excel generation
var assignmentXlsxBytes = RatePoolAssignmentWriter.WriteOptimizationResultSheet(deviceAssignments, summariesByRatePlans);
```

---

## 4. Create Customer Optimization Summaries

### What
Generates concise customer optimization summaries containing key metrics, customer lists, and performance indicators for stakeholder communication.

### Why
- **Executive Communication**: Provides high-level summary for leadership
- **Customer Notification**: Informs customers of optimization completion
- **Performance Tracking**: Summarizes optimization effectiveness
- **Process Documentation**: Records optimization completion status

### How
The system compiles customer processing records, timing information, and summary statistics into formatted HTML and text summaries.

### Algorithm
```
INPUT: optimizationInstance, customerProcessingRecords, timingInfo, deviceCounts

STEP 1: Gather Summary Information
    runStartTime ← FormatDateTime(optimizationInstance.RunStartTime, billingTimeZone)
    runEndTime ← FormatDateTime(optimizationInstance.RunEndTime, billingTimeZone)
    billingPeriodEnd ← FormatDate(optimizationInstance.BillingPeriodEndDate)
    
    totalCustomersProcessed ← customerProcessingRecords.Count
    totalDevicesOptimized ← SUM(customerProcessingRecords.DeviceCount)
    
    deviceDetailSyncDate ← FormatDateTime(syncResults.DetailLastSyncDate)
    deviceUsageSyncDate ← FormatDateTime(syncResults.UsageLastSyncDate)

STEP 2: Create Customer Summary Table
    customerSummaryHtml ← CreateHtmlTable()
    
    customerSummaryHtml.AddHeader("No.", "Customer Name", "Device Count", "Completion Status")
    
    FOR EACH customer IN customerProcessingRecords:
        customerName ← GetCustomerDisplayName(customer, optimizationInstance.CustomerType)
        deviceCount ← customer.DeviceCount
        completionStatus ← customer.IsProcessed ? "Completed" : "In Progress"
        
        customerSummaryHtml.AddRow(
            customer.Index + 1,
            customerName,
            deviceCount,
            completionStatus
        )
    END FOR

STEP 3: Generate Summary Content
    summaryContent ← CreateSummaryContent(
        billingPeriodEnd,
        runStartTime,
        runEndTime,
        deviceDetailSyncDate,
        deviceUsageSyncDate,
        totalDevicesOptimized,
        executionOU,
        customerSummaryHtml
    )

STEP 4: Format Summary Output
    IF outputFormat = "HTML":
        summary ← OptCustomerResultsBody(
            optimizationInstance,
            customerProcessingRecords,
            runStartTime,
            runEndTime,
            deviceDetailSyncDate,
            deviceUsageSyncDate,
            totalDevicesOptimized.ToString()
        )
    ELSE IF outputFormat = "EMAIL":
        summary ← BuildResultsEmailBody(
            optimizationInstance,
            assignmentXlsxBytes,
            billingTimeZone,
            syncResults
        )
    END IF

OUTPUT: summary (formatted customer optimization summary)
```

### Code Location
**File: `AltaworxSimCardCostOptimizerCleanup.cs`**
```csharp
// Lines 2013-2068: Customer optimization summary creation
private string OptCustomerResultsBody(KeySysLambdaContext context, OptimizationInstance instance,
    List<OptimizationCustomerProcessing> optCustomerProcessing, string runStartTime, string runEndTime, string deviceDetailSyncDate, string deviceUsageSyncDate, string simCount)
{
    var stringBuilder = new StringBuilder($@"
        <html>
        <head>
        <style>
        body {{
            background-color: #fff;
            font-family: ""Lato"", ""Helvetica Neue"", Helvetica, Arial, sans-serif;
        }}
        </style>
        </head>
        
        <div>Here are your optimization Results for Billing Period Ending on {instance.BillingPeriodEndDate.ToShortDateString()}. 
        Optimization started on: {runStartTime}. Optimization completed on: {runEndTime}.</div><br/>
        
        <table>
        <tr><th>No.</th><th>Customer Name</th></tr>");
    
    foreach (var opt in optCustomerProcessing.Select((item, index) => new { item, index }))
    {
        var customerName = instance.CustomerType == SiteTypes.AMOP ? opt.item.AMOPCustomerName : opt.item.CustomerName;
        stringBuilder.Append($"<tr><td>{opt.index + 1}</td><td>{customerName}</td></tr>");
    }
    
    stringBuilder.Append("</table></html>");
    return stringBuilder.ToString();
}

// Lines 1987-2011: Email body builder for results
private BodyBuilder BuildResultsEmailBody(KeySysLambdaContext context, OptimizationInstance instance, byte[] assignmentXlsxBytes, TimeZoneInfo billingTimeZone, DeviceSyncSummary syncResults)
{
    var body = new BodyBuilder()
    {
        HtmlBody = $"<div>Here are your optimization Results for Billing Period Ending on {instance.BillingPeriodEndDate.ToShortDateString()}. Optimization started on: {runStartTime}. Optimization completed on: {runEndTime}.</div>",
        TextBody = $"Optimization Results for Billing Period Ending on {instance.BillingPeriodEndDate.ToShortDateString()}..."
    };
    body.Attachments.Add("device_assignments.xlsx", assignmentXlsxBytes, new ContentType("application", "vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    return body;
}
```

---

## Complete Customer Compilation Process Algorithm

### Integrated Compilation Workflow
```
INPUT: optimizationInstance, commGroupIds, billingPeriod

STEP 1: Select Winning Assignments
    winningQueueIds ← []
    FOR EACH commGroupId IN commGroupIds:
        winningQueueId ← SelectWinningCustomerAssignment(commGroupId)
        winningQueueIds.Add(winningQueueId)
    END FOR

STEP 2: Compile Cost Savings and Statistics
    optimizationStatistics ← CompileCustomerCostSavings(winningQueueIds, billingPeriod)
    
    totalSavings ← optimizationStatistics.TotalSavings
    savingsPercentage ← optimizationStatistics.SavingsPercentage
    deviceCount ← optimizationStatistics.TotalDeviceCount

STEP 3: Generate Excel Reports
    deviceAssignments ← GetDeviceAssignments(winningQueueIds)
    customerInfo ← GetCustomerInformation(optimizationInstance)
    
    assignmentXlsxBytes ← GenerateCustomerExcelReport(
        optimizationStatistics,
        customerInfo,
        deviceAssignments
    )

STEP 4: Create Optimization Summaries
    customerProcessingRecords ← GetCustomerProcessingRecords(optimizationInstance.SessionId)
    timingInfo ← GetOptimizationTimingInfo(optimizationInstance)
    
    customerSummary ← CreateCustomerOptimizationSummary(
        optimizationInstance,
        customerProcessingRecords,
        timingInfo,
        deviceCount
    )

STEP 5: Save and Distribute Results
    SaveOptimizationInstanceResultFile(optimizationInstance.Id, assignmentXlsxBytes, deviceCount)
    
    IF isCustomerOptimization:
        SendCustomerOptimizationResults(optimizationInstance, customerSummary)
    ELSE:
        SendCarrierOptimizationResults(optimizationInstance, assignmentXlsxBytes)
    END IF

OUTPUT: compilationSuccess, totalSavings, deviceCount
```

## Key Implementation Points

**Winning Assignment Selection**
- SQL query orders by TotalCost ASC to find lowest cost
- Cleanup removes non-winning queue results  
- Validation ensures valid TotalCost and RunEndTime

**Statistics Compilation**
- Aggregates BaseRateAmount, RateChargeAmount, OverageChargeAmount
- Calculates original vs optimized cost differences
- Compiles device counts and savings percentages

**Excel Report Generation**
- Multi-sheet workbooks with statistics, assignments, shared pools
- Uses RatePoolStatisticsWriter and RatePoolAssignmentWriter
- Supports both M2M and Mobility portal types

**Customer Summaries**
- HTML-formatted customer lists with completion status
- Email-ready format with Excel attachments
- Timing information and sync status details