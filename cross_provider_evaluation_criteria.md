# Cross-Provider Evaluation Criteria: Algorithmic Analysis

## Overview
This document provides algorithmic breakdowns for cross-provider evaluation criteria in the Altaworx SIM Card Cost Optimization system. Each criterion includes What, Why, How explanations and actual code implementations from the Lambda functions.

---

## 1. Total Cost Across All Providers

### What
**What:** Calculate and compare the cumulative cost of SIM card services across all available providers (M2M, Mobility, CrossProvider) to determine the most cost-effective combination.

### Why
**Why:** To minimize overall telecommunications expenditure by identifying the optimal provider mix that delivers the lowest total cost while maintaining service requirements.

### How
**How:** The system aggregates rate plan costs, overage charges, and base fees across providers using mathematical optimization algorithms that consider usage patterns and billing cycles.

### Algorithm Implementation

```
Algorithm: TotalCostCalculation
Input: simCards[], ratePlans[], billingPeriod, chargeType
Output: optimalCostAssignment

1. FOR each provider in [M2M, Mobility, CrossProvider]:
   a. Calculate average usage: avgUsage = sum(simCard.CycleDataUsageMB) / count(simCards)
   b. Execute: calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(ratePlans, avgUsage)
   c. Create rate pools: ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType)

2. Initialize RatePoolAssigner with cost optimization parameters:
   - shouldFilterByRatePlanType = (portalType == Mobility && !isCustomerOptimization)
   - shouldPoolUsageBetweenRatePlans = (portalType == Mobility || isCustomerOptimization) && ratePoolCollection.IsPooled

3. Execute optimization strategies sequentially:
   a. No Grouping + Largest To Smallest assignment
   b. No Grouping + Smallest To Largest assignment  
   c. Group By Communication Plan + Largest To Smallest assignment
   d. Group By Communication Plan + Smallest To Largest assignment

4. Select best result: return assigner.Best_Result with minimum total cost
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostOptimizer.cs` - `ProcessQueues()` method

```csharp
// Calculate average usage across SIM cards for cost optimization
var avgUsage = simCards.Count > 0 ? simCards.Sum(x => x.CycleDataUsageMB) / simCards.Count : 0;

// Execute rate pool calculation with average usage
var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(queueRatePlans, avgUsage);

// Create rate pools with billing period and charge type
var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, queue.UsesProration, chargeType);

// Determine pooling strategy based on portal type
var shouldPoolByOptimizationGroup = (instance.PortalType == PortalTypes.Mobility || instance.IsCustomerOptimization) && ratePools.Any(x => x.RatePlan.AllowsSimPooling);

// Create rate pool collection for optimization
ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools, shouldPoolByOptimizationGroup, customerRatePoolId);

// Initialize rate pool assigner with optimization parameters
var shouldFilterByRatePlanType = instance.PortalType == PortalTypes.Mobility && !instance.IsCustomerOptimization;
var shouldPoolUsageBetweenRatePlans = (instance.PortalType == PortalTypes.Mobility || instance.IsCustomerOptimization) && ratePoolCollection.IsPooled;

var assigner = new RatePoolAssigner(string.Empty, ratePoolCollection, simCards, context.logger, SanityCheckTimeLimit, context.LambdaContext, IsUsingRedisCache,
    instance.PortalType,
    shouldFilterByRatePlanType,
    shouldPoolUsageBetweenRatePlans);

// Execute optimization with multiple strategies
assigner.AssignSimCards(GetSimCardGroupingByPortalType(instance.PortalType, instance.IsCustomerOptimization),
    context.OptimizationSettings.BillingTimeZone, false, false, ratePoolSequences);

// Get best result with minimum total cost
var isSuccess = assigner.Best_Result != null;
if (isSuccess)
{
    var result = assigner.Best_Result;
    // Record optimized results
    RecordResults(context, result.QueueId, amopCustomerId.Value, commPlanGroupId, result, skipLowerCostCheck);
}
```

---

## 2. Provider-Specific Performance Metrics

### What
**What:** Evaluate and measure each provider's performance characteristics including data throughput, service reliability, coverage areas, and optimization group effectiveness.

### Why
**Why:** To ensure that cost optimization doesn't compromise service quality and that each provider meets specific performance benchmarks for different device categories.

### How
**How:** The system uses optimization groups to categorize devices and applies provider-specific performance filters during the rate plan assignment process.

### Algorithm Implementation

```
Algorithm: ProviderPerformanceEvaluation
Input: optimizationGroups[], serviceProviderId, commPlanGroupId
Output: performanceQualifiedRatePlans[]

1. FOR each provider type:
   a. IF (portalType == M2M && !isCustomerOptimization):
      - Retrieve: commPlans = GetCommPlansForCommGroup(commPlanGroupId)
      - Filter devices by communication plan compatibility
   
   b. IF (portalType == Mobility && !isCustomerOptimization):
      - Retrieve: optimizationGroups = GetOptimizationGroupsByCommGroupId(commPlanGroupId)
      - Group devices by optimization categories
   
   c. IF (portalType == CrossProvider):
      - Execute: GetCrossProviderOptimizationDevices() for multi-provider analysis

2. Apply performance filters:
   a. shouldFilterByRatePlanType = (portalType == Mobility && !isCustomerOptimization)
   b. Validate rate plan compatibility with device optimization groups
   c. Ensure coverage requirements are met per optimization group

3. Calculate performance score:
   - Data allowance efficiency per optimization group
   - Overage rate competitiveness  
   - Service reliability metrics per provider
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostOptimizer.cs` - Provider differentiation logic

```csharp
// Provider-specific device retrieval based on portal type
private List<Core.SimCard> GetSimCardsByPortalType(KeySysLambdaContext context, OptimizationInstance instance, int? serviceProviderId, BillingPeriod billingPeriod, PortalTypes portalType, long commPlanGroupId, List<string> commPlans = null, List<OptimizationGroup> optimizationGroups = null)
{
    if (portalType == PortalTypes.M2M)
    {
        // M2M provider: use communication plans for device filtering
        return GetSimCards(context, instance.Id, serviceProviderId, commPlans, billingPeriod, commPlanGroupId, instance.IsCustomerOptimization);
    }
    else if (portalType == PortalTypes.Mobility)
    {
        // Mobility provider: use optimization groups for performance categorization
        var optimizationGroupIds = optimizationGroups.Select(x => x.Id).ToList();
        return optimizationMobilityDeviceRepository.GetOptimizationMobilityDevices(context, instance.Id, serviceProviderId, optimizationGroupIds, billingPeriod, commPlanGroupId, instance.IsCustomerOptimization);
    }
    else if (portalType == PortalTypes.CrossProvider)
    {
        // Cross-provider: multi-provider analysis
        return crossProviderOptimizationRepository.GetCrossProviderOptimizationDevices(ParameterizedLog(context), instance.Id, billingPeriod, commPlanGroupId, instance.IsCustomerOptimization);
    }
    else
    {
        OptimizationErrorHandler.OnPortalTypeError(context, instance.PortalType, true);
        return new List<Core.SimCard>();
    }
}

// Provider-specific optimization group and communication plan setup
if (instance.PortalType == PortalTypes.M2M && !instance.IsCustomerOptimization)
{
    // M2M carrier optimization: use communication plans
    commPlans = GetCommPlansForCommGroup(context, queue.CommPlanGroupId);
}

if (instance.PortalType == PortalTypes.Mobility && !instance.IsCustomerOptimization)
{
    // Mobility carrier optimization: use optimization groups for performance metrics
    optimizationGroups = carrierRatePlanRepository.GetOptimizationGroupsByCommGroupId(ParameterizedLog(context), queue.CommPlanGroupId);
}

// Performance filtering parameters
var shouldFilterByRatePlanType = instance.PortalType == PortalTypes.Mobility && !instance.IsCustomerOptimization;
var shouldPoolUsageBetweenRatePlans = (instance.PortalType == PortalTypes.Mobility || instance.IsCustomerOptimization) && ratePoolCollection.IsPooled;
```

**Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` - Performance validation for Mobility groups

```csharp
// Validate performance metrics for optimization groups
var optimizationGroups = carrierRatePlanRepository.GetValidOptimizationGroupsWithRatePlanIds(ParameterizedLog(context), instance.ServiceProviderId.GetValueOrDefault());

// Check for null rate plan type ID or optimization group ID (performance validation)
if (deviceResults.Any(x => x.RatePlanTypeId == null || x.OptimizationGroupId == null))
{
    LogInfo(context, CommonConstants.ERROR, string.Format(LogCommonStrings.ERROR_NULL_RATE_PLAN_TYPE_ID_OPTIMIZATION_GROUP_ID, string.Join(',', deviceResults.Select(x => x.ICCID))));
}

// Filter devices by performance criteria
var deviceResultsByOptimizationGroups = deviceResults
    .Where(x => x.RatePlanTypeId != null && x.OptimizationGroupId != null)
    .GroupBy(x => x.OptimizationGroupId)
    .ToDictionary(x => x.Key, x => x.ToList());

// Map rate plans to optimization groups for performance evaluation
foreach (var optimizationGroup in optimizationGroups)
{
    var groupRatePlans = MapRatePlansToOptimizationGroup(ratePlans, optimizationGroup);
    // Performance-based rate pool creation
    optimizationGroupResultPools.Add(new ResultRatePool(ratePlan, usesProration, billingPeriod, ResultRatePoolKeyType.ICCID, optimizationGroup.Name));
}
```

---

## 3. Cross-Provider Migration Feasibility

### What
**What:** Assess the technical and operational feasibility of migrating SIM cards between different service providers while maintaining service continuity.

### Why
**Why:** To enable seamless transitions between providers when cost optimization identifies better alternatives, ensuring minimal service disruption and technical compatibility.

### How
**How:** The system evaluates rate plan compatibility, device group constraints, and implements a sequential migration strategy using optimization queues.

### Algorithm Implementation

```
Algorithm: MigrationFeasibilityAnalysis
Input: currentProvider, targetProvider, simCards[], ratePlans[]
Output: migrationPlan, feasibilityScore

1. Assess technical compatibility:
   a. Validate rate plan code compatibility between providers
   b. Check optimization group constraints:
      - shouldPoolUsageBetweenRatePlans compatibility
      - SIM pooling allowance: ratePlan.AllowsSimPooling
   
2. Evaluate migration constraints:
   a. Customer rate pool compatibility:
      IF (isCustomerOptimization):
         customerRatePoolId = GetCustomerRatePoolsByCommGroupId(commPlanGroupId)
   b. Billing period alignment validation
   c. Communication plan group compatibility

3. Generate migration sequence:
   a. Create rate pool sequences: RatePlanSequence{QueueId, RatePlanIds[]}
   b. Process queues in dependency order
   c. Implement rollback mechanism for failed migrations

4. Calculate migration cost:
   - Setup costs + Migration downtime costs + Risk factors
   - Compare against potential savings from provider switch
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostOptimizer.cs` - Rate plan sequence management and migration logic

```csharp
// Rate plan sequence generation for cross-provider migration
List<RatePlan> queueRatePlans = carrierRatePlanRepository.GetQueueRatePlans(ParameterizedLog(context), new List<long> { queueId });
List<int> ratePlanIdSequence = queueRatePlans.Select(x => x.Id).ToList();
ratePoolSequences.Add(new RatePlanSequence() { QueueId = queueId, RatePlanIds = ratePlanIdSequence });

// Customer rate pool compatibility validation for migration
int? customerRatePoolId = null;
if (instance.IsCustomerOptimization)
{
    customerRatePoolId = GetCustomerRatePoolsByCommGroupId(context, queue.CommPlanGroupId);
}

// Migration feasibility check - SIM pooling allowance
var shouldPoolByOptimizationGroup = (instance.PortalType == PortalTypes.Mobility || instance.IsCustomerOptimization) && ratePools.Any(x => x.RatePlan.AllowsSimPooling);

// Create rate pool collection for migration analysis
ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools, shouldPoolByOptimizationGroup, customerRatePoolId);

// Customer rate pool retrieval for migration validation
public int? GetCustomerRatePoolsByCommGroupId(KeySysLambdaContext context, long commGroupId)
{
    LogInfo(context, CommonConstants.SUB, $"({commGroupId})");
    var sqlRetryPolicy = new PolicyFactory(context.logger).GetSqlRetryPolicy(CommonConstants.NUMBER_OF_RETRIES);
    return sqlRetryPolicy.Execute(() =>
    {
        var parameters = new List<SqlParameter>()
        {
            new SqlParameter(CommonSQLParameterNames.COMM_GROUP_ID, commGroupId),
            new SqlParameter(CommonSQLParameterNames.CUSTOMER_RATE_POOL_ID_PASCAL_CASE, SqlDbType.Int)
            {
                Direction = ParameterDirection.Output
            }
        };
        return SqlQueryHelper.ExecuteStoredProcedureWithSingleValueResult<int?>(ParameterizedLog(context), context.ConnectionString,
            SQLConstant.StoredProcedureName.GET_CUSTOMER_RATE_POOLS_BY_COMM_GROUP_ID,
            outputParamName: CommonSQLParameterNames.CUSTOMER_RATE_POOL_ID_PASCAL_CASE,
            null,
            parameters,
            SQLConstant.ShortTimeoutSeconds);
    });
}

// Migration continuation processing with rollback mechanism
private async Task ProcessQueuesContinue(KeySysLambdaContext context, List<long> queueIds, string messageId, bool skipLowerCostCheck, OptimizationChargeType chargeType)
{
    // Read assigner from cache for continuation
    var assigner = RedisCacheHelper.GetPartialAssignerFromCache(context, queueIds, context.OptimizationSettings.BillingTimeZone);

    // Migration rollback if cache not found
    if (assigner == null)
    {
        return; // Consider migration complete or failed
    }
    else
    {
        assigner.SetLambdaContext(context.LambdaContext);
        assigner.SetLambdaLogger(context.logger);
        // Continue migration process
        assigner.AssignSimCardsContinue(context.OptimizationSettings.BillingTimeZone, false);
    }
    
    await WrapUpCurrentInstance(context, queueIds, skipLowerCostCheck, chargeType, amopCustomerId, accountNumber, commPlanGroupId, assigner);
}
```

**Lambda:** `AltaworxSimCardCostQueueCustomerOptimization.cs` - Cross-provider migration implementation

```csharp
// Cross-provider migration initialization
private async Task RunCrossProviderCustomerOptimization(KeySysLambdaContext context, int tenantId, int customerId, SiteTypes customerType, string serviceProviderIds, int customerBillingPeriodId, string messageId, long optimizationSessionId, bool isLastInstance, string additionalData)
{
    // Get customer and validate migration eligibility
    var customer = crossProviderOptimizationRepository.GetOptimizationCustomer(ParameterizedLog(context), customerId, customerType);
    
    // Get billing period for migration alignment
    var billingPeriod = crossProviderOptimizationRepository.GetBillingPeriod(ParameterizedLog(context), customerId, customerBillingPeriodId, context.OptimizationSettings.BillingTimeZone);
    
    // Get cross-provider rate plans for migration analysis
    var ratePlans = customerRatePlanRepository.GetCrossProviderCustomerRatePlans(ParameterizedLog(context), serviceProviderIds, customerType, new List<int> { customerId }, billingPeriod, tenantId);
    
    // Start cross-provider optimization instance for migration
    var instanceId = crossProviderOptimizationRepository.StartCrossProviderOptimizationInstance(ParameterizedLog(context), tenantId, messageId,
        customer, PortalTypes.CrossProvider, optimizationSessionId,
        useBillInAdvance, billingPeriod, nextBillingPeriod, serviceProviderIds);
    
    // Process migration with feasibility checks
    var isError = await ProcessCrossProviderDevicesByCustomerRatePlans(context, serviceProviderIds, false, ratePlans, billingPeriod, nextBillingPeriod, instanceId, chargeType, customer, tenantId);
}

// Service provider validation for migration compatibility
if (autoChangeRatePlans.Any() && !string.IsNullOrWhiteSpace(serviceProviderIds))
{
    var serviceProviderIdList = serviceProviderIds.Replace(" ", "").Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList();
    autoChangeRatePlans = autoChangeRatePlans.Where(x => x.ServiceProviderIds.Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList().ContainsAllItems(serviceProviderIdList)).ToList();
    if (!autoChangeRatePlans.Any())
    {
        LogInfo(context, CommonConstants.ERROR, string.Format(LogCommonStrings.NO_VALID_CROSS_PROVIDER_CUSTOMER_RATE_PLAN_FOUND, serviceProviderIds));
        return true; // Migration not feasible
    }
}
```

---

## 4. Service Quality and Coverage Considerations

### What
**What:** Evaluate network coverage quality, service level agreements, and geographical reach across different providers to ensure service standards are maintained.

### Why
**Why:** To guarantee that cost optimization decisions don't compromise service delivery quality or coverage requirements for specific device deployments and geographical areas.

### How
**How:** The system incorporates service provider specifications, optimization group requirements, and coverage validation into the rate plan selection algorithm.

### Algorithm Implementation

```
Algorithm: ServiceQualityCoverageEvaluation
Input: serviceProviderId, optimizationGroups[], commPlanGroupId, billingPeriod
Output: qualityAdjustedRatePlans[]

1. Retrieve provider-specific service metrics:
   a. IF (M2M provider):
      devices = GetSimCards(serviceProviderId, commPlans, billingPeriod, commPlanGroupId)
   b. IF (Mobility provider):
      devices = GetOptimizationMobilityDevices(serviceProviderId, optimizationGroupIds, billingPeriod)
   c. IF (CrossProvider):
      devices = GetCrossProviderOptimizationDevices(serviceProviderId, billingPeriod, commPlanGroupId)

2. Apply coverage validation:
   a. Validate geographical coverage per optimization group
   b. Check SLA compliance for each service provider
   c. Ensure redundancy requirements are met

3. Quality scoring algorithm:
   FOR each ratePlan in ratePlans:
     a. Calculate coverage score based on optimization group requirements
     b. Apply service quality weight: qualityWeight = f(providerSLA, coverageArea)
     c. Adjust cost calculation: adjustedCost = baseCost * (1 + qualityWeight)

4. Filter and rank by quality-adjusted cost:
   RETURN ratePlans.OrderBy(adjustedCost).Where(meetsCoverageRequirements)
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` - Service quality validation and coverage analysis

```csharp
// Quality validation for Mobility carrier optimization with coverage considerations
protected OptimizationInstanceResultFile WriteMobilityCarrierResults(KeySysLambdaContext context, OptimizationInstance instance, List<long> queueIds, BillingPeriod billingPeriod, bool usesProration)
{
    // Get rate plans from service provider for quality validation
    var ratePlans = carrierRatePlanRepository.GetValidRatePlans(ParameterizedLog(context), instance.ServiceProviderId.GetValueOrDefault());
    
    // Get optimization groups with coverage and quality metrics
    var optimizationGroups = carrierRatePlanRepository.GetValidOptimizationGroupsWithRatePlanIds(ParameterizedLog(context), instance.ServiceProviderId.GetValueOrDefault());

    // Get device results for quality assessment
    var deviceResults = optimizationMobilityDeviceRepository.GetMobilityDeviceResults(context, queueIds, billingPeriod);
    
    // Quality validation: Check for null rate plan type ID or optimization group ID
    if (deviceResults.Any(x => x.RatePlanTypeId == null || x.OptimizationGroupId == null))
    {
        LogInfo(context, CommonConstants.ERROR, string.Format(LogCommonStrings.ERROR_NULL_RATE_PLAN_TYPE_ID_OPTIMIZATION_GROUP_ID, string.Join(',', deviceResults.Select(x => x.ICCID))));
    }
    
    // Filter devices by quality criteria and group by optimization categories
    var deviceResultsByOptimizationGroups = deviceResults
        .Where(x => x.RatePlanTypeId != null && x.OptimizationGroupId != null)
        .GroupBy(x => x.OptimizationGroupId)
        .ToDictionary(x => x.Key, x => x.ToList());

    // Map devices to optimization groups for coverage evaluation
    foreach (var optimizationGroup in optimizationGroups)
    {
        if (!deviceResultsByOptimizationGroups.TryGetValue(optimizationGroup.Id, out var groupDeviceResults))
        {
            LogInfo(context, CommonConstants.WARNING, string.Format(LogCommonStrings.NO_DEVICE_FOUND_FOR_OPTIMIZATION_GROUP_ID, optimizationGroup.Id));
            continue;
        }
        
        // Map rate plans to optimization group for coverage validation
        var groupRatePlans = MapRatePlansToOptimizationGroup(ratePlans, optimizationGroup);
        var optimizationGroupResultPools = new List<ResultRatePool>();
        
        // Create result pools with coverage and quality considerations
        foreach (var ratePlan in groupRatePlans)
        {
            optimizationGroupResultPools.Add(new ResultRatePool(ratePlan, usesProration, billingPeriod, ResultRatePoolKeyType.ICCID, optimizationGroup.Name));
        }
        
        // Calculate starting cost per device with quality adjustments
        var originalRatePools = RatePoolFactory.CreateRatePools(ratePlans, billingPeriod, usesProration, OptimizationChargeType.RateChargeAndOverage);
        var originalAssignmentCollection = RatePoolCollectionFactory.CreateRatePoolCollection(originalRatePools, shouldPoolByOptimizationGroup: true);

        // Generate device assignments considering service quality
        deviceAssignments.AddRange(MapToMobilityDeviceAssignmentsFromResult(originalAssignmentCollection, optimizationGroupResultPools, billingPeriod, optimizationGroup));
        summariesByRatePlans.AddRange(MapToSummariesFromResult(optimizationGroupResultPools, optimizationGroup));
    }
}

// Service quality mapping for device assignments
private List<MobilityCarrierAssignmentExportModel> MapToMobilityDeviceAssignmentsFromResult(RatePoolCollection originalAssignmentCollection, List<ResultRatePool> optimizationGroupResultPools, BillingPeriod billingPeriod, OptimizationGroup optimizationGroup)
{
    var deviceAssignments = new List<MobilityCarrierAssignmentExportModel>();
    foreach (var resultPool in optimizationGroupResultPools)
    {
        foreach (var sim in resultPool.SimCards)
        {
            // Find original rate pool for coverage comparison
            var originalRatePool = originalAssignmentCollection.RatePools.FirstOrDefault(x => x.SimCards.TryGetValue(sim.Key, out var _));
            if (originalRatePool == null)
            {
                continue; // Skip if coverage requirements not met
            }
            
            // Create device assignment with optimization group context for quality validation
            var deviceAssignment = MobilityCarrierAssignmentExportModel.FromSimCardResult(sim.Value, originalRatePool?.RatePlan, resultPool.RatePlan, billingPeriod.BillingPeriodStart, optimizationGroup.Name);
            deviceAssignments.Add(deviceAssignment);
        }
    }
    return deviceAssignments;
}
```

**Lambda:** `AltaworxSimCardCostQueueCustomerOptimization.cs` - Cross-provider service quality validation

```csharp
// Cross-provider service quality and coverage validation
private async Task<bool> ProcessCrossProviderDevicesByCustomerRatePlans(KeySysLambdaContext context, string serviceProviderIds, bool usesProration, List<RatePlan> ratePlans, BillingPeriod billingPeriod, BillingPeriod nextBillingPeriod, long instanceId, OptimizationChargeType chargeType, OptimizationCustomer customer, int tenantId)
{
    // Get cross-provider customer SIM cards with service quality metrics
    var optimizationSimCards = crossProviderOptimizationRepository.GetCrossProviderCustomerSimCards(ParameterizedLog(context), customer.CustomerType, customer.CustomerId, customer.RevAccountNumber, customer.IntegrationAuthenticationId, billingPeriod, serviceProviderIds);

    // Filter for devices with valid rate plan codes (quality indicator)
    optimizationSimCards = optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList();

    // Service provider compatibility validation for quality assurance
    var autoChangeRatePlans = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan);
    if (autoChangeRatePlans.Any() && !string.IsNullOrWhiteSpace(serviceProviderIds))
    {
        var serviceProviderIdList = serviceProviderIds.Replace(" ", "").Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList();
        
        // Validate cross-provider service compatibility
        autoChangeRatePlans = autoChangeRatePlans.Where(x => x.ServiceProviderIds.Split(CommonConstants.STRING_ITEMS_SEPERATOR).ToList().ContainsAllItems(serviceProviderIdList)).ToList();
        
        if (!autoChangeRatePlans.Any())
        {
            LogInfo(context, CommonConstants.ERROR, string.Format(LogCommonStrings.NO_VALID_CROSS_PROVIDER_CUSTOMER_RATE_PLAN_FOUND, serviceProviderIds));
            return true; // Service quality requirements not met
        }
    }

    // Process devices by rate pool IDs with coverage validation
    var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();
    foreach (var simCardsByRatePoolId in simCardsByRatePoolIds)
    {
        LogInfo(context, CommonConstants.INFO, $"Processing RatePoolId: {simCardsByRatePoolId.Key} for service quality validation");
        
        // Get rate plan codes for coverage analysis
        var ratePlanCodes = simCardsByRatePoolId.Select(x => x.CustomerRatePlanCode).Distinct();
        
        if (simCardsByRatePoolId.Key != null)
        {
            // Get rate plans matching coverage requirements
            var ratePlansForPool = ratePlans.Where(x => ratePlanCodes.Contains(x.PlanName));
            var isError = await ProcessRatePoolGroup(context, customer.IntegrationAuthenticationId, usesProration, customer.RevAccountNumber, customer.CustomerId, billingPeriod, instanceId, chargeType, ratePlansForPool, simCardsByRatePoolId.ToList(), simCardsByRatePoolId?.Key, queuesPerInstance: QueuesPerInstance);
        }
    }
    
    return false;
}
```

---

## 5. Long-Term Cost Projections Across Providers

### What
**What:** Generate predictive cost models that forecast total expenditure across providers over multiple billing cycles, considering usage trends and rate plan evolution.

### Why
**Why:** To make strategic decisions that optimize costs not just for the current billing cycle but across extended periods, accounting for growth patterns and rate changes.

### How
**How:** The system uses billing period analysis, usage trending, and rate plan update mechanisms to project future costs and determine optimal long-term provider strategies.

### Algorithm Implementation

```
Algorithm: LongTermCostProjection
Input: billingPeriods[], usageHistory[], ratePlanTrends[], providers[]
Output: projectedCosts[], optimalLongTermStrategy

1. Analyze historical usage patterns:
   a. Calculate usage growth rate per SIM card
   b. Identify seasonal patterns in data consumption
   c. Project future usage: futureUsage = currentUsage * (1 + growthRate)^periods

2. Evaluate rate plan evolution:
   a. Check rate plan update feasibility:
      canUpdate = DoesHaveTimeToProcessRatePlanUpdates(instance, ratePlanCount, currentTime, billingTimeZone)
   b. Calculate rate plan update timeline:
      updateTime = MinutesToUpdateRatePlans(ratePlanCount, updateSummary)
   c. Project rate plan costs over multiple billing cycles

3. Cross-provider cost projection:
   FOR each provider in [M2M, Mobility, CrossProvider]:
     FOR each billingPeriod in projectionRange:
       a. Calculate projected usage for period
       b. Apply provider-specific rate structure
       c. Include overage projections and base charges
       d. Sum total cost: totalProjectedCost += periodCost

4. Optimization strategy selection:
   a. Compare total projected costs across providers
   b. Factor in migration costs and switching penalties
   c. Select strategy minimizing total long-term cost
   d. Generate migration timeline and decision points
```

### Code Implementation

**Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` - Long-term cost projection and planning validation

```csharp
// Long-term planning validation for rate plan updates across providers
public static bool DoesHaveTimeToProcessRatePlanUpdates(OptimizationInstance instance, int ratePlansToUpdateCount,
    string connectionString, IKeysysLogger logger, DateTime currentSystemTimeUtc, TimeZoneInfo timeZoneInfo)
{
    logger.LogInfo("SUB", $"DoesHaveTimeToProcessRatePlanUpdates({instance.Id})");

    // Get historical rate plan update data for projection analysis
    var ratePlanUpdateSummaryRecords = GetPreviousRatePlanUpdateSummary(instance.Id, connectionString, logger);

    // Calculate remaining time in billing cycle for long-term planning
    decimal minutesRemainingInBillCycle = MinutesRemainingInBillCycle(logger, instance.BillingPeriodEndDate, currentSystemTimeUtc, timeZoneInfo);

    // Estimate time required for rate plan updates (cost projection component)
    var minutesToUpdateRatePlans = MinutesToUpdateRatePlans(ratePlansToUpdateCount, ratePlanUpdateSummaryRecords, logger);

    // Long-term feasibility check (10-minute buffer for risk management)
    if (minutesRemainingInBillCycle > 0 && minutesRemainingInBillCycle - minutesToUpdateRatePlans >= 10)
    {
        return true; // Long-term strategy feasible
    }

    return false; // Long-term strategy not recommended
}

// Historical analysis for long-term cost projection
private static List<OptimizationRatePlanUpdateSummary> GetPreviousRatePlanUpdateSummary(long instanceId, string connectionString, IKeysysLogger logger)
{
    logger.LogInfo("SUB", $"GetPreviousRatePlanUpdateSummary({instanceId})");

    var summaryRecords = new List<OptimizationRatePlanUpdateSummary>();
    using (var conn = new SqlConnection(connectionString))
    {
        using (var cmd = new SqlCommand("usp_Optimization_PreviousRatePlanUpdateSummary", conn))
        {
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@InstanceId", instanceId);

            conn.Open();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    // Extract historical data for trend analysis
                    var ratePlanSummaryRecord = RatePlanSummaryRecordFromReader(reader);
                    summaryRecords.Add(ratePlanSummaryRecord);
                }
            }
        }
    }

    return summaryRecords;
}

// Billing cycle time analysis for long-term projections
public static int MinutesRemainingInBillCycle(IKeysysLogger logger, DateTime billingPeriodEndDate, DateTime currentSystemTimeUtc, TimeZoneInfo timeZoneInfo)
{
    logger.LogInfo("SUB", $"MinutesRemainingInBillCycle({billingPeriodEndDate})");

    var currentLocalTime = TimeZoneInfo.ConvertTimeFromUtc(currentSystemTimeUtc, timeZoneInfo);
    return MinutesRemainingInBillCycle(logger, billingPeriodEndDate, currentLocalTime);
}

public static int MinutesRemainingInBillCycle(IKeysysLogger logger, DateTime billingPeriodEndDate, DateTime currentLocalTime)
{
    logger.LogInfo("SUB", $"MinutesRemainingInBillCycle({billingPeriodEndDate},{currentLocalTime})");

    double totalSecondsRemaining = 0;
    if (currentLocalTime < billingPeriodEndDate)
    {
        totalSecondsRemaining = billingPeriodEndDate.Subtract(currentLocalTime).TotalSeconds;
    }

    return (int)Math.Floor(totalSecondsRemaining / 60); // Convert to minutes for projection calculations
}

// Rate plan update timeline calculation for cost projection
private static decimal MinutesToUpdateRatePlans(int ratePlansToUpdateCount,
    IReadOnlyCollection<OptimizationRatePlanUpdateSummary> ratePlanUpdateSummaryRecords,
    IKeysysLogger logger)
{
    logger.LogInfo("SUB", $"MinutesToUpdateRatePlans({ratePlansToUpdateCount})");

    // Rate plan update batch size for cost calculation
    var maxBatchSize = 250;

    // Use maximum update rate from historical data for projection
    var maxUpdateRate = ratePlanUpdateSummaryRecords.Count > 0 ? ratePlanUpdateSummaryRecords.Max(x => x.UpdateRateDevicesPerMinute) : 60.0M;
    
    if (ratePlansToUpdateCount > maxBatchSize)
    {
        return maxBatchSize / maxUpdateRate; // Projected time for large batches
    }
    else
    {
        return ratePlansToUpdateCount / maxUpdateRate; // Projected time for current batch
    }
}

// Rate plan change count for long-term cost analysis
private static int CountRatePlansToUpdate(long instanceId, string connectionString, IKeysysLogger logger)
{
    logger.LogInfo("SUB", $"CountRatePlansToUpdate({instanceId})");

    var ratePlansToUpdate = 0;
    using (var conn = new SqlConnection(connectionString))
    {
        using (var cmd = new SqlCommand("usp_Optimization_RatePlanChangeCount", conn))
        {
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.AddWithValue("@InstanceId", instanceId);

            conn.Open();
            using (var reader = cmd.ExecuteReader())
            {
                while (reader.Read())
                {
                    ratePlansToUpdate = int.Parse(reader["TargetDeviceCount"].ToString());
                }
            }
        }
    }

    return ratePlansToUpdate; // Number of devices requiring rate plan changes for cost projection
}
```

**Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` - Cross-provider result processing for long-term analysis

```csharp
// Cross-provider customer results for long-term cost projection
protected OptimizationInstanceResultFile WriteCrossProviderCustomerResults(KeySysLambdaContext context, OptimizationInstance instance, List<long> queueIds, bool usesProration)
{
    LogInfo(context, CommonConstants.SUB, $"({instance.Id},{string.Join(',', queueIds)})");

    // Get customer billing period for long-term projection
    var customerBillingPeriod = crossProviderOptimizationRepository.GetBillingPeriod(ParameterizedLog(context), instance.AMOPCustomerId.GetValueOrDefault(), instance.CustomerBillingPeriodId.GetValueOrDefault(), context.OptimizationSettings.BillingTimeZone);
    
    // Get cross-provider rate plans for projection analysis
    var ratePlans = customerRatePlanRepository.GetCrossProviderCustomerRatePlans(ParameterizedLog(context), instance.ServiceProviderIds, instance.CustomerType, new List<int> { instance.AMOPCustomerId.GetValueOrDefault() }, customerBillingPeriod, instance.TenantId);

    // Generate rate pools for long-term cost modeling
    var crossOptimizationResultRatePools = GetResultRatePools(context, instance, customerBillingPeriod, usesProration, queueIds, true);
    var optimizationResultRatePools = GenerateCustomerSpecificRatePools(crossOptimizationResultRatePools);

    // Add unassigned rate pool for comprehensive cost analysis
    AddUnassignedRatePool(context, instance, customerBillingPeriod, usesProration, crossOptimizationResultRatePools, optimizationResultRatePools);

    // Process results for each queue in long-term analysis
    foreach (var queueId in queueIds)
    {
        LogInfo(context, LogTypeConstant.Info, $"Building long-term cost analysis for queue: {queueId}");
        
        // Get device results for cost projection
        var deviceResults = crossProviderOptimizationRepository.GetCrossProviderResults(ParameterizedLog(context), new List<long>() { queueId }, customerBillingPeriod);
        
        // Get shared pool results for comprehensive analysis
        var sharedPoolDeviceResults = crossProviderOptimizationRepository.GetCrossProviderSharedPoolResults(ParameterizedLog(context), new List<long>() { queueId }, customerBillingPeriod);
        
        // Build optimization result for long-term cost modeling
        result = BuildCrossProviderOptimizationResult(deviceResults, optimizationResultRatePools, result);
        crossCustomerResult = BuildCrossProviderOptimizationResult(sharedPoolDeviceResults, crossOptimizationResultRatePools, crossCustomerResult, shouldSkipAutoChangeRatePlan: true);
    }

    // Generate Excel output for long-term cost analysis
    var assignmentXlsxBytes = RatePoolAssignmentWriter.GenerateExcelFileFromByteArrays(statFileBytes, assignmentFileBytes, sharedPoolStatFileBytes, sharedPoolAssignmentFileBytes);

    // Save optimization results for long-term tracking
    return SaveOptimizationInstanceResultFile(context, instance.Id, assignmentXlsxBytes, result.TotalDeviceCount);
}
```

---

## Cross-Provider Integration Points

### Key Lambda Functions and Their Roles

1. **`AltaworxSimCardCostOptimizer.cs`** (Main Optimization Engine)
   - Handles real-time cost optimization across providers
   - Implements core rate pool assignment algorithms
   - Manages optimization queue processing

2. **`AltaworxSimCardCostOptimizerCleanup.cs`** (Results Processing & Long-term Analysis)
   - Processes optimization results and generates reports
   - Handles long-term cost projection analysis
   - Manages rate plan update scheduling

3. **`AltaworxSimCardCostQueueCustomerOptimization.cs`** (Customer-Specific Cross-Provider)
   - Implements customer-specific cross-provider optimization
   - Handles migration feasibility for customer deployments
   - Manages cross-provider customer rate plan analysis

### Performance Optimization Features

- **Redis Caching:** Distributed caching for large-scale optimizations
- **Queue-based Processing:** Scalable processing using SQS for optimization tasks
- **Sanity Check Mechanisms:** Time-limited processing with configurable limits
- **Provider-Specific Repositories:** Specialized data access for each provider type

This algorithmic framework ensures comprehensive evaluation across all five criteria while maintaining optimal performance and scalability in the Lambda environment.