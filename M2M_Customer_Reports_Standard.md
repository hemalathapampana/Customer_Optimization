# M2M Customer Reports - Standard Algorithms

## Overview
M2M customer reporting system generates four essential report types that provide customers with comprehensive optimization insights and implementation guidance for their device portfolios.

---

## 1. Customer Device Assignment Spreadsheets

### What & Why
**What**: Excel spreadsheets containing device-to-rate-plan assignments with implementation details  
**Why**: 
- **Implementation Guidance**: Provide clear instructions for rate plan changes
- **Documentation**: Create audit trail for optimization decisions
- **Bulk Operations**: Enable efficient device management workflows

### Standard Algorithm
```
STEP 1: Data Collection
    deviceResults = GetM2MResults(queueIds, billingPeriod)
    
STEP 2: Device Processing
    FOR each device in deviceResults:
        - Extract device identifiers (ICCID, MSISDN, DeviceId)
        - Get communication plan and current rate plan
        - Retrieve assigned rate plan and rate pool information
        - Calculate cost components (base, usage, overage)
        - Apply billing period proration if needed
        
STEP 3: Assignment Validation
    FOR each device assignment:
        - Verify rate plan compatibility
        - Validate rate pool assignments
        - Check cost calculation accuracy
        - Flag any assignment errors
        
STEP 4: Excel Generation
    - Create device assignment sheet with sorted data
    - Add summary sheet with aggregated metrics
    - Include validation and formatting rules
    - Generate final Excel workbook
```

### Spreadsheet Content Structure
- **Device Details**: ICCID, MSISDN, Communication Plan, Device ID
- **Current Assignment**: Existing rate plan code and costs
- **New Assignment**: Optimized rate plan and rate pool
- **Cost Analysis**: Base rate, usage charges, overage costs
- **Implementation**: Activation dates and billing adjustments

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 833-870
private List<SimCardResult> GetM2MResults(KeySysLambdaContext context, List<long> queueIds, BillingPeriod billingPeriod)
{
    // SQL Query extracts comprehensive device data
    SELECT device.[ICCID], device.[MSISDN], device.[CommunicationPlan],
           customerPlan.[RatePlanCode], deviceResult.[BaseRateAmt], 
           deviceResult.[RateChargeAmt], deviceResult.[OverageChargeAmt],
           customerPool.[Name] AS RatePoolName
    FROM OptimizationDeviceResult deviceResult 
    INNER JOIN Device device ON deviceResult.[AmopDeviceId] = device.[Id]
    LEFT JOIN CustomerRatePool customerPool ON deviceResult.[CustomerRatePoolId] = customerPool.[Id]
}

// Lines 746-776: Report generation and Excel creation
protected OptimizationInstanceResultFile WriteM2MResults(...)
{
    result = BuildM2MOptimizationResult(deviceResults, ratePools, result);
    assignmentXlsxBytes = RatePoolAssignmentWriter.GenerateExcelFileFromByteArrays(...);
}
```

---

## 2. Customer Cost Savings Summaries

### What & Why
**What**: Financial analysis showing cost reductions achieved through optimization  
**Why**:
- **ROI Demonstration**: Prove financial value of optimization
- **Budget Planning**: Support telecommunications budget forecasting
- **Performance Tracking**: Establish savings benchmarks

### Standard Algorithm
```
STEP 1: Cost Data Extraction
    originalCosts = []
    optimizedCosts = []
    
STEP 2: Device Cost Calculation
    FOR each device in deviceResults:
        originalCost = BaseRateAmt + RateChargeAmt + OverageChargeAmt
        
        IF device activated mid-billing period:
            prorationFactor = DaysActivated / TotalBillingDays
            originalCost = originalCost * prorationFactor
            
        optimizedCost = CalculateNewPlanCost(device.newRatePlan, device.usage)
        deviceSavings = originalCost - optimizedCost
        
        originalCosts.Add(originalCost)
        optimizedCosts.Add(optimizedCost)
        
STEP 3: Aggregate Analysis
    totalMonthlySavings = SUM(originalCosts) - SUM(optimizedCosts)
    totalAnnualSavings = totalMonthlySavings * 12
    savingsPercentage = (totalMonthlySavings / SUM(originalCosts)) * 100
    averageSavingsPerDevice = totalMonthlySavings / deviceCount
    
STEP 4: Summary Generation
    Create savings summary with:
    - Total monthly and annual savings
    - Percentage improvement
    - Per-device average savings
    - Cost breakdown by device type
```

### Example Calculation
```
Customer: TechCorp IoT Division
Device Portfolio: 200 M2M devices

BEFORE OPTIMIZATION:
- 80 devices @ $30/month = $2,400
- 70 devices @ $40/month = $2,800  
- 50 devices @ $50/month = $2,500
Total Monthly Cost: $7,700

AFTER OPTIMIZATION:
- 80 devices @ $25/month = $2,000
- 70 devices @ $30/month = $2,100
- 50 devices @ $35/month = $1,750
Total Monthly Cost: $5,850

SAVINGS RESULTS:
Monthly Savings: $1,850 (24% reduction)
Annual Savings: $22,200
Average Per Device: $9.25/month
Implementation ROI: 4.1 months
```

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1376-1390
private M2MOptimizationResult BuildM2MOptimizationResult(List<SimCardResult> deviceResults, List<ResultRatePool> ratePools)
{
    AddSimCardsToResultRatePools(deviceResults, ratePools);
    var collection = new M2MRatePoolCollection(tempRPList);
    result.CombinedRatePools = collection; // Contains cost savings data
}

// Lines 1073-1130: Cost field extraction and proration handling
BaseRateAmount = !rdr.IsDBNull("BaseRateAmt") ? rdr.GetDecimal("BaseRateAmt") : 0,
WasActivatedInThisBillingPeriod = DateIsInBillingPeriod(simCard.DateActivated, billingPeriod),
DaysActivatedInBillingPeriod = CalculateBillingDays(simCard.DateActivated, billingPeriod)
```

---

## 3. Customer Rate Plan Utilization Statistics

### What & Why
**What**: Analysis of rate plan efficiency and resource utilization patterns  
**Why**:
- **Efficiency Optimization**: Identify under-utilized and over-utilized plans
- **Capacity Planning**: Support future usage forecasting
- **Cost Optimization**: Maximize rate plan value through optimal utilization

### Standard Algorithm
```
STEP 1: Rate Plan Analysis Setup
    utilizationThresholds = { underUtilized: 70%, wellUtilized: 90% }
    utilizationStats = []
    
STEP 2: Plan-by-Plan Analysis
    FOR each ratePlan in customerRatePlans:
        assignedDevices = GetDevicesForRatePlan(ratePlan.Id)
        
        totalAllowance = ratePlan.DataAllowanceMB * assignedDevices.Count
        actualUsage = SUM(assignedDevices.CycleDataUsageMB)
        utilizationPercentage = (actualUsage / totalAllowance) * 100
        
STEP 3: Efficiency Classification
        IF utilizationPercentage < 70%:
            status = "Under-Utilized"
            wasteAmount = totalAllowance - actualUsage
            recommendation = "Consider downgrading to lower allowance plan"
            
        ELSE IF utilizationPercentage <= 90%:
            status = "Well-Utilized"
            recommendation = "Maintain current assignments"
            
        ELSE:
            status = "Over-Utilized" 
            overageAmount = actualUsage - totalAllowance
            recommendation = "Consider upgrading to higher allowance plan"
            
STEP 4: Statistics Recording
        utilizationStats.Add({
            ratePlan: ratePlan.Code,
            deviceCount: assignedDevices.Count,
            utilization: utilizationPercentage,
            status: status,
            recommendation: recommendation
        })
```

### Utilization Analysis Example
```
Customer Analysis: Manufacturing IoT Network (300 devices)

RATE PLAN BREAKDOWN:

Plan A - Basic IoT (1GB, $20/month):
- Assigned Devices: 150
- Total Allowance: 150GB
- Actual Usage: 105GB
- Utilization: 70% (Well-Utilized ✅)
- Status: Optimal efficiency

Plan B - Standard IoT (3GB, $35/month):
- Assigned Devices: 100  
- Total Allowance: 300GB
- Actual Usage: 120GB
- Utilization: 40% (Under-Utilized ⚠️)
- Waste: 180GB unused allowance
- Recommendation: Move 50 devices to Plan A

Plan C - Premium IoT (5GB, $55/month):
- Assigned Devices: 50
- Total Allowance: 250GB
- Actual Usage: 240GB  
- Utilization: 96% (Over-Utilized ⚠️)
- Risk: High overage potential
- Recommendation: Upgrade to higher allowance plan

OPTIMIZATION OPPORTUNITY:
Current Monthly Cost: $8,750
Optimized Cost: $7,100 (19% reduction)
Annual Savings: $19,800
```

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1150-1174
private List<ResultRatePool> GetResultRatePools(KeySysLambdaContext context, ...)
{
    var ratePools = GenerateResultRatePoolFromRatePlans(billingPeriod, usesProration, ratePlans, planPoolMappings, false, instance);
    // Rate pools contain utilization calculations and device assignments
}

// Lines 780-790: Statistics generation
var statFileBytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, result);
```

---

## 4. Customer Optimization Group Details

### What & Why
**What**: Organization analysis showing device grouping strategies and rate pool assignments  
**Why**:
- **Strategy Understanding**: Explain optimization methodology to customers
- **Operational Planning**: Support efficient device management
- **Shared Benefits**: Identify cross-customer optimization opportunities

### Standard Algorithm
```
STEP 1: Communication Plan Grouping
    commPlanGroups = {}
    FOR each device in customerDevices:
        commPlan = device.CommunicationPlan
        IF commPlan not in commPlanGroups:
            commPlanGroups[commPlan] = []
        commPlanGroups[commPlan].Add(device)
        
STEP 2: Rate Pool Assignment Analysis
    ratePoolAssignments = {}
    FOR each device in optimizedDevices:
        ratePoolId = device.CustomerRatePoolId
        IF ratePoolId not in ratePoolAssignments:
            ratePoolAssignments[ratePoolId] = []
        ratePoolAssignments[ratePoolId].Add(device)
        
STEP 3: Shared Pool Identification
    sharedPools = []
    FOR each ratePool in allRatePools:
        IF ratePool.IsSharedRatePool:
            customerDevices = GetCustomerDevices(ratePool, currentCustomerId)
            otherCustomerDevices = GetOtherCustomerDevices(ratePool, currentCustomerId)
            
            IF customerDevices.Count > 0 AND otherCustomerDevices.Count > 0:
                sharedPools.Add({
                    poolName: ratePool.Name,
                    customerDeviceCount: customerDevices.Count,
                    totalDeviceCount: ratePool.TotalDevices,
                    costSharingBenefit: CalculateCostSharing(ratePool)
                })
                
STEP 4: Group Statistics Calculation
    FOR each group in commPlanGroups:
        Calculate:
        - Device count per group
        - Total usage per group  
        - Cost savings from optimization
        - Average usage per device
```

### Group Analysis Structure
**Communication Plan Groups**:
- Device categorization by communication plan
- Usage pattern analysis per group
- Cost impact assessment

**Rate Pool Assignments**:
- Pool utilization metrics
- Device assignment rationale
- Cost allocation methodology

**Shared Pool Opportunities**:
- Cross-customer collaboration benefits
- Cost sharing calculations
- Implementation considerations

### Example Group Analysis
```
Customer: Industrial IoT Solutions

COMMUNICATION PLAN GROUPS:
Group 1 - "Sensor_Basic": 120 devices
- Average usage: 0.8GB/month
- Total cost: $2,400/month
- Optimization savings: $360/month (15%)

Group 2 - "Tracker_Standard": 80 devices  
- Average usage: 2.1GB/month
- Total cost: $2,800/month
- Optimization savings: $700/month (25%)

Group 3 - "Gateway_Premium": 50 devices
- Average usage: 4.2GB/month
- Total cost: $2,750/month
- Optimization savings: $550/month (20%)

RATE POOL ASSIGNMENTS:
Pool A - "IoT_Efficiency": 150 devices from Groups 1&2
Pool B - "IoT_Performance": 50 devices from Group 3
Pool C - "Shared_Enterprise": 50 devices (cross-customer pool)

SHARED POOL BENEFITS:
- Pool C saves additional $200/month through cost sharing
- 15% cost reduction vs individual customer pools
- Access to premium features at reduced cost
```

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1245-1310
public List<RatePlanPoolMapping> GetRatePlanToRatePoolMapping(KeySysLambdaContext context, List<long> queueIds, PortalTypes portalType)
{
    // Extracts rate plan to pool mappings for group analysis
    SELECT carrierPlan.[RatePlanCode], customerPlan.[RatePlanCode],
           deviceResult.[CustomerRatePoolId], customerPool.[Name]
    FROM OptimizationDeviceResult deviceResult 
    LEFT JOIN CustomerRatePool customerPool ON deviceResult.[CustomerRatePoolId] = customerPool.[Id]
    GROUP BY rate plan and pool combinations
}

// Lines 821-830: Customer-specific vs shared pool separation
private static List<ResultRatePool> GenerateCustomerSpecificRatePools(List<ResultRatePool> crossOptimizationResultRatePools)
{
    return crossOptimizationResultRatePools.Where(pool => !pool.IsSharedRatePool).ToList();
}
```

---

## Complete Report Generation Workflow

### Master Process Algorithm
```
PHASE 1: Data Preparation
    - Retrieve device optimization results from database
    - Validate data completeness and accuracy
    - Apply billing period adjustments and proration
    
PHASE 2: Report Generation
    - Generate device assignment spreadsheet
    - Calculate cost savings analysis
    - Analyze rate plan utilization statistics
    - Create optimization group details
    
PHASE 3: Excel Compilation
    - Combine all reports into multi-sheet workbook
    - Apply formatting and validation rules
    - Add summary dashboard and key metrics
    
PHASE 4: Quality Assurance
    - Validate calculations and data consistency
    - Check formatting and presentation quality
    - Ensure all required data is included
    
PHASE 5: Delivery
    - Save final Excel workbook
    - Email reports to customer stakeholders
    - Update optimization tracking records
```

### Report Output Structure
**Multi-Sheet Excel Workbook**:
- **Sheet 1**: Device Assignment List (implementation instructions)
- **Sheet 2**: Cost Savings Summary (financial analysis)
- **Sheet 3**: Utilization Statistics (efficiency analysis)
- **Sheet 4**: Group Details (organizational analysis)
- **Sheet 5**: Executive Summary (key metrics dashboard)

### Key Performance Indicators
- **Device Coverage**: 100% of M2M devices included in assignments
- **Cost Accuracy**: All financial calculations validated and consistent
- **Implementation Readiness**: Clear instructions for all rate plan changes
- **Savings Verification**: Documented cost reductions with supporting analysis
- **Report Quality**: Professional formatting and comprehensive documentation

---

## Validation Standards

### Quality Checklist
✅ **Data Integrity**: All device assignments have valid rate plans and cost calculations  
✅ **Financial Accuracy**: Savings calculations are mathematically correct and reasonable  
✅ **Utilization Validity**: Rate plan utilization percentages are within expected ranges  
✅ **Group Consistency**: Device groupings are logical and non-overlapping  
✅ **Excel Quality**: Multi-sheet workbook with proper formatting and navigation  
✅ **Implementation Clarity**: Clear instructions for executing optimization recommendations  
✅ **Performance Standards**: Report generation completes within acceptable timeframes