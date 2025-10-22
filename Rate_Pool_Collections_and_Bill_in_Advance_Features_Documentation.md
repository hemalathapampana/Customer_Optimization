# Rate Pool Collections and Bill in Advance Features Documentation

## 1. Rate Pool Collections: Compatible Rate Plan Collection Creation

### Description
Creates collections of compatible rate plans for optimization by grouping rate plans with matching characteristics and generating permutation sequences for optimal device assignment. Essential for enabling automated rate plan optimization across multiple devices while ensuring compatibility constraints are respected and cost-effectiveness is maximized.

### Functionality
Examines customer rate plans to group compatible plans by rate plan code and pooling settings, creates rate pool collections from calculated plans, and generates permutation sequences for optimization algorithm processing.

### Algorithm

**ALGORITHM: CreateRatePoolCollections**  
**INPUT:** Customer Rate Plans, Billing Period, Charge Type, Optimization Settings  
**OUTPUT:** Rate Pool Collection with Generated Sequences

**Step 1: Group Rate Plans by Compatibility**
- Group rate plans by plan name (rate plan code) to ensure compatibility
- Further group by AllowsSimPooling setting to maintain pooling constraints
- Filter out rate plans with zero values for DataPerOverageCharge or OverageRate
- If zero value rate plans found, log exception and return failure

**Step 2: Validate Rate Plan Limits**
- Check if rate plan count exceeds maximum limit of 15 plans per group
- If limit exceeded, log exception and skip this group
- Check if rate plan count is below minimum limit of 2 plans
- If below minimum, log info and skip optimization for this group

**Step 3: Create Rate Pool Collection**
- Calculate maximum average usage for grouped rate plans using RatePoolCalculator
- Create rate pools from calculated plans using RatePoolFactory with billing period and charge type
- Generate rate pool collection using RatePoolCollectionFactory.CreateRatePoolCollection()

**Step 4: Generate Rate Pool Sequences**
- Call RatePoolAssigner.GenerateRatePoolSequences() with rate pool collection
- Create permutation sequences for optimization algorithm processing
- Each sequence represents a different assignment combination for devices

**Step 5: Create Optimization Queues**
- For each rate pool sequence, create a new optimization queue
- Add rate plans to queue with sequence order for processing
- Assign queue to communication plan group for batch processing

**Step 6: Return Collection Results**
- Return successfully created rate pool collection with generated sequences
- Collection is ready for optimization algorithm processing

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
- **Key Methods:**
  ```csharp
  // Rate pool collection creation
  var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
  var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
  var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);
  
  // Generate permutation sequences
  var ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools);
  ```

---

## 2. Bill in Advance Features: Advanced Billing Period Processing

### Description
Identifies rate plans eligible for Bill in Advance processing, loads next billing period for advance billing calculations, and sets appropriate charge types for advance billing scenarios. Essential for enabling customers to be billed for projected usage in advance of the actual billing period, providing better cash flow management and billing predictability.

### Functionality
Checks rate plans for Bill in Advance eligibility flag, retrieves next billing period information, configures charge type to OverageOnly for advance scenarios, and validates billing period availability before processing.

### Algorithm

**ALGORITHM: ProcessBillInAdvanceFeatures**  
**INPUT:** Customer Rate Plans, Current Billing Period, Service Provider ID  
**OUTPUT:** Bill in Advance Configuration and Next Billing Period

**Step 1: Identify Eligible Rate Plans**
- Examine all customer rate plans for IsBillInAdvanceEligible flag
- Count rate plans that have Bill in Advance eligibility enabled
- Set useBillInAdvance flag based on eligible rate plan count
- Currently override to false due to disabled logic (PORT-166)

**Step 2: Load Next Billing Period**
- If current billing period exists, call GetNextBillingPeriod()
- Pass service provider ID and current billing period end date
- Retrieve next billing period information for advance calculations
- Store next billing period ID for advance billing processing

**Step 3: Validate Billing Period Availability**
- Check if next billing period was successfully retrieved
- If useBillInAdvance is true but next billing period is null
- Log error message indicating billing in advance is not possible
- Return failure to prevent optimization processing

**Step 4: Configure Charge Type for Advance Billing**
- Set default charge type to RateChargeAndOverage for normal processing
- If useBillInAdvance is enabled, override charge type to OverageOnly
- OverageOnly ensures only overage charges are calculated for advance billing
- Log the selected charge type for processing transparency

**Step 5: Disable Logic Validation (Current State)**
- Check for PORT-166 implementation status
- Force disable Bill in Advance logic until new implementation ready
- Log current disabled status for operational awareness
- Continue with normal optimization processing

**Step 6: Return Advance Billing Configuration**
- Return configuration with next billing period ID and charge type
- Include useBillInAdvance flag status for downstream processing
- Configuration ready for optimization instance creation

### Code Location
- **File:** AltaworxSimCardCostQueueCustomerOptimization.cs
- **Key Methods:**
  ```csharp
  // Check eligibility
  var useBillInAdvance = ratePlans.Count(x => x.IsBillInAdvanceEligible) > 0;
  //Disable bill in advance logic until new logic is defined (PORT-166)
  useBillInAdvance = false;
  
  // Load next billing period
  BillingPeriod nextBillingPeriod = null;
  if (billingPeriod != null)
  {
      nextBillingPeriod = GetNextBillingPeriod(context, billingPeriod.ServiceProviderId, billingPeriod.BillingPeriodEnd);
  }
  
  // Set charge type
  var chargeType = OptimizationChargeType.RateChargeAndOverage;
  if (useBillInAdvance)
  {
      chargeType = OptimizationChargeType.OverageOnly;
  }
  ```

### Current Status
**DISABLED:** Bill in Advance features are currently disabled pending new logic implementation (PORT-166). The functionality exists but is temporarily deactivated until enhanced implementation is completed.