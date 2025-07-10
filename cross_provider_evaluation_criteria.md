# Cross-Provider Evaluation Criteria: Algorithmic Analysis

## Overview
This document provides algorithmic breakdowns for cross-provider evaluation criteria in the Altaworx SIM Card Cost Optimization system. Each criterion includes What, Why, How explanations and specific code locations within the Lambda functions.

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

### Code Location
- **Lambda:** `AltaworxSimCardCostOptimizer.cs` lines 232-270
- **Method:** `ProcessQueues()` 
- **Key Components:**
  - `RatePoolCalculator.CalculateMaxAvgUsage()` at line 232
  - `RatePoolAssigner` initialization at lines 257-261
  - Cost optimization execution at lines 261-266

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

### Code Location
- **Lambda:** `AltaworxSimCardCostOptimizer.cs` lines 196-210
- **Method:** `ProcessQueues()` - Provider differentiation logic
- **Key Components:**
  - Optimization group retrieval at line 204
  - Performance filtering at lines 255-256
  - Provider-specific device retrieval in `GetSimCardsByPortalType()` at lines 286-300

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

### Code Location
- **Lambda:** `AltaworxSimCardCostOptimizer.cs` lines 173-180
- **Method:** `ProcessQueues()` - Rate plan sequence management
- **Key Components:**
  - Rate plan sequence generation at lines 173-174
  - Customer rate pool validation at lines 207-210
  - Migration queue processing in `ProcessQueuesContinue()` at lines 302-361

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

### Code Location
- **Lambda:** `AltaworxSimCardCostOptimizer.cs` lines 286-300
- **Method:** `GetSimCardsByPortalType()` - Provider-specific device retrieval
- **Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` lines 651-656
- **Method:** `WriteMobilityCarrierResults()` - Quality validation for Mobility optimization groups

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

### Code Location
- **Lambda:** `AltaworxSimCardCostOptimizerCleanup.cs` lines 445-495
- **Method:** `DoesHaveTimeToProcessRatePlanUpdates()` - Long-term planning validation
- **Key Components:**
  - Billing cycle analysis at lines 537-548: `MinutesRemainingInBillCycle()`
  - Rate plan update timeline at lines 559-578: `MinutesToUpdateRatePlans()`
  - Historical analysis at lines 479-509: `GetPreviousRatePlanUpdateSummary()`

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