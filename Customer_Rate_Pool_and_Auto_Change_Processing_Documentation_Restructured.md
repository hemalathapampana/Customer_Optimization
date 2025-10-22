# Customer Rate Pool and Auto Change Processing Documentation

## 1. Customer Rate Pool Processing: Device Grouping by Rate Pool ID

### What
Groups devices by customer rate pool ID for pooled optimization, creating logical collections of SIM cards that share the same billing pool characteristics for coordinated optimization processing.

### Why
Essential for ensuring devices with shared billing characteristics are optimized together, maintaining proper billing boundaries and maximizing cost savings potential. Prevents optimization conflicts between devices that should be managed as a unified group.

### How
Examines optimization SIM cards to group devices by their assigned customer rate pool identifier, processes each group separately through optimization algorithms, and handles both pooled and non-pooled device scenarios with appropriate rate plan matching.

### Algorithm
**ALGORITHM: ProcessCustomerRatePoolGrouping**  
**INPUT:** Optimization SIM Cards, Rate Plans, Billing Period  
**OUTPUT:** Grouped Device Collections for Optimization

**Step 1: Group Devices by Rate Pool ID**
- Examine all optimization SIM cards in the collection
- Group devices using CustomerRatePoolId as the grouping key
- Apply Distinct() to ensure unique rate pool groups
- Log rate pool ID for each group for tracking purposes

**Step 2: Extract Rate Plan Codes for Each Group**
- For each rate pool group, extract distinct CustomerRatePlanCode values
- Create collection of rate plan codes associated with devices in the group
- Filter out devices with null or empty rate plan codes

**Step 3: Process Pooled Rate Pool Groups**
- Check if rate pool ID is not null (indicates pooled devices)
- Match rate plans where PlanName contains the extracted rate plan codes
- Call ProcessRatePoolGroup with matched rate plans and device collection
- Pass rate pool ID and queue configuration parameters

**Step 4: Process Non-Pooled Auto Change Groups**
- For devices with null rate pool ID (non-pooled)
- Filter rate plans where AutoChangeRatePlan is enabled
- Group filtered rate plans by PlanName for compatibility
- Process each plan name group through ProcessPlanNameGroup method

**Step 5: Handle Processing Errors**
- Monitor for errors during rate pool or plan name group processing
- If error occurs, immediately return error status to stop optimization
- Log appropriate error messages for debugging and monitoring

**Step 6: Return Processing Status**
- Return false if all groups processed successfully
- Return true if any error occurred during processing

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
- **Primary Processing:** Lines 532, 818
```csharp
var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();

foreach (var simCardsByRatePoolId in simCardsByRatePoolIds)
{
    LogInfo(context, CommonConstants.INFO, $"RatePoolId: {simCardsByRatePoolId.Key}");
    // Get all rate plan codes from the devices
    var ratePlanCodes = simCardsByRatePoolId.Select(x => x.CustomerRatePlanCode).Distinct();
    var isError = false;
    if (simCardsByRatePoolId.Key != null)
    {
        // Get all rate plans with matching rate plan codes
        var ratePlansForPool = ratePlans.Where(x => ratePlanCodes.Contains(x.PlanName));
        isError = await ProcessRatePoolGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, billingPeriod, instanceId, chargeType, ratePlansForPool, simCardsByRatePoolId.ToList(), simCardsByRatePoolId?.Key, queuesPerInstance: QueuesPerInstance);
    }
    else
    {
        // Group rate plans by rate plan code and run auto change optimization logic for this group of devices
        var ratePlansByCodes = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan && ratePlanCodes.Contains(ratePlan.PlanName)).GroupBy(x => x.PlanName);
        foreach (var ratePlansByCode in ratePlansByCodes)
        {
            isError = await ProcessPlanNameGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, billingPeriod, instanceId, chargeType, ratePlansByCode, simCardsByRatePoolId.ToList());
        }
    }
    
    // If error, stop optimization midway
    if (isError)
    {
        return isError;
    }
}
```

---

## 2. Auto Change Processing: Dynamic Rate Plan Grouping

### What
Groups devices by rate plan code for dynamic rate plan changes, enabling automatic optimization across different rate plan options through systematic plan compatibility evaluation.

### Why
Essential for implementing cost-saving rate plan switches while maintaining service quality and ensuring customers are always on the most cost-effective plans. Enables automated optimization without manual intervention.

### How
Filters rate plans enabled for auto change functionality, groups them by plan name to ensure compatibility, and processes each group through optimization algorithms for automatic rate plan assignment with SIM pooling considerations.

### Algorithm
**ALGORITHM: ProcessAutoChangeRatePlans**  
**INPUT:** Rate Plans, Rate Plan Codes, Device Collections  
**OUTPUT:** Optimized Rate Plan Assignments

**Step 1: Filter Auto Change Enabled Rate Plans**
- Examine all available rate plans for AutoChangeRatePlan flag
- Filter to include only rate plans where AutoChangeRatePlan is true
- Match filtered plans with rate plan codes from device collection
- Create collection of eligible auto change rate plans

**Step 2: Group Rate Plans by Plan Name**
- Group filtered rate plans using PlanName as grouping key
- Ensure compatibility by grouping plans with identical names
- Create separate processing groups for each unique plan name
- Log plan name group information for tracking

**Step 3: Process Each Plan Name Group**
- For each plan name group, call ProcessPlanNameGroup method
- Pass device collection, billing period, and optimization parameters
- Handle both Rev and AMOP customer scenarios appropriately
- Monitor for processing errors and handle gracefully

**Step 4: Sub-Group by SIM Pooling Settings**
- Within each plan name group, further group by AllowsSimPooling flag
- Create separate processing paths for pooled vs non-pooled plans
- Log SIM pooling setting for each sub-group
- Ensure compatibility within pooling groups

**Step 5: Validate Rate Plan Constraints**
- Check for zero value rate plans (DataPerOverageCharge or OverageRate = 0)
- If zero value plans found, log exception and return error
- Validate device count meets minimum requirements for optimization
- Ensure rate plan count stays within acceptable limits

**Step 6: Execute Optimization Logic**
- Create communication plan group for valid rate plan groups
- Generate rate pool collection and permutation sequences
- Enqueue optimization runs for parallel processing
- Return success status for completed processing

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
- **Primary Processing:** Lines 549, 835
```csharp
// Group rate plans by rate plan code and run auto change optimization logic for this group of devices
var ratePlansByCodes = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan && ratePlanCodes.Contains(ratePlan.PlanName)).GroupBy(x => x.PlanName);
foreach (var ratePlansByCode in ratePlansByCodes)
{
    isError = await ProcessPlanNameGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, billingPeriod, instanceId, chargeType, ratePlansByCode, simCardsByRatePoolId.ToList());
}

// Within ProcessPlanNameGroup - further grouping by AllowsSimPooling
foreach (var ratePlanGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling))
{
    LogInfo(context, LogTypeConstant.Info, $"Allows SIM Pooling: {ratePlanGroup.Key}");
    
    // get rate plans for group
    var groupRatePlans = ratePlanGroup.ToList();
    var zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
    if (zeroValueRatePlans.Count > 0)
    {
        LogInfo(context, LogTypeConstant.Exception, $"The following rate plans in '{planNameGroup.Key}' has Data per Overage Charge or Overage Rate of 0. Please update to a non-zero value.{Environment.NewLine} {string.Join(',', zeroValueRatePlans.Select(ratePlan => ratePlan.PlanDisplayName))}");
        return true;
    }
}
```

---

## 3. Permutation Generation: Rate Plan Combination Creation

### What
Creates all valid rate plan combinations for testing to ensure comprehensive optimization coverage across different rate plan scenarios through systematic permutation generation.

### Why
Essential for maximizing cost savings by evaluating all possible rate plan assignments and selecting the most cost-effective combinations. Prevents suboptimal assignments by ensuring comprehensive evaluation coverage.

### How
Generates sequences of compatible rate plans using RatePoolAssigner, validates rate plan limits to prevent combinatorial explosion, and creates optimization queues for each permutation sequence with proper ordering.

### Algorithm
**ALGORITHM: GenerateRatePoolPermutations**  
**INPUT:** Rate Pool Collection, Billing Period, Communication Plan Group  
**OUTPUT:** Optimization Queues with Rate Plan Sequences

**Step 1: Initialize Permutation Generation**
- Log start of GenerateRatePoolSequences process
- Extract rate pools from rate pool collection
- Prepare for sequence generation with proper logging
- Set up data structures for queue creation

**Step 2: Generate Rate Pool Sequences**
- Call RatePoolAssigner.GenerateRatePoolSequences() with rate pools
- Generate all valid permutation combinations
- Each sequence represents different rate plan assignment scenario
- Log completion of sequence generation process

**Step 3: Prepare Queue Data Structure**
- Create DataTable for queue rate plan assignments
- Add columns: QueueId, CommGroup_RatePlanId, SequenceOrder, CreatedBy, CreatedDate
- Initialize data table for bulk queue creation
- Set up proper data types for each column

**Step 4: Create Queue for Each Sequence**
- Iterate through each generated rate pool sequence
- Create new optimization queue using CreateQueue method
- Pass instance ID, communication plan group ID, and service provider
- Configure queue with proper proration settings

**Step 5: Add Rate Plans to Queues**
- For each queue, call AddRatePlansToQueue with sequence
- Map rate pool sequence to communication group rate plan table
- Add sequence order for proper processing priority
- Accumulate rate plan assignments in data table

**Step 6: Finalize Queue Creation**
- Call CreateQueueRatePlans with accumulated data table
- Bulk insert all queue rate plan assignments
- Log successful queue creation for monitoring
- Return control to optimization processing

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
- **Primary Processing:** Lines 631-633
```csharp
LogInfo(context, LogTypeConstant.Sub, detail: $"Start GenerateRatePoolSequences for {ratePoolCollection.RatePools.Count} Rate Plans");
var ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools);
LogInfo(context, LogTypeConstant.Sub, "End GenerateRatePoolSequences");

var dtQueueRatePlan = new DataTable();
dtQueueRatePlan.Columns.Add("QueueId", typeof(long));
dtQueueRatePlan.Columns.Add("CommGroup_RatePlanId", typeof(long));
dtQueueRatePlan.Columns.Add("SequenceOrder", typeof(int));
dtQueueRatePlan.Columns.Add("CreatedBy");
dtQueueRatePlan.Columns.Add("CreatedDate", typeof(DateTime));

foreach (var ratePoolSequence in ratePoolSequences)
{
    // add queue for rate plan permutation
    var queueId = CreateQueue(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId, usesProration);

    // add rate plans to queue
    var dtQueueRatePlanTemp = AddRatePlansToQueue(queueId, ratePoolSequence, commGroupRatePlanTable);
    if (dtQueueRatePlanTemp != null && dtQueueRatePlanTemp.Rows.Count > 0)
    {
        foreach (DataRow dr in dtQueueRatePlanTemp.Rows)
        {
            dtQueueRatePlan.Rows.Add(dr.ItemArray);
        }
    }
}

CreateQueueRatePlans(context, dtQueueRatePlan);
```

---

## 4. Queue Creation: Parallel Processing Queue Generation

### What
Generates optimization queues for parallel processing to enable concurrent execution of optimization algorithms across multiple rate plan combinations through distributed queue management.

### Why
Essential for reducing processing time and improving system performance by distributing computational workload across multiple parallel execution paths. Enables scalable optimization for large customer bases.

### How
Creates individual queues for each rate plan permutation, assigns queues to communication plan groups for batch processing, and configures queue parameters for optimal parallel execution with proper resource allocation.

### Algorithm
**ALGORITHM: CreateParallelOptimizationQueues**  
**INPUT:** Rate Pool Sequences, Instance ID, Communication Plan Group  
**OUTPUT:** Configured Optimization Queues for Parallel Processing

**Step 1: Initialize Queue Creation Process**
- Prepare queue creation for each rate pool sequence
- Set up communication plan group association
- Configure service provider and proration settings
- Initialize queue tracking variables

**Step 2: Create Individual Optimization Queue**
- Call CreateQueue method with instance and group parameters
- Pass service provider ID from billing period
- Configure queue with proration settings from optimization context
- Generate unique queue ID for tracking and processing

**Step 3: Assign Rate Plans to Queue**
- Call AddRatePlansToQueue with queue ID and rate pool sequence
- Map sequence to communication group rate plan table
- Assign sequence order for processing priority
- Return data table with rate plan assignments

**Step 4: Accumulate Queue Data**
- Check if rate plan data table is valid and contains rows
- Iterate through each data row in temporary table
- Add each row to master queue rate plan data table
- Maintain proper data structure for bulk operations

**Step 5: Execute Bulk Queue Creation**
- Call CreateQueueRatePlans with accumulated data table
- Perform bulk insert of all queue rate plan assignments
- Ensure database consistency and proper indexing
- Log successful queue creation for monitoring

**Step 6: Enqueue for Parallel Processing**
- Call EnqueueOptimizationRunsAsync with communication plan groups
- Pass charge type, queue limits, and customer optimization flags
- Enable skipLowerCostCheck for customer optimization scenarios
- Configure parallel processing with QueuesPerInstance limit

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
- **Primary Processing:** Line 645
```csharp
foreach (var ratePoolSequence in ratePoolSequences)
{
    // add queue for rate plan permutation
    var queueId = CreateQueue(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId, usesProration);

    // add rate plans to queue
    var dtQueueRatePlanTemp = AddRatePlansToQueue(queueId, ratePoolSequence, commGroupRatePlanTable);
    if (dtQueueRatePlanTemp != null && dtQueueRatePlanTemp.Rows.Count > 0)
    {
        foreach (DataRow dr in dtQueueRatePlanTemp.Rows)
        {
            dtQueueRatePlan.Rows.Add(dr.ItemArray);
        }
    }
}

CreateQueueRatePlans(context, dtQueueRatePlan);

// enqueue rate plan permutations
await EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true);

// For unused devices - separate queue creation
var unusedCommPlanGroupId = CreateCommPlanGroup(context, instanceId);
var unusedQueueId = CreateQueue(context, instanceId, unusedCommPlanGroupId, null, usesProration);
StartQueue(context, unusedQueueId, string.Empty);
```

---

## Rate Plan Permutation Logic

### What
Generates sequences of compatible rate plans for testing while implementing safeguards to prevent combinatorial explosion and ensure optimal resource utilization.

### Why
Essential for comprehensive optimization coverage while maintaining system performance and preventing resource exhaustion from excessive permutation combinations.

### How
Validates rate plan limits, filters invalid combinations, generates sequences through RatePoolAssigner, and orders sequences by cost optimization potential.

### Algorithm
**ALGORITHM: ValidateAndGeneratePermutations**  
**INPUT:** Calculated Rate Plans, Plan Name Group, Rate Plan Group  
**OUTPUT:** Valid Rate Plan Permutations or Error Status

**Step 1: Validate Maximum Rate Plan Limit**
- Check if calculatedPlans.Count exceeds OptimizationConstant.RatePlanLimit (15)
- If limit exceeded, log exception with rate plan code information
- Include guidance to reduce options to 15 or less
- Continue to next group without processing current group

**Step 2: Validate Minimum Rate Plan Requirement**
- Check if calculatedPlans.Count is less than or equal to RatePlanMinimumLimit (2)
- If below minimum, log informational message
- Use LogCommonStrings.AUTO_CHANGE_MINIMUM_RATE_PLAN_LIMIT_REACHED format
- Continue to next group as optimization requires multiple options

**Step 3: Filter Zero Value Rate Plans**
- Find rate plans where DataPerOverageCharge equals 0.0M
- Find rate plans where OverageRate equals 0.0M
- Combine results to create zeroValueRatePlans collection
- If any found, log exception with plan display names and return error

**Step 4: Validate Device Count for Optimization**
- Check if baseAssignedSimCardsCount exceeds BaseAssignedDeviceLimit
- If insufficient devices, skip permutation logic
- Log informational message with device count and rate plan IDs
- Optimization by permutation requires sufficient device population

**Step 5: Generate Rate Pool Collection**
- Calculate maximum average usage using RatePoolCalculator
- Create rate pools using RatePoolFactory with billing period and charge type
- Generate rate pool collection using RatePoolCollectionFactory
- Prepare collection for permutation sequence generation

**Step 6: Create Permutation Sequences**
- Call GeneratePermutationQueueRatePlans method
- Pass rate pool collection and communication plan group table
- Generate all valid sequences and create corresponding queues
- Enqueue optimization runs for parallel processing

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
- **Validation Lines:** 605-607, 610-613, 572-577
```csharp
// Rate plan limit validation
if (calculatedPlans.Count > OptimizationConstant.RatePlanLimit)
{
    LogInfo(context, LogTypeConstant.Exception, $"The rate plan count exceeds the limit of 15 for this Rate Plan Code {ratePlanGroup.Key}. Please cut down the options to 15 or less for this Rate Plan Code.");
    continue;
}
if (calculatedPlans.Count <= OptimizationConstant.RatePlanMinimumLimit)
{
    LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.AUTO_CHANGE_MINIMUM_RATE_PLAN_LIMIT_REACHED, calculatedPlans.Count, planNameGroup.Key, ratePlanGroup.Key));
    continue;
}

// Zero value rate plan filtering  
var zeroValueRatePlans = groupRatePlans.FindAll(x => x.DataPerOverageCharge == 0.0M || x.OverageRate == 0.0M);
if (zeroValueRatePlans.Count > 0)
{
    LogInfo(context, LogTypeConstant.Exception, $"The following rate plans in '{planNameGroup.Key}' has Data per Overage Charge or Overage Rate of 0. Please update to a non-zero value.{Environment.NewLine} {string.Join(',', zeroValueRatePlans.Select(ratePlan => ratePlan.PlanDisplayName))}");
    return true;
}

// Device count validation
if (baseAssignedSimCardsCount > OptimizationConstant.BaseAssignedDeviceLimit)
{
    GeneratePermutationQueueRatePlans(context, usesProration, billingPeriod, instanceId, commPlanGroupId, ratePoolCollection, commGroupRatePlanTable);
    
    // enqueue rate plan permutations
    await EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true);
}
else
{
    LogInfo(context, LogTypeConstant.Info, $"Plan name group for the rate plans {string.Join(',', ratePlanGroup.Select(plan => plan.Id).ToList())} only have {baseAssignedSimCardsCount} devices. The optimization by permutation logic will not be triggered.");
}
```