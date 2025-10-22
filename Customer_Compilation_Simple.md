# Customer Compilation Process - Simple Algorithms

## Overview
Four simple steps to finalize customer optimization: select winners, calculate savings, create Excel reports, and generate summaries.

---

## 1. Select Winning Assignment

### What & Why
**What**: Pick the best optimization result for each customer group  
**Why**: Choose the option that saves the most money

### Simple Algorithm
```
FOR each customer group:
    1. Find all optimization results
    2. Pick the one with lowest total cost
    3. Delete the other results
    4. Keep the winner
```

### Code Location
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 338-348
var winningQueueId = GetWinningQueueId(context, commGroup.Id);
CleanupDeviceResultsForCommGroup(context, commGroup.Id, winningQueueId);

// Lines 2070-2082: How to find winner
SELECT TOP 1 Id FROM OptimizationQueue 
WHERE CommPlanGroupId = @commGroupId 
ORDER BY TotalCost ASC  -- Lowest cost wins
```

---

## 2. Calculate Cost Savings

### What & Why
**What**: Add up how much money customers will save  
**Why**: Show the financial benefit of optimization

### Simple Algorithm
```
FOR each winning result:
    1. Get all devices in that result
    2. Calculate: originalCost - newCost = savings
    3. Add up all savings
    4. Calculate percentage saved
```

### Example Calculation
```
Original Cost: $1000/month
New Cost: $750/month
Savings: $250/month (25% reduction)
```

### Code Location
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1363-1390
private MobilityOptimizationResult BuildMobilityOptimizationResult(
    List<SimCardResult> deviceResults, 
    List<ResultRatePool> ratePools)
{
    // Calculates cost savings from device results
    AddSimCardsToResultRatePools(deviceResults, ratePools);
}
```

---

## 3. Generate Excel Reports

### What & Why
**What**: Create Excel files with device assignments and cost details  
**Why**: Give customers detailed results they can review and implement

### Simple Algorithm
```
1. Create statistics sheet (summary numbers)
2. Create assignments sheet (which device gets which plan)
3. Create shared pools sheet (if needed)
4. Combine into one Excel file
```

### Excel Report Structure
- **Sheet 1**: Executive Summary (total savings, device count)
- **Sheet 2**: Device Assignments (ICCID → Rate Plan mapping)
- **Sheet 3**: Cost Analysis (before/after costs)
- **Sheet 4**: Shared Pools (cross-customer savings)

### Code Location
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 622-640
var statFileBytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(result);
var assignmentFileBytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(result);
var assignmentXlsxBytes = RatePoolAssignmentWriter.GenerateExcelFileFromByteArrays(
    statFileBytes, assignmentFileBytes, sharedPoolStatFileBytes, sharedPoolAssignmentFileBytes);
```

---

## 4. Create Customer Summaries

### What & Why
**What**: Make simple summaries for emails and reports  
**Why**: Communicate results to customers and management

### Simple Algorithm
```
1. Get optimization timing (start/end times)
2. Count total customers processed
3. Count total devices optimized
4. Create HTML table with customer names
5. Format as email body
```

### Summary Content
- Billing period dates
- Optimization start/end times
- Total SIM cards optimized
- List of customers processed
- Device sync information

### Code Location
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 2013-2068
private string OptCustomerResultsBody(
    OptimizationInstance instance,
    List<OptimizationCustomerProcessing> optCustomerProcessing)
{
    // Creates HTML summary with customer table
    var stringBuilder = new StringBuilder(@"<html>...");
    // Adds customer names and device counts
}
```

---

## Complete Process Flow

### Master Algorithm
```
STEP 1: Select Winners
    winners = []
    FOR each customer group:
        winner = FindLowestCostResult(group)
        winners.add(winner)

STEP 2: Calculate Savings  
    totalSavings = 0
    FOR each winner:
        savings = CalculateCustomerSavings(winner)
        totalSavings += savings

STEP 3: Generate Excel
    excel = CreateExcelReport(winners, savings)
    SaveExcelFile(excel)

STEP 4: Create Summary
    summary = CreateCustomerSummary(winners, totalSavings)
    SendEmail(summary, excel)
```

### Real Example
```
Input: 3 customer groups, each with 4 optimization strategies
Process: 
- Group A: Strategy 2 wins ($500 savings)
- Group B: Strategy 1 wins ($300 savings)  
- Group C: Strategy 3 wins ($700 savings)

Output:
- Total savings: $1,500/month
- Excel report with 3 winning assignments
- Email summary to customers
```

---

## Key Implementation Points

### Database Operations
- **Winner Selection**: `ORDER BY TotalCost ASC` finds cheapest option
- **Cleanup**: Delete non-winning results to save space
- **Statistics**: Sum `BaseRateAmount + RateChargeAmount + OverageChargeAmount`

### File Generation
- **Excel**: Multi-sheet workbook with assignments and statistics
- **Email**: HTML format with customer table and Excel attachment
- **Storage**: Save to `OptimizationInstanceResultFile` table

### Error Handling
- Skip groups with no valid results
- Log missing or invalid data
- Continue processing other groups if one fails

### Performance Tips
- Process groups in parallel when possible
- Cleanup non-winning results immediately
- Use database bulk operations for large datasets

---

## Simple Validation Checklist

✅ **Winner Selected**: Each group has exactly one winning result  
✅ **Savings Calculated**: All cost differences computed correctly  
✅ **Excel Generated**: Multi-sheet workbook created successfully  
✅ **Summary Created**: Customer list and timing information included  
✅ **Results Saved**: Files stored in database and sent via email