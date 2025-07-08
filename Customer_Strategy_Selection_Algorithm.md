# Customer Strategy Selection Algorithm

## Overview
The customer strategy selection process involves four sequential steps to optimize SIM card rate plan assignments and achieve maximum customer savings.

---

## 1. Compare Customer Strategy Results

### What
Compares multiple optimization strategy results to identify the most cost-effective assignment solution.

### Why
- **Multiple Strategies**: Different strategies (NoGrouping, GroupByCommunicationPlan) produce different results
- **Cost Optimization**: Need to find the strategy that provides maximum savings
- **Resource Efficiency**: Avoid suboptimal assignments that waste plan allowances
- **Customer Value**: Deliver the best possible cost reduction

### How
The system runs multiple assignment strategies simultaneously and compares their total costs.

### Algorithm
```
INPUT: queueResults (list of strategy results with TotalCost)

STEP 1: Collect All Strategy Results
    strategyResults ← []
    FOR EACH queueId IN queueIds:
        result ← GetQueueResult(queueId)
        IF result.TotalCost IS NOT NULL AND result.RunEndTime IS NOT NULL:
            strategyResults.Add(result)
        END IF
    END FOR

STEP 2: Compare Total Costs
    bestResult ← NULL
    lowestCost ← INFINITY
    
    FOR EACH result IN strategyResults:
        IF result.TotalCost < lowestCost:
            lowestCost ← result.TotalCost
            bestResult ← result
        END IF
    END FOR

OUTPUT: bestResult (strategy with lowest total cost)
```

### Code Location
**File: `AltaworxSimCardCostOptimizerCleanup.cs`**
```csharp
// Lines 2074-2081: GetWinningQueueId function
protected long GetWinningQueueId(KeySysLambdaContext context, long commGroupId)
{
    using (var cmd = new SqlCommand("SELECT TOP 1 Id FROM OptimizationQueue WHERE CommPlanGroupId = @commGroupId AND TotalCost IS NOT NULL AND RunEndTime IS NOT NULL ORDER BY TotalCost ASC", conn))
    {
        // Returns queue with lowest TotalCost
    }
}
```

---

## 2. Select Best Customer Assignment

### What
Selects the winning optimization strategy and marks it as the best assignment solution.

### Why
- **Single Winner**: Only one strategy result should be implemented
- **Resource Cleanup**: Remove non-winning results to save storage
- **Decision Clarity**: Clear identification of chosen optimization approach
- **Implementation Ready**: Prepare selected assignment for execution

### How
The system identifies the queue with the lowest total cost and selects it as the winner.

### Algorithm
```
INPUT: commGroupId (communication plan group identifier)

STEP 1: Find Winning Queue
    winningQueueId ← GetWinningQueueId(commGroupId)
    
    IF winningQueueId = 0:
        LogError("No winning queue found")
        RETURN FALSE
    END IF

STEP 2: Select Best Assignment
    bestAssignment ← GetQueueResults(winningQueueId)
    
    IF bestAssignment IS NULL:
        LogError("No results found for winning queue")
        RETURN FALSE
    END IF

STEP 3: Mark Selection
    MarkQueueAsWinner(winningQueueId)
    LogInfo("Selected queue " + winningQueueId + " as best assignment")

OUTPUT: winningQueueId, bestAssignment
```

### Code Location
**File: `AltaworxSimCardCostOptimizerCleanup.cs`**
```csharp
// Lines 338-348: Selection process
var winningQueueId = GetWinningQueueId(context, commGroup.Id);
CleanupDeviceResultsForCommGroup(context, commGroup.Id, winningQueueId);
queueIds.Add(winningQueueId);
```

---

## 3. Validate Customer Savings

### What
Validates that the selected assignment strategy actually provides cost savings compared to current assignments.

### Why
- **Savings Verification**: Ensure optimization actually reduces costs
- **Quality Control**: Prevent assignments that increase customer costs
- **Business Validation**: Confirm financial benefit before implementation
- **Error Prevention**: Catch calculation errors or data issues

### How
The system compares the optimized total cost against current customer charges.

### Algorithm
```
INPUT: currentCost, optimizedCost, minimumSavingsThreshold

STEP 1: Calculate Savings
    totalSavings ← currentCost - optimizedCost
    savingsPercentage ← (totalSavings / currentCost) × 100

STEP 2: Validate Savings Amount
    IF totalSavings <= 0:
        LogWarning("No savings achieved")
        validationResult ← FALSE
    ELSE IF totalSavings < minimumSavingsThreshold:
        LogWarning("Savings below minimum threshold")
        validationResult ← FALSE
    ELSE:
        validationResult ← TRUE
    END IF

STEP 3: Validate Cost Reasonableness
    IF optimizedCost > (currentCost × 1.1):
        LogError("Optimized cost exceeds current cost by >10%")
        validationResult ← FALSE
    END IF

STEP 4: Record Validation
    LogSavingsValidation(totalSavings, savingsPercentage, validationResult)

OUTPUT: validationResult, totalSavings, savingsPercentage
```

### Code Location
**File: `AltaworxSimCardCostOptimizer.cs`**
```csharp
// Lines 386-390: Best result validation
var isSuccess = assigner.Best_Result != null;
if (isSuccess)
{
    var result = assigner.Best_Result;
    // Validate that Best_Result contains valid cost savings
}
```

**File: `AltaworxSimCardCostOptimizerCleanup.cs`**
```csharp
// Lines 1111-1117: Cost field validation
ChargeAmt = Convert.ToDecimal(rdr["ChargeAmt"].ToString()),
BaseRateAmount = !rdr.IsDBNull("BaseRateAmt") ? rdr.GetDecimal("BaseRateAmt") : 0,
RateChargeAmount = !rdr.IsDBNull("RateChargeAmt") ? rdr.GetDecimal("RateChargeAmt") : 0,
OverageChargeAmount = !rdr.IsDBNull("OverageChargeAmt") ? rdr.GetDecimal("OverageChargeAmt") : 0,
```

---

## 4. Record Customer Details

### What
Records the selected assignment details and customer information to the optimization results database.

### Why
- **Audit Trail**: Maintain record of optimization decisions
- **Implementation Data**: Provide data needed for rate plan changes
- **Reporting**: Enable customer savings reports and analytics
- **Compliance**: Meet record-keeping requirements for billing changes

### How
The system stores device assignments, rate plan mappings, and customer details in result tables.

### Algorithm
```
INPUT: customerId, queueId, optimizationResult, customerDetails

STEP 1: Record Device Assignments
    FOR EACH device IN optimizationResult.devices:
        INSERT INTO OptimizationDeviceResult (
            QueueId, AmopDeviceId, AssignedCustomerRatePlanId,
            CustomerRatePoolId, BaseRateAmt, RateChargeAmt, OverageChargeAmt
        )
    END FOR

STEP 2: Record Customer Information
    INSERT INTO OptimizationCustomerProcessing (
        ServiceProviderId, CustomerId, CustomerName, DeviceCount,
        StartTime, EndTime, IsProcessed, SessionId
    )

STEP 3: Record Rate Pool Mappings
    FOR EACH ratePool IN optimizationResult.ratePools:
        INSERT INTO CustomerRatePool (
            Name, RatePlanId, CustomerId, TotalCost, DeviceCount
        )
    END FOR

STEP 4: Update Processing Status
    MarkProcessedOptimizationInstanceTrackingRecord(customerId, sessionId)
    LogCustomerOptimizationComplete(customerId, totalSavings)

OUTPUT: recordingSuccess (boolean)
```

### Code Location
**File: `AltaworxSimCardCostOptimizer.cs`**
```csharp
// Lines 393-397: Record results calls
if (amopCustomerId.HasValue)
{
    RecordResults(context, result.QueueId, amopCustomerId.Value, commPlanGroupId, result, skipLowerCostCheck);
}
else
{
    RecordResults(context, result.QueueId, accountNumber, commPlanGroupId, result, skipLowerCostCheck);
}
```

**File: `AltaworxSimCardCostQueueCustomerOptimization.cs`**
```csharp
// Lines 201, 247: Customer processing record marking
optimizationRepository.MarkProcessedOptimizationInstanceTrackingRecord(context, optimizationSessionId, customerId, amopCustomerId);
optimizationRepository.MarkProcessedOptimizationInstanceTrackingRecord(context, optimizationSessionId, revCustomerId: null, customerIdentifier);
```

**File: `AltaworxSimCardCostQueueCustomerOptimization.cs`**
```csharp
// Lines 676-677: Record results to database
OptimizationResultDbWriter.RecordRatePool(context, context.ConnectionString, unusedQueueId, billingPeriodId.Value, simsWithNoRatePlanCodes);
OptimizationResultDbWriter.RecordTotalCost(context, context.ConnectionString, unusedQueueId, OptimizationConstant.DefaultUnassignedTotalCost);
```

---

## Complete Process Algorithm

### Integrated Customer Strategy Selection
```
INPUT: commGroupId, optimizationInstance, queueIds

STEP 1: Compare Strategy Results
    allResults ← GetAllQueueResults(queueIds)
    validResults ← FilterValidResults(allResults)

STEP 2: Select Best Assignment
    winningQueueId ← GetQueueWithLowestCost(validResults)
    bestAssignment ← GetQueueResults(winningQueueId)

STEP 3: Validate Savings
    currentCost ← GetCurrentCustomerCost(commGroupId)
    optimizedCost ← bestAssignment.TotalCost
    savingsValid ← ValidateSavings(currentCost, optimizedCost)
    
    IF NOT savingsValid:
        LogError("Savings validation failed")
        RETURN FALSE
    END IF

STEP 4: Record Customer Details
    recordingSuccess ← RecordOptimizationResults(bestAssignment, customerDetails)
    CleanupNonWinningResults(queueIds, winningQueueId)
    
    IF recordingSuccess:
        MarkOptimizationComplete(commGroupId, winningQueueId)
        LogOptimizationSuccess(commGroupId, totalSavings)
    END IF

OUTPUT: optimizationSuccess, totalSavings, winningQueueId
```

## Key Implementation Points

**Database Tables**
- `OptimizationQueue`: Stores strategy execution results with TotalCost
- `OptimizationDeviceResult`: Stores device-level assignment details
- `OptimizationCustomerProcessing`: Tracks customer optimization status
- `CustomerRatePool`: Maps customers to optimized rate pools

**Selection Criteria**
- Lowest TotalCost wins among completed queues
- Results must have non-null TotalCost and RunEndTime
- Savings validation ensures positive cost reduction

**Error Handling**
- Missing queue results logged and skipped
- Invalid cost calculations flagged for review
- Failed savings validation prevents implementation