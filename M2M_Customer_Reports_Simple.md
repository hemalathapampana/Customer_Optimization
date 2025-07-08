# M2M Customer Reports - Simple Algorithms

## Overview
Four types of M2M customer reports: device assignment spreadsheets, cost savings summaries, rate plan utilization statistics, and optimization group details.

---

## 1. Customer Device Assignment Spreadsheets

### What & Why
**What**: Excel spreadsheets showing which devices get which rate plans  
**Why**: Give customers exact instructions for implementing optimization results

### Simple Algorithm
```
FOR each M2M customer:
    1. Get all devices from optimization results
    2. Create spreadsheet with device details
    3. Add rate plan assignments
    4. Include cost information
```

### Spreadsheet Structure
- **Device ID**: ICCID and MSISDN
- **Current Plan**: Communication plan and rate plan code
- **New Assignment**: Assigned rate plan and rate pool
- **Cost Details**: Base rate, usage charges, overage charges
- **Usage Data**: Monthly MB usage and SMS usage

### Code Location
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 833-870
private List<SimCardResult> GetM2MResults(KeySysLambdaContext context, List<long> queueIds, BillingPeriod billingPeriod)
{
    // SQL Query selects:
    // ICCID, MSISDN, CommunicationPlan, RatePlanCode, 
    // BaseRateAmt, RateChargeAmt, OverageChargeAmt, UsageMB
    SELECT device.[ICCID], device.[MSISDN],
           device.[CommunicationPlan], 
           customerPlan.[RatePlanCode],
           deviceResult.[BaseRateAmt], 
           deviceResult.[RateChargeAmt], 
           deviceResult.[OverageChargeAmt]
    FROM OptimizationDeviceResult deviceResult 
    INNER JOIN Device device ON deviceResult.[AmopDeviceId] = device.[Id]
}

// Lines 746-801: M2M report generation
protected OptimizationInstanceResultFile WriteM2MResults(...)
{
    var assignmentFileBytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(result);
    var assignmentXlsxBytes = RatePoolAssignmentWriter.GenerateExcelFileFromByteArrays(...);
}
```

---

## 2. Customer Cost Savings Summaries

### What & Why
**What**: Calculate and show how much money customers save with optimization  
**Why**: Demonstrate the financial value of the optimization process

### Simple Algorithm
```
FOR each customer device:
    1. Calculate original cost = BaseRate + RateCharge + OverageCharge
    2. Calculate optimized cost using new rate plan
    3. Find savings = original cost - optimized cost
    4. Sum up total customer savings
```

### Example Calculation
```
Device A:
- Original cost: $15/month (base) + $5 (usage) + $10 (overage) = $30
- New cost: $12/month (base) + $3 (usage) + $0 (overage) = $15
- Savings: $15/month per device

Customer with 100 devices:
- Total monthly savings: $1,500
- Annual savings: $18,000
```

### Code Location
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1376-1390
private M2MOptimizationResult BuildM2MOptimizationResult(
    List<SimCardResult> deviceResults, 
    List<ResultRatePool> ratePools)
{
    AddSimCardsToResultRatePools(deviceResults, ratePools);
    var collection = new M2MRatePoolCollection(tempRPList);
    result.CombinedRatePools = collection;  // Contains cost savings data
}

// Lines 1073-1127: Cost field extraction from database
BaseRateAmount = !rdr.IsDBNull("BaseRateAmt") ? rdr.GetDecimal("BaseRateAmt") : 0,
RateChargeAmount = !rdr.IsDBNull("RateChargeAmt") ? rdr.GetDecimal("RateChargeAmt") : 0,
OverageChargeAmount = !rdr.IsDBNull("OverageChargeAmt") ? rdr.GetDecimal("OverageChargeAmt") : 0,
```

---

## 3. Customer Rate Plan Utilization Statistics

### What & Why
**What**: Show how efficiently customers are using their rate plan allowances  
**Why**: Help customers understand plan efficiency and identify optimization opportunities

### Simple Algorithm
```
FOR each rate plan:
    1. Calculate total data allowance for all devices
    2. Calculate actual data usage for all devices
    3. Find utilization = actual usage / total allowance
    4. Identify under-utilized and over-utilized plans
```

### Utilization Categories
- **Under-utilized**: <70% of allowance used (waste money)
- **Well-utilized**: 70-90% of allowance used (efficient)
- **Over-utilized**: >90% of allowance used (overage charges)

### Example Statistics
```
Rate Plan A (1GB allowance):
- 50 devices assigned
- Total allowance: 50GB
- Actual usage: 35GB
- Utilization: 70% (well-utilized)

Rate Plan B (2GB allowance):
- 25 devices assigned  
- Total allowance: 50GB
- Actual usage: 15GB
- Utilization: 30% (under-utilized, potential savings)
```

### Code Location
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1150-1174
private List<ResultRatePool> GetResultRatePools(KeySysLambdaContext context, ...)
{
    var ratePools = GenerateResultRatePoolFromRatePlans(billingPeriod, usesProration, ratePlans, planPoolMappings, false, instance);
    // Rate pools contain utilization statistics
}

// Lines 780-790: Statistics file generation
var statFileBytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, result);
```

---

## 4. Customer Optimization Group Details

### What & Why
**What**: Show how devices are grouped and organized for optimization  
**Why**: Help customers understand the optimization strategy and device organization

### Simple Algorithm
```
FOR each customer:
    1. Group devices by communication plan
    2. Identify rate pool assignments
    3. Show shared pool opportunities
    4. Display optimization group statistics
```

### Group Details Structure
- **Communication Plan Groups**: Devices grouped by current communication plan
- **Rate Pool Assignments**: Which rate pools devices are assigned to
- **Shared Pool Information**: Cross-customer optimization opportunities
- **Group Statistics**: Device count, usage totals, cost savings per group

### Code Location
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1245-1310
public List<RatePlanPoolMapping> GetRatePlanToRatePoolMapping(KeySysLambdaContext context, List<long> queueIds, PortalTypes portalType)
{
    // Maps rate plans to rate pools for grouping
    SELECT ISNULL(carrierPlan.[RatePlanCode], customerPlan.[RatePlanCode]) AS RatePlanCode,
           deviceResult.[CustomerRatePoolId] AS RatePoolId,
           customerPool.[Name] AS RatePoolName
    FROM OptimizationDeviceResult deviceResult 
    LEFT JOIN CustomerRatePool customerPool ON deviceResult.[CustomerRatePoolId] = customerPool.[Id]
}

// Lines 821-830: Customer-specific rate pools
private static List<ResultRatePool> GenerateCustomerSpecificRatePools(List<ResultRatePool> crossOptimizationResultRatePools)
{
    // Filters shared pools to customer-specific pools
    if (!crossOptimizationResultRatePool.IsSharedRatePool)
    {
        optimizationResultRatePools.Add(new ResultRatePool(crossOptimizationResultRatePool));
    }
}
```

---

## Complete Report Generation Flow

### Master Algorithm
```
STEP 1: Generate Device Assignments
    devices = GetM2MResults(queueIds)
    spreadsheet = CreateDeviceAssignmentSpreadsheet(devices)

STEP 2: Calculate Cost Savings
    FOR each device:
        savings = CalculateSavings(device.originalCost, device.newCost)
    totalSavings = SumAllSavings(devices)
    
STEP 3: Analyze Utilization
    ratePools = GetResultRatePools(devices)
    utilizationStats = CalculateUtilization(ratePools)
    
STEP 4: Create Group Details
    groups = GroupDevicesByCommunicationPlan(devices)
    poolMappings = GetRatePlanToRatePoolMapping(queueIds)
    groupDetails = CreateOptimizationGroupDetails(groups, poolMappings)

STEP 5: Generate Final Reports
    excelFile = CombineAllReports(spreadsheet, totalSavings, utilizationStats, groupDetails)
    SaveAndEmailReports(excelFile)
```

### Real Example
```
Customer: TechCorp Manufacturing
M2M Devices: 500 IoT sensors

Report Results:
- Device Assignments: 500 devices optimized across 5 rate plans
- Cost Savings: $2,500/month (25% reduction)
- Utilization: 3 plans well-utilized, 2 plans under-utilized
- Groups: 5 communication plan groups, 2 shared rate pools

Excel Output:
- Sheet 1: Device Assignment List (500 rows)
- Sheet 2: Cost Savings Summary ($2,500 total)
- Sheet 3: Utilization Statistics (5 rate plans analyzed)
- Sheet 4: Optimization Group Details (5 groups, 2 shared pools)
```

---

## Key Implementation Points

### Database Tables
- **OptimizationDeviceResult**: Main device assignment data
- **Device**: Device details (ICCID, MSISDN, CommunicationPlan)
- **CustomerRatePool**: Rate pool assignments and names
- **JasperCustomerRatePlan**: Customer rate plan details

### Excel Generation
- **Statistics Sheet**: Rate pool utilization and cost analysis
- **Assignment Sheet**: Device-to-rate-plan mappings
- **Shared Pool Sheet**: Cross-customer optimization opportunities
- **Summary Sheet**: High-level customer metrics

### Cost Calculations
- **Original Cost**: BaseRateAmt + RateChargeAmt + OverageChargeAmt
- **Optimized Cost**: New rate plan costs after optimization
- **Savings**: Difference between original and optimized costs

### Error Handling
- Skip devices with missing ICCID or rate plan data
- Log invalid cost calculations
- Continue processing if individual device fails

---

## Simple Validation Checklist

✅ **Device Assignments**: All M2M devices have valid rate plan assignments  
✅ **Cost Savings**: Savings calculations are positive and reasonable  
✅ **Utilization Stats**: All rate plans show valid utilization percentages  
✅ **Group Details**: Communication plan groups are properly organized  
✅ **Excel Generated**: Multi-sheet workbook created with all report sections