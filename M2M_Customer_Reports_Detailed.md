# M2M Customer Reports - Detailed Algorithms

## Overview
M2M (Machine-to-Machine) customer reporting system generates four comprehensive report types to provide customers with actionable optimization insights: device assignment spreadsheets, cost savings analysis, rate plan utilization statistics, and optimization group organizational details.

---

## 1. Customer Device Assignment Spreadsheets

### What & Why
**What**: Comprehensive Excel spreadsheets containing detailed device-to-rate-plan assignment instructions with implementation data  
**Why**: 
- **Implementation Guidance**: Provide customers exact step-by-step instructions for implementing optimization results
- **Audit Trail**: Create permanent record of optimization decisions and assignments
- **Operational Efficiency**: Enable bulk device management and rate plan changes
- **Compliance**: Meet regulatory requirements for device assignment documentation

### Detailed Algorithm
```
INITIALIZATION:
    deviceResults = GetM2MResults(context, queueIds, billingPeriod)
    optimizationResult = new M2MOptimizationResult()
    
MAIN PROCESSING LOOP:
    FOR each queueId in queueIds:
        1. DEVICE DATA EXTRACTION:
           - Query OptimizationDeviceResult table
           - Join with Device table for ICCID, MSISDN, CommunicationPlan
           - Join with CustomerRatePool for rate pool assignments
           - Join with JasperCustomerRatePlan/JasperCarrierRatePlan for rate plan codes
           
        2. COST CALCULATION:
           - Extract BaseRateAmt, RateChargeAmt, OverageChargeAmt from database
           - Calculate prorated costs if device activated mid-billing period
           - Apply billing period adjustments using billingPeriod.DaysInBillingPeriod
           
        3. USAGE DATA PROCESSING:
           - Process UsageMB for data consumption analysis
           - Process SmsUsage and SmsChargeAmount for messaging costs
           - Calculate utilization percentages against rate plan allowances
           
        4. ASSIGNMENT VALIDATION:
           - Verify AssignedCustomerRatePlanId or AssignedCarrierRatePlanId exists
           - Validate CustomerRatePoolId assignments
           - Check for rate plan compatibility with device type
           
    SPREADSHEET GENERATION:
        5. CREATE EXCEL STRUCTURE:
           - Device Assignment Sheet: Individual device assignments
           - Summary Sheet: Customer-level aggregated data
           - Statistics Sheet: Rate pool utilization metrics
           - Shared Pool Sheet: Cross-customer optimization opportunities
           
        6. DATA POPULATION:
           - Sort devices by communication plan and usage patterns
           - Format cost data with proper currency formatting
           - Add conditional formatting for utilization thresholds
           - Include data validation for rate plan codes
```

### Enhanced Spreadsheet Structure
- **Device Identification**: ICCID (primary), MSISDN, Device ID, Communication Plan
- **Current State**: Existing rate plan, current costs, usage patterns
- **Optimization Assignment**: New rate plan code, rate pool name, assignment rationale
- **Financial Impact**: Cost breakdown (base/usage/overage), savings calculation, ROI metrics
- **Implementation Data**: Activation dates, billing adjustments, proration factors
- **Validation Flags**: Assignment status, error conditions, approval requirements

### Code Location & Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 833-884
private List<SimCardResult> GetM2MResults(KeySysLambdaContext context, List<long> queueIds, BillingPeriod billingPeriod)
{
    // Comprehensive SQL query with multiple table joins
    using (var cmd = new SqlCommand(@"
        SELECT device.[Id] AS DeviceId, 
            [UsageMB], device.[ICCID], device.[MSISDN],
            ISNULL(commPlan.[AliasName], device.[CommunicationPlan]) AS CommunicationPlan, 
            ISNULL(carrierPlan.[RatePlanCode], customerPlan.[RatePlanCode]) AS RatePlanCode, 
            ISNULL(deviceResult.[AssignedCustomerRatePlanId], deviceResult.[AssignedCarrierRatePlanId]) AS RatePlanId, 
            deviceResult.[CustomerRatePoolId] AS RatePoolId,
            customerPool.[Name] AS RatePoolName,
            [ChargeAmt], device.[ProviderDateActivated], [SmsUsage], 
            [SmsChargeAmount], deviceResult.[BaseRateAmt], deviceResult.[RateChargeAmt], deviceResult.[OverageChargeAmt] 
        FROM OptimizationDeviceResult deviceResult 
        INNER JOIN Device device ON deviceResult.[AmopDeviceId] = device.[Id] 
        LEFT JOIN JasperCommunicationPlan commPlan ON commPlan.[CommunicationPlanName] = device.[CommunicationPlan] 
        LEFT JOIN JasperCarrierRatePlan carrierPlan ON deviceResult.[AssignedCarrierRatePlanId] = carrierPlan.[Id] 
        LEFT JOIN JasperCustomerRatePlan customerPlan ON deviceResult.[AssignedCustomerRatePlanId] = customerPlan.[Id] 
        LEFT JOIN CustomerRatePool customerPool ON deviceResult.[CustomerRatePoolId] = customerPool.[Id] 
        WHERE deviceResult.[QueueId] IN (@QueueIds)", conn))
}

// Lines 746-776: M2M result compilation and Excel generation
protected OptimizationInstanceResultFile WriteM2MResults(KeySysLambdaContext context, OptimizationInstance instance, List<long> queueIds, BillingPeriod billingPeriod, bool usesProration, bool isCustomerOptimization)
{
    result = BuildM2MOptimizationResult(deviceResults, optimizationResultRatePools, result);
    var assignmentFileBytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(result);
    var assignmentXlsxBytes = RatePoolAssignmentWriter.GenerateExcelFileFromByteArrays(assignmentFileBytes, statFileBytes, sharedPoolFileBytes);
}
```

---

## 2. Customer Cost Savings Analysis

### What & Why
**What**: Comprehensive financial analysis calculating and demonstrating monetary benefits achieved through optimization  
**Why**: 
- **ROI Demonstration**: Prove tangible financial value of optimization investments
- **Budget Planning**: Help customers forecast and plan telecommunications budgets
- **Performance Metrics**: Establish baseline metrics for ongoing optimization performance
- **Stakeholder Reporting**: Provide executive-level financial summaries for decision-making

### Detailed Algorithm
```
COST ANALYSIS INITIALIZATION:
    totalOriginalCost = 0
    totalOptimizedCost = 0
    deviceSavingsDetails = new List<DeviceSavings>()
    
FOR each device in deviceResults:
    1. ORIGINAL COST CALCULATION:
       originalBaseCost = device.BaseRateAmount
       originalUsageCost = device.RateChargeAmount  
       originalOverageCost = device.OverageChargeAmount
       originalTotalCost = originalBaseCost + originalUsageCost + originalOverageCost
       
    2. PRORATION ADJUSTMENT:
       IF device.WasActivatedInThisBillingPeriod:
           prorationFactor = device.DaysActivatedInBillingPeriod / billingPeriod.DaysInBillingPeriod
           originalTotalCost = originalTotalCost * prorationFactor
           
    3. OPTIMIZED COST CALCULATION:
       newRatePlan = GetRatePlanById(device.RatePlanId)
       optimizedBaseCost = CalculateProRatedBaseCost(newRatePlan, device.DaysActivatedInBillingPeriod)
       optimizedUsageCost = CalculateUsageCost(device.CycleDataUsageMB, newRatePlan)
       optimizedOverageCost = CalculateOverageCost(device.CycleDataUsageMB, newRatePlan)
       optimizedTotalCost = optimizedBaseCost + optimizedUsageCost + optimizedOverageCost
       
    4. SAVINGS CALCULATION:
       deviceSavings = originalTotalCost - optimizedTotalCost
       deviceSavingsPercentage = (deviceSavings / originalTotalCost) * 100
       
    5. VALIDATION AND RECORDING:
       IF deviceSavings > 0:
           deviceSavingsDetails.Add(new DeviceSavings(device.Id, originalTotalCost, optimizedTotalCost, deviceSavings, deviceSavingsPercentage))
           totalOriginalCost += originalTotalCost
           totalOptimizedCost += optimizedTotalCost
           
AGGREGATE ANALYSIS:
    totalMonthlySavings = totalOriginalCost - totalOptimizedCost
    totalAnnualSavings = totalMonthlySavings * 12
    overallSavingsPercentage = (totalMonthlySavings / totalOriginalCost) * 100
    averageSavingsPerDevice = totalMonthlySavings / deviceResults.Count
```

### Enhanced Example Calculation
```
Customer: Manufacturing Corp
Device Count: 250 IoT devices

BEFORE OPTIMIZATION:
Device Type A (100 devices @ $25/month): $2,500
Device Type B (100 devices @ $35/month): $3,500  
Device Type C (50 devices @ $45/month):  $2,250
Total Monthly Cost: $8,250

AFTER OPTIMIZATION:
Device Type A → Plan Efficiency (100 devices @ $20/month): $2,000
Device Type B → Plan Standard (100 devices @ $25/month):  $2,500
Device Type C → Plan Premium (50 devices @ $30/month):   $1,500
Total Monthly Cost: $6,000

SAVINGS ANALYSIS:
Monthly Savings: $2,250 (27.3% reduction)
Annual Savings: $27,000
Average Per Device: $9/month
ROI Timeline: 3.2 months (assuming $8,500 implementation cost)

BREAKDOWN BY DEVICE TYPE:
Type A: $500/month savings (20% reduction)
Type B: $1,000/month savings (28.6% reduction)  
Type C: $750/month savings (33.3% reduction)
```

### Code Location & Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1376-1390
private M2MOptimizationResult BuildM2MOptimizationResult(List<SimCardResult> deviceResults, List<ResultRatePool> ratePools, M2MOptimizationResult result, bool shouldSkipAutoChangeRatePlan = false)
{
    AddSimCardsToResultRatePools(deviceResults, ratePools, shouldSkipAutoChangeRatePlan);
    var tempRPList = ratePools.Where(rp => rp.SimCardResultList.Count > 0).ToList();
    var collection = new M2MRatePoolCollection(tempRPList);
    result.RawRatePools = new List<M2MRatePoolCollection>() { collection };
    result.CombinedRatePools = collection;
    return result;
}

// Lines 1073-1130: Cost data extraction with null handling
BaseRateAmount = !rdr.IsDBNull("BaseRateAmt") ? rdr.GetDecimal("BaseRateAmt") : 0,
RateChargeAmount = !rdr.IsDBNull("RateChargeAmt") ? rdr.GetDecimal("RateChargeAmt") : 0,
OverageChargeAmount = !rdr.IsDBNull("OverageChargeAmt") ? rdr.GetDecimal("OverageChargeAmt") : 0,
WasActivatedInThisBillingPeriod = DateIsInBillingPeriod(simCard.DateActivated, billingPeriod),
DaysActivatedInBillingPeriod = simCard.WasActivatedInThisBillingPeriod ? DaysLeftInBillingPeriod(simCard.DateActivated, billingPeriod) : billingPeriod.DaysInBillingPeriod
```

---

## 3. Customer Rate Plan Utilization Statistics

### What & Why
**What**: Comprehensive analysis of rate plan efficiency and resource utilization across customer device portfolio  
**Why**:
- **Efficiency Optimization**: Identify under-utilized plans wasting allowances and over-utilized plans generating overage charges
- **Capacity Planning**: Help customers understand usage patterns for future capacity planning
- **Cost Efficiency**: Maximize value from rate plan investments through optimal utilization
- **Trend Analysis**: Establish baseline metrics for tracking utilization improvements over time

### Detailed Algorithm
```
UTILIZATION ANALYSIS INITIALIZATION:
    utilizationStats = new List<RatePlanUtilizationStat>()
    utilizationThresholds = { underUtilized: 70%, wellUtilized: 90%, overUtilized: 100% }
    
FOR each ratePlan in customerRatePlans:
    1. DEVICE AGGREGATION:
       assignedDevices = GetDevicesAssignedToRatePlan(ratePlan.Id)
       deviceCount = assignedDevices.Count
       
    2. ALLOWANCE CALCULATION:
       totalDataAllowance = ratePlan.DataAllowanceMB * deviceCount
       totalSmsAllowance = ratePlan.SmsAllowance * deviceCount
       
    3. ACTUAL USAGE AGGREGATION:
       totalDataUsage = SUM(assignedDevices.Select(d => d.CycleDataUsageMB))
       totalSmsUsage = SUM(assignedDevices.Select(d => d.SmsUsage))
       
    4. UTILIZATION CALCULATION:
       dataUtilizationPercentage = (totalDataUsage / totalDataAllowance) * 100
       smsUtilizationPercentage = (totalSmsUsage / totalSmsAllowance) * 100
       
    5. EFFICIENCY CLASSIFICATION:
       IF dataUtilizationPercentage < utilizationThresholds.underUtilized:
           classification = "Under-Utilized"
           recommendedAction = "Consider downgrading to lower allowance plan"
           wasteAmount = totalDataAllowance - totalDataUsage
           wasteCost = (wasteAmount / totalDataAllowance) * (ratePlan.BaseRate * deviceCount)
           
       ELSE IF dataUtilizationPercentage <= utilizationThresholds.wellUtilized:
           classification = "Well-Utilized" 
           recommendedAction = "Maintain current plan assignments"
           
       ELSE IF dataUtilizationPercentage > utilizationThresholds.overUtilized:
           classification = "Over-Utilized"
           recommendedAction = "Consider upgrading to higher allowance plan"
           overageAmount = totalDataUsage - totalDataAllowance
           overageCost = overageAmount * ratePlan.OverageRate
           
    6. COST IMPACT ANALYSIS:
       currentMonthlyCost = (ratePlan.BaseRate * deviceCount) + overageCost
       potentialSavings = CalculatePotentialSavings(classification, wasteCost, overageCost)
       
    7. RECORD STATISTICS:
       utilizationStats.Add(new RatePlanUtilizationStat {
           RatePlanCode = ratePlan.Code,
           DeviceCount = deviceCount,
           DataUtilization = dataUtilizationPercentage,
           SmsUtilization = smsUtilizationPercentage,
           Classification = classification,
           RecommendedAction = recommendedAction,
           CostImpact = potentialSavings
       })
```

### Enhanced Utilization Categories & Recommendations
**Under-Utilized Plans (<70% usage)**:
- **Issue**: Paying for unused allowances, inefficient spend
- **Recommendation**: Downgrade to lower allowance plans or consolidate devices
- **Potential Savings**: 15-25% cost reduction through right-sizing

**Well-Utilized Plans (70-90% usage)**:
- **Status**: Optimal utilization range, efficient resource usage
- **Recommendation**: Maintain current assignments, monitor for usage growth
- **Management**: Track monthly to ensure continued efficiency

**Over-Utilized Plans (>90% usage)**:
- **Issue**: High overage charges, potential service disruption
- **Recommendation**: Upgrade to higher allowance plans or implement usage controls
- **Risk Mitigation**: Prevent unexpected overage costs and service throttling

### Enhanced Example Statistics
```
Customer Analysis: TechCorp Manufacturing (500 M2M devices)

RATE PLAN UTILIZATION BREAKDOWN:

Plan A - IoT Basic (1GB, $15/month):
- Devices Assigned: 200
- Total Allowance: 200GB
- Actual Usage: 140GB  
- Utilization: 70% (Well-Utilized)
- Monthly Cost: $3,000
- Status: ✅ Optimal efficiency

Plan B - IoT Standard (2GB, $25/month):
- Devices Assigned: 200
- Total Allowance: 400GB
- Actual Usage: 180GB
- Utilization: 45% (Under-Utilized)
- Monthly Cost: $5,000
- Waste: 220GB unused ($2,750 waste cost)
- Recommendation: ⬇️ Downgrade 100 devices to Plan A

Plan C - IoT Premium (5GB, $50/month):
- Devices Assigned: 100
- Total Allowance: 500GB
- Actual Usage: 480GB
- Utilization: 96% (Over-Utilized)
- Monthly Cost: $5,200 (including $200 overage)
- Recommendation: ⬆️ Consider higher allowance plan

OPTIMIZATION OPPORTUNITY:
Current Cost: $13,200/month
Optimized Cost: $10,450/month (Plan redistribution)
Monthly Savings: $2,750 (20.8% reduction)
Annual Savings: $33,000
```

---

## 4. Customer Optimization Group Details

### What & Why
**What**: Comprehensive organizational analysis showing device grouping strategies, rate pool assignments, and optimization methodology  
**Why**:
- **Strategy Transparency**: Help customers understand optimization decision-making process
- **Operational Planning**: Enable efficient device management and bulk operations
- **Cross-Customer Benefits**: Identify shared pool opportunities for additional savings
- **Change Management**: Provide structured approach for implementing optimization changes

### Detailed Algorithm
```
GROUP ANALYSIS INITIALIZATION:
    communicationPlanGroups = new Dictionary<string, List<Device>>()
    ratePoolAssignments = new Dictionary<int, List<Device>>()
    sharedPoolOpportunities = new List<SharedPoolOpportunity>()
    
PHASE 1: COMMUNICATION PLAN GROUPING
    FOR each device in customerDevices:
        communicationPlan = device.CommunicationPlan
        IF !communicationPlanGroups.ContainsKey(communicationPlan):
            communicationPlanGroups[communicationPlan] = new List<Device>()
        communicationPlanGroups[communicationPlan].Add(device)
        
PHASE 2: RATE POOL ASSIGNMENT ANALYSIS
    FOR each device in optimizedDevices:
        ratePoolId = device.CustomerRatePoolId
        IF !ratePoolAssignments.ContainsKey(ratePoolId):
            ratePoolAssignments[ratePoolId] = new List<Device>()
        ratePoolAssignments[ratePoolId].Add(device)
        
PHASE 3: SHARED POOL OPPORTUNITY IDENTIFICATION
    FOR each ratePool in crossCustomerRatePools:
        IF ratePool.IsSharedRatePool:
            customerDevices = ratePool.SimCardResultList.Where(d => d.CustomerId == currentCustomerId)
            otherCustomerDevices = ratePool.SimCardResultList.Where(d => d.CustomerId != currentCustomerId)
            
            IF customerDevices.Count > 0 AND otherCustomerDevices.Count > 0:
                sharedOpportunity = new SharedPoolOpportunity {
                    RatePoolName = ratePool.Name,
                    CustomerDeviceCount = customerDevices.Count,
                    TotalDeviceCount = ratePool.SimCardResultList.Count,
                    CostSharingBenefit = CalculateCostSharingBenefit(ratePool),
                    RecommendedAction = "Consider shared pool participation"
                }
                sharedPoolOpportunities.Add(sharedOpportunity)
                
PHASE 4: GROUP STATISTICS CALCULATION
    FOR each group in communicationPlanGroups:
        groupStats = new GroupStatistics {
            GroupName = group.Key,
            DeviceCount = group.Value.Count,
            TotalUsage = SUM(group.Value.Select(d => d.CycleDataUsageMB)),
            TotalCost = SUM(group.Value.Select(d => d.ChargeAmt)),
            AverageUsagePerDevice = groupStats.TotalUsage / groupStats.DeviceCount,
            CostSavingsFromOptimization = CalculateGroupSavings(group.Value)
        }
```

### Enhanced Group Details Structure
**Communication Plan Groups**:
- Group identification and device categorization
- Usage pattern analysis per group
- Cost distribution and optimization impact
- Implementation complexity assessment

**Rate Pool Assignments**:
- Pool utilization and capacity analysis
- Device compatibility and assignment rationale
- Cost allocation and sharing mechanisms
- Performance metrics and benchmarks

**Shared Pool Opportunities**:
- Cross-customer collaboration benefits
- Cost sharing calculations and savings potential
- Operational considerations and requirements
- Risk assessment and mitigation strategies

**Group Statistics Dashboard**:
- Device count and distribution metrics
- Usage analytics and trend identification
- Cost analysis and savings quantification
- Performance benchmarks and KPIs

### Code Location & Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1245-1332
public List<RatePlanPoolMapping> GetRatePlanToRatePoolMapping(KeySysLambdaContext context, List<long> queueIds, PortalTypes portalType)
{
    var ratePlanPoolMappings = new List<RatePlanPoolMapping>();
    var resultTableName = GetResultTableNameFromPortalType(context, portalType);
    var sharedResultTableName = GetSharedResultTableNameFromPortalType(context, portalType);
    
    // Query for regular customer rate pool mappings
    using (var cmd = new SqlCommand(GetRatePlanPoolMappingQueryString(resultTableName), conn))
    {
        // Extract rate plan to pool mappings for grouping analysis
        SELECT ISNULL(carrierPlan.[RatePlanCode], customerPlan.[RatePlanCode]) AS RatePlanCode,
               ISNULL(deviceResult.[AssignedCustomerRatePlanId], deviceResult.[AssignedCarrierRatePlanId]) AS RatePlanId, 
               deviceResult.[CustomerRatePoolId] AS RatePoolId,
               customerPool.[Name] AS RatePoolName
        FROM OptimizationDeviceResult deviceResult 
        GROUP BY carrierPlan.[RatePlanCode], customerPlan.[RatePlanCode], deviceResult.[AssignedCustomerRatePlanId], deviceResult.[AssignedCarrierRatePlanId], deviceResult.[CustomerRatePoolId], customerPool.[Name]
    }
    
    // Query for shared pool mappings
    using (var cmd = new SqlCommand(GetRatePlanPoolMappingQueryString(sharedResultTableName), conn))
    {
        // Process shared pool opportunities for cross-customer benefits
    }
}

// Lines 821-830: Customer-specific rate pool generation
private static List<ResultRatePool> GenerateCustomerSpecificRatePools(List<ResultRatePool> crossOptimizationResultRatePools)
{
    var optimizationResultRatePools = new List<ResultRatePool>();
    foreach (var crossOptimizationResultRatePool in crossOptimizationResultRatePools)
    {
        if (!crossOptimizationResultRatePool.IsSharedRatePool)
        {
            optimizationResultRatePools.Add(new ResultRatePool(crossOptimizationResultRatePool));
        }
    }
    return optimizationResultRatePools;
}
```

---

## Enhanced Validation & Quality Assurance

### Data Validation Checklist
✅ **Device Assignment Validation**: Verify all M2M devices have valid ICCID, rate plan assignments, and cost calculations  
✅ **Cost Calculation Accuracy**: Validate savings calculations are positive, reasonable, and mathematically consistent  
✅ **Utilization Statistics Integrity**: Ensure all rate plans show valid utilization percentages between 0-200%  
✅ **Group Organization Logic**: Confirm communication plan groups are properly categorized and non-overlapping  
✅ **Excel Report Generation**: Verify multi-sheet workbook creation with proper formatting and data validation  
✅ **Shared Pool Analysis**: Validate cross-customer opportunities identification and cost sharing calculations  
✅ **Performance Metrics**: Ensure report generation completes within acceptable time thresholds  
✅ **Error Handling**: Confirm graceful handling of missing data, invalid assignments, and edge cases