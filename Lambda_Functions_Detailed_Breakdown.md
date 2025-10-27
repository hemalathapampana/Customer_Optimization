# Lambda Functions Detailed Breakdown

## Lambda Function 1: QueueCustomerOptimization

### Purpose
Entry point for customer optimization that processes SQS messages containing customer optimization requests and sets up optimization environment.

### Main Handler Method
- **Method**: `FunctionHandler(SQSEvent sqsEvent, ILambdaContext context)`

---

### Step 1: Message Validation and Extraction

#### Action Methods:
- `ProcessEvent(KeySysLambdaContext context, SQSEvent sqsEvent)`
- `ProcessEventRecord(KeySysLambdaContext context, SQSEvent.SQSMessage message)`

#### SQS Message Attributes Extracted:
| Field | Type | Description |
|-------|------|-------------|
| CustomerType | SiteTypes | Rev, AMOP, or Cross-Provider |
| TenantId | int | Tenant identifier |
| OptimizationSessionId | long | Session tracking ID |
| CustomerId | Guid | Rev customer ID (for Rev customers) |
| AMOPCustomerId | int | AMOP customer ID (for AMOP customers) |
| BillPeriodId | int | Billing period ID |
| BillYear/BillMonth | int | Alternative billing period specification |
| ServiceProviderId | int | Service provider identifier |
| IntegrationAuthenticationId | int | Authentication ID for Rev customers |
| PortalType | PortalTypes | M2M, Mobility, or Cross-Provider |
| IsLastInstance | bool | Indicates if this is the final instance |

#### Database Tables Accessed:
- **BillingPeriod**: Query service provider from billing period
  - SQL: `SELECT ServiceProviderId FROM BillingPeriod bp WHERE bp.id = @billingPeriodId`

---

### Step 2: Customer Type Processing

#### Rev Customers Processing:
- **Method**: `ProcessCustomerId(KeySysLambdaContext context, int tenantId, Guid customerId, int? serviceProviderId, int? billingPeriodId, string messageId, int integrationAuthenticationId, long optimizationSessionId, bool usesProration, bool isLastInstance, SiteTypes customerType, string additionalData)`

#### AMOP Customers Processing:
- **Method**: `ProcessAMOPCustomerId(KeySysLambdaContext context, int tenantId, SiteTypes customerType, int AMOPCustomerId, int? serviceProviderId, int? billingPeriodId, string messageId, long optimizationSessionId, bool usesProration, bool isLastInstance, string additionalData)`

#### Cross-Provider Processing:
- **Method**: `RunCrossProviderCustomerOptimization(KeySysLambdaContext context, int tenantId, int customerId, SiteTypes customerType, string serviceProviderIds, int customerBillingPeriodId, string messageId, long optimizationSessionId, bool isLastInstance, string additionalData)`

---

### Step 3: Rate Plan Retrieval

#### Action Methods:
- `GetCustomerRatePlans(context, customerId, billingPeriodId, serviceProviderId, tenantId)`
- `GetCustomerRatePlans(context, Guid.Empty, billingPeriodId, serviceProviderId, tenantId, customerType, AMOPCustomerId)` (for AMOP)

#### Rate Plan Fields Retrieved:
| Field | Type | Description |
|-------|------|-------------|
| Id | int | Rate plan identifier |
| PlanName | string | Rate plan code |
| PlanDisplayName | string | Display name |
| AutoChangeRatePlan | bool | Allows auto-change optimization |
| AllowsSimPooling | bool | Supports SIM pooling |
| IsBillInAdvanceEligible | bool | Eligible for bill-in-advance |
| DataPerOverageCharge | decimal | Overage charge rate |
| OverageRate | decimal | Overage rate |

#### Database Tables Involved:
- **JasperCustomerRatePlan**: Customer-specific rate plans
- **JasperCarrierRatePlan**: Carrier rate plans
- **BillingPeriod**: Billing period information

---

### Step 4: Optimization Instance Creation

#### Action Methods:
- `StartOptimizationInstanceWithBillingPeriod(context, tenantId, messageId, billingPeriod.Id, customerId, integrationAuthenticationId, PortalTypes.M2M, optimizationSessionId, useBillInAdvance, billInAdvanceBillingPeriodId)`
- `StartOptimizationInstance(context, tenantId, serviceProviderId, customerId, messageId, integrationAuthenticationId, minStart, maxEnd, portalType, optimizationSessionId, billingPeriodId, useBillInAdvance, billInAdvanceBillingPeriodId)`

#### Database Tables Created/Updated:
- **OptimizationInstance**: Main optimization instance record
  
#### OptimizationInstance Fields:
| Field | Type | Description |
|-------|------|-------------|
| Id | long | Instance identifier |
| TenantId | int | Tenant ID |
| MessageId | string | SQS message ID |
| BillingPeriodId | int | Billing period reference |
| RevCustomerId | Guid? | Rev customer ID |
| AMOPCustomerId | int? | AMOP customer ID |
| IntegrationAuthenticationId | int? | Auth ID |
| PortalType | PortalTypes | Portal type |
| SessionId | long | Session ID |
| UseBillInAdvance | bool | Bill-in-advance flag |
| BillInAdvanceBillingPeriodId | int? | Future billing period |
| RunStatusId | OptimizationStatus | Current status |
| IsCustomerOptimization | bool | Customer vs carrier optimization |

---

### Step 5: Device Processing by Rate Plans

#### Auto-Change Disabled Rate Plans:
- **Method**: `ProcessDevicesWithAutoChangeDisabledRatePlans(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, billingPeriod, nextBillingPeriod, instanceId, optimizationSimCards, ratePlansByCustomerRatePool, tenantId)`

#### Auto-Change Enabled Rate Plans:
- **Method**: `ProcessPlanNameGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, billingPeriod, instanceId, chargeType, planNameGroup, optimizationSimCards)`

#### Device Retrieval:
- **Method**: `GetOptimizationSimCards(context, null, serviceProviderId, revAccountNumber, integrationAuthenticationId, billingPeriodId, tenantId, customerType, AMOPCustomerId)`

#### Database Views/Tables for Device Data:
- **vwOptimizationSimCard**: Optimization device view
  
#### Device Fields:
| Field | Type | Description |
|-------|------|-------------|
| Id | int | Device ID |
| ICCID | string | SIM card identifier |
| MSISDN | string | Phone number |
| CommunicationPlan | string | Communication plan |
| CustomerRatePlanCode | string | Current rate plan |
| CustomerRatePoolId | int? | Rate pool assignment |
| CycleDataUsageMB | decimal | Data usage in MB |
| SmsUsage | long | SMS usage count |
| ProviderDateActivated | DateTime? | Activation date |

---

### Step 6: Communication Plan Group Creation

#### Action Methods:
- `CreateCommPlanGroup(context, instanceId)`
- `AddCustomerRatePlansToCommPlanGroup(context, instanceId, commPlanGroupId, calculatedPlans)`

#### Database Tables:
- **CommPlanGroup**: Communication plan groupings
  
#### CommPlanGroup Fields:
| Field | Type | Description |
|-------|------|-------------|
| Id | long | Group identifier |
| InstanceId | long | Optimization instance |
| CreatedBy | string | System user |
| CreatedDate | DateTime | Creation timestamp |

- **CommGroup_RatePlan**: Rate plan assignments to groups

---

### Step 7: Queue Generation and Rate Pool Calculations

#### Action Methods:
- `RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null)`
- `RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType)`
- `RatePoolCollectionFactory.CreateRatePoolCollection(ratePools)`

#### Permutation Generation:
- **Method**: `GeneratePermutationQueueRatePlans(context, usesProration, billingPeriod, instanceId, commPlanGroupId, ratePoolCollection, commGroupRatePlanTable)`
- **Method**: `RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools)`

#### Queue Creation:
- **Method**: `CreateQueue(context, instanceId, commPlanGroupId, serviceProviderId, usesProration)`
- **Method**: `CreateQueueRatePlans(context, dtQueueRatePlan)`

#### Database Tables:
- **OptimizationQueue**: Individual optimization queues
  
#### OptimizationQueue Fields:
| Field | Type | Description |
|-------|------|-------------|
| Id | long | Queue identifier |
| InstanceId | long | Optimization instance |
| CommPlanGroupId | long | Communication group |
| ServiceProviderId | int? | Service provider |
| UsesProration | bool | Proration flag |
| RunStatusId | OptimizationStatus | Queue status |

- **QueueRatePool**: Rate plan assignments to queues

#### QueueRatePool Fields:
| Field | Type | Description |
|-------|------|-------------|
| QueueId | long | Queue reference |
| CommGroup_RatePlanId | long | Rate plan reference |
| SequenceOrder | int | Order in sequence |
| CreatedBy | string | System user |
| CreatedDate | DateTime | Creation timestamp |

---

### Step 8: Device Assignment and Base Calculations

#### Action Methods:
- `BaseDeviceAssignment(context, instanceId, commPlanGroupId, serviceProviderId, revAccountNumber, integrationAuthenticationId, null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId)`
- `ProjectDataUsageAndSaveDevices(context, instanceId, optimizationSimCards, billingPeriod, usesProration)`

#### Database Tables:
- **OptimizationDevice**: Device optimization records
  
#### OptimizationDevice Fields:
| Field | Type | Description |
|-------|------|-------------|
| Id | long | Record identifier |
| InstanceId | long | Optimization instance |
| AmopDeviceId | int | Device reference |
| CommPlanGroupId | long | Communication group |
| ICCID | string | SIM identifier |
| UsageMB | decimal | Data usage |
| ChargeAmt | decimal | Calculated charge |
| RateChargeAmt | decimal | Rate charge component |
| OverageChargeAmt | decimal | Overage charge component |
| BaseRateAmt | decimal | Base rate component |

---

### Step 9: Queue Optimization Enqueueing

#### Action Methods:
- `EnqueueOptimizationRunsAsync(context, instanceId, commPlanGroupIds, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true)`

#### SQS Message Attributes for Optimizer:
| Attribute | Type | Description |
|-----------|------|-------------|
| QueueIds | string | Comma-separated queue IDs |
| SkipLowerCostCheck | bool | Skip cost validation |
| ChargeType | OptimizationChargeType | Rate+Overage or OverageOnly |

---

### Step 10: Cleanup Enqueueing

#### Action Methods:
- `EnqueueCleanup(context, instanceId, deliveryDelay: 15, serviceProviderId, isCustomerOptimization: true, isLastInstance)`

#### Database Tables Updated:
- **OptimizationInstanceTrackingRecord**: Session tracking
  
#### Tracking Fields:
| Field | Type | Description |
|-------|------|-------------|
| OptimizationSessionId | long | Session identifier |
| RevCustomerId | Guid? | Rev customer |
| AMOPCustomerId | int? | AMOP customer |
| IsProcessed | bool | Processing status |

---

### Error Handling Methods:
- `UpdateCustomerOptimization(context, optimizationSessionId, errorMessage, serviceProviderId, revAccountNumber)`
- `StopOptimizationInstance(context, instanceId, OptimizationStatus.CompleteWithErrors)`
- `OptimizationAmopApiTrigger.SendResponseToAMOP20(context, "ErrorMessage", optimizationSessionId, null, 0, errorMessage, 0, revAccountNumber, additionalData)`

---

## Lambda Function 2: SimCost Optimizer

### Purpose
Performs core optimization calculations using advanced algorithms to determine cost-effective rate plan assignments.

### Main Handler Method
- **Method**: `Handler(SQSEvent sqsEvent, ILambdaContext context)`

---

### Step 1: Queue Processing Setup

#### Action Methods:
- `ProcessEvent(KeySysLambdaContext context, SQSEvent sqsEvent)`
- `ProcessEventRecord(KeySysLambdaContext context, SQSEvent.SQSMessage message)`

#### SQS Message Attributes:
| Attribute | Type | Description |
|-----------|------|-------------|
| QueueIds | string | Comma-separated queue IDs |
| SkipLowerCostCheck | bool | Skip lower cost validation |
| ChargeType | OptimizationChargeType | Optimization charge type |
| IsChainingProcess | bool | Lambda chaining flag |

---

### Step 2: Instance and Device Retrieval

#### Queue Validation:
- **Method**: `GetQueue(context, queueId)`
- **Status Check**: Validates queue not in finished status

#### Queue Status Constants:
```csharp
QUEUE_FINISHED_STATUSES = {
    OptimizationStatus.CleaningUp,
    OptimizationStatus.CompleteWithSuccess,
    OptimizationStatus.CompleteWithErrors
}
```

#### Instance Retrieval:
- **Method**: `GetInstance(context, queue.InstanceId)`

#### Database Tables:
- **OptimizationQueue**: Queue information
  
#### Queue Fields Retrieved:
| Field | Type | Description |
|-------|------|-------------|
| Id | long | Queue identifier |
| InstanceId | long | Parent instance |
| CommPlanGroupId | long | Communication group |
| ServiceProviderId | int? | Service provider |
| UsesProration | bool | Proration enabled |
| RunStatusId | OptimizationStatus | Current status |

- **OptimizationInstance**: Instance details

#### Instance Fields Retrieved:
| Field | Type | Description |
|-------|------|-------------|
| Id | long | Instance identifier |
| PortalType | PortalTypes | M2M, Mobility, CrossProvider |
| RevCustomerId | Guid? | Rev customer ID |
| AMOPCustomerId | int? | AMOP customer ID |
| IsCustomerOptimization | bool | Customer vs carrier |
| BillingPeriodEndDate | DateTime | Billing period end |
| ServiceProviderId | int? | Service provider |

---

### Step 3: Device Data Loading by Portal Type

#### M2M Portal:
- **Method**: `GetSimCards(context, instance.Id, serviceProviderId, commPlans, billingPeriod, commPlanGroupId, instance.IsCustomerOptimization)`
- **Communication Plans**: `GetCommPlansForCommGroup(context, queue.CommPlanGroupId)`

#### Mobility Portal:
- **Method**: `optimizationMobilityDeviceRepository.GetOptimizationMobilityDevices(context, instance.Id, serviceProviderId, optimizationGroupIds, billingPeriod, commPlanGroupId, instance.IsCustomerOptimization)`
- **Optimization Groups**: `carrierRatePlanRepository.GetOptimizationGroupsByCommGroupId(ParameterizedLog(context), queue.CommPlanGroupId)`

#### Cross-Provider Portal:
- **Method**: `crossProviderOptimizationRepository.GetCrossProviderOptimizationDevices(ParameterizedLog(context), instance.Id, billingPeriod, commPlanGroupId, instance.IsCustomerOptimization)`

#### Database Views for Device Loading:
- **vwOptimizationSimCard** (M2M)
- **vwOptimizationMobilityDevice** (Mobility)
- **vwCrossProviderOptimizationDevice** (Cross-Provider)

#### Device Fields Loaded:
| Field | Type | Description |
|-------|------|-------------|
| Id | int | Device identifier |
| ICCID | string | SIM card ID |
| MSISDN | string | Phone number |
| CommunicationPlan | string | Communication plan |
| CycleDataUsageMB | decimal | Billing cycle usage |
| SmsUsage | long | SMS message count |
| ChargeAmt | decimal | Current charge |
| ProviderDateActivated | DateTime? | Activation date |
| StartingRatePlanId | int | Current rate plan |

---

### Step 4: Rate Pool Creation and Configuration

#### Rate Plan Retrieval:
- **Method**: `carrierRatePlanRepository.GetQueueRatePlans(ParameterizedLog(context), new List<long> { queueId })`

#### Rate Pool Calculation:
- **Method**: `RatePoolCalculator.CalculateMaxAvgUsage(queueRatePlans, avgUsage)`
- **Average Usage**: `simCards.Sum(x => x.CycleDataUsageMB) / simCards.Count`

#### Rate Pool Factory:
- **Method**: `RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, queue.UsesProration, chargeType)`

#### Pool Collection Creation:
- **Method**: `RatePoolCollectionFactory.CreateRatePoolCollection(ratePools, shouldPoolByOptimizationGroup, customerRatePoolId)`

#### Configuration Parameters:
| Parameter | Description |
|-----------|-------------|
| shouldPoolByOptimizationGroup | `(instance.PortalType == PortalTypes.Mobility \|\| instance.IsCustomerOptimization) && ratePools.Any(x => x.RatePlan.AllowsSimPooling)` |
| customerRatePoolId | `GetCustomerRatePoolsByCommGroupId(context, queue.CommPlanGroupId)` |

---

### Step 5: Optimization Algorithm Execution

#### RatePoolAssigner Configuration:
- **Constructor**: `new RatePoolAssigner(string.Empty, ratePoolCollection, simCards, context.logger, SanityCheckTimeLimit, context.LambdaContext, IsUsingRedisCache, instance.PortalType, shouldFilterByRatePlanType, shouldPoolUsageBetweenRatePlans)`

#### Algorithm Parameters:
| Parameter | Value |
|-----------|-------|
| shouldFilterByRatePlanType | `instance.PortalType == PortalTypes.Mobility && !instance.IsCustomerOptimization` |
| shouldPoolUsageBetweenRatePlans | `(instance.PortalType == PortalTypes.Mobility \|\| instance.IsCustomerOptimization) && ratePoolCollection.IsPooled` |

#### Grouping Strategies:
- **Method**: `GetSimCardGroupingByPortalType(instance.PortalType, instance.IsCustomerOptimization)`

#### Grouping Options:
| Portal Type | Customer Optimization | Grouping Strategies |
|-------------|----------------------|-------------------|
| Mobility | Any | NoGrouping |
| M2M | True | NoGrouping |
| M2M | False | NoGrouping, GroupByCommunicationPlan |

#### Assignment Execution:
- **Method**: `assigner.AssignSimCards(groupingStrategies, context.OptimizationSettings.BillingTimeZone, false, false, ratePoolSequences)`

---

### Step 6: Time Management and Lambda Chaining

#### Time Monitoring:
- **Remaining Time**: `context.LambdaContext.RemainingTime.TotalSeconds`
- **Sanity Check Limit**: Default 180 seconds

#### Redis Caching:
- **Cache Storage**: `RedisCacheHelper.RecordPartialAssignerToCache(context, assigner)`
- **Cache Retrieval**: `RedisCacheHelper.GetPartialAssignerFromCache(context, queueIds, context.OptimizationSettings.BillingTimeZone)`

#### Chaining Process:
- **Method**: `ProcessQueuesContinue(context, queueIds, messageId, skipLowerCostCheck, chargeType)`
- **Continue Method**: `assigner.AssignSimCardsContinue(context.OptimizationSettings.BillingTimeZone, false)`

#### Chaining SQS Message:
- **Method**: `EnqueueOptimizationContinueProcessAsync(context, remainingQueueIds, chargeType, skipLowerCostCheck)`

---

### Step 7: Result Recording

#### Best Result Selection:
- **Property**: `assigner.Best_Result`

#### Result Recording Methods:
- `RecordResults(context, result.QueueId, amopCustomerId.Value, commPlanGroupId, result, skipLowerCostCheck)` (AMOP)
- `RecordResults(context, result.QueueId, accountNumber, commPlanGroupId, result, skipLowerCostCheck)` (Rev)

#### Database Tables Updated:
- **OptimizationDeviceResult** (M2M)
- **OptimizationMobilityDeviceResult** (Mobility)
- **OptimizationSharedPoolResult** (Cross-pooling)

#### Result Fields:
| Field | Type | Description |
|-------|------|-------------|
| QueueId | long | Queue reference |
| AmopDeviceId | int | Device reference |
| AssignedCarrierRatePlanId | int? | Assigned carrier plan |
| AssignedCustomerRatePlanId | int? | Assigned customer plan |
| CustomerRatePoolId | int? | Rate pool assignment |
| UsageMB | decimal | Device usage |
| ChargeAmt | decimal | Total calculated charge |
| BaseRateAmt | decimal | Base rate portion |
| RateChargeAmt | decimal | Rate charge portion |
| OverageChargeAmt | decimal | Overage portion |
| SmsUsage | long | SMS usage |
| SmsChargeAmount | decimal | SMS charges |

---

### Step 8: Queue Status Management

#### Queue Start:
- **Method**: `StartQueue(context, queueId, messageId)`

#### Queue Stop:
- **Method**: `StopQueue(context, queueId, isSuccess)`

#### Status Updates in Database:
- **OptimizationQueue.RunStatusId**: Updated to reflect processing state

---

### Customer Rate Pool Handling

#### Customer Rate Pool Retrieval:
- **Method**: `GetCustomerRatePoolsByCommGroupId(KeySysLambdaContext context, long commGroupId)`
- **Stored Procedure**: `GET_CUSTOMER_RATE_POOLS_BY_COMM_GROUP_ID`

#### Parameters:
| Parameter | Type | Description |
|-----------|------|-------------|
| @COMM_GROUP_ID | long | Communication group ID |
| @CustomerRatePoolId | int (Output) | Retrieved rate pool ID |

---

## Lambda Function 3: SimCost Optimizer Cleanup

### Purpose
Finalizes optimization process by cleaning up temporary data, generating reports, and sending notifications.

### Main Handler Method
- **Method**: `Handler(SQSEvent sqsEvent, ILambdaContext context)`

---

### Step 1: Queue Completion Monitoring

#### Action Methods:
- `ProcessEventRecord(KeySysLambdaContext context, SQSEvent.SQSMessage message)`
- `GetOptimizationQueueLength(context)`

#### SQS Message Attributes:
| Attribute | Type | Description |
|-----------|------|-------------|
| InstanceId | long | Optimization instance ID |
| RetryCount | int | Current retry attempt |
| IsCustomerOptimization | bool | Customer vs carrier |
| IsLastInstance | bool | Final instance flag |
| ServiceProviderId | int | Service provider |
| IsOptLastStepSendEmail | bool | Email step flag |
| SessionId | long | Session identifier |

#### Queue Length Monitoring:
- **Method**: `GetOptimizationQueueLength(context)`
- **AWS SQS Attributes**: 
  - `ApproximateNumberOfMessages`
  - `ApproximateNumberOfMessagesDelayed` 
  - `ApproximateNumberOfMessagesNotVisible`

#### Retry Logic:
- **Max Retries**: 10 attempts
- **Method**: `RequeueCleanup(context, instanceId, retryCount, optimizationQueueLength, isCustomerOptimization)`

---

### Step 2: Instance Validation and Cleanup Triggering

#### Instance Status Check:
- **Method**: `GetInstance(context, instanceId)`

#### Instance Finished Statuses:
```csharp
INSTANCE_FINISHED_STATUSES = {
    OptimizationStatus.CleaningUp,
    OptimizationStatus.CompleteWithSuccess,
    OptimizationStatus.CompleteWithErrors
}
```

#### Main Cleanup Method:
- **Method**: `CleanupInstance(KeySysLambdaContext context, long instanceId, bool isCustomerOptimization, bool isLastInstance, int serviceProviderId)`

---

### Step 3: Communication Group Processing

#### Communication Groups Retrieval:
- **Method**: `GetCommGroups(context, instanceId)`

#### Database Table:
- **CommPlanGroup**: Communication group records

#### CommGroup Fields:
| Field | Type | Description |
|-------|------|-------------|
| Id | long | Group identifier |
| InstanceId | long | Parent instance |
| Name | string | Group name |
| CreatedDate | DateTime | Creation time |

#### Per-Group Processing:
1. **Winning Queue Selection**: `GetWinningQueueId(context, commGroup.Id)`
2. **Queue Termination**: `EndQueuesForCommGroup(context, commGroup.Id)`
3. **Result Cleanup**: `CleanupDeviceResultsForCommGroup(context, commGroup.Id, winningQueueId)`

---

### Step 4: Result Aggregation and Winner Selection

#### Winning Queue Logic:
- **Criteria**: Lowest total cost per communication group
- **Method**: `GetWinningQueueId(context, commGroupId)`

#### Database Query for Winner Selection:
```sql
SELECT TOP 1 QueueId 
FROM QueueTotalCost 
WHERE CommPlanGroupId = @commGroupId 
ORDER BY TotalCost ASC
```

#### Non-Winning Result Cleanup:
- **Method**: `CleanupDeviceResultsForCommGroup(context, commGroupId, winningQueueId)`
- **Tables Cleaned**: 
  - OptimizationDeviceResult
  - OptimizationMobilityDeviceResult
  - OptimizationSharedPoolResult

---

### Step 5: Report Generation by Portal Type

#### M2M Results:
- **Method**: `WriteM2MResults(KeySysLambdaContext context, OptimizationInstance instance, List<long> queueIds, BillingPeriod billingPeriod, bool usesProration, bool isCustomerOptimization)`

#### Mobility Results:
- **Method**: `WriteMobilityResults(KeySysLambdaContext context, OptimizationInstance instance, List<long> queueIds, BillingPeriod billingPeriod, bool usesProration, bool isCustomerOptimization)`
- **Carrier Results**: `WriteMobilityCarrierResults(context, instance, queueIds, billingPeriod, usesProration)`

#### Cross-Provider Results:
- **Method**: `WriteCrossProviderCustomerResults(context, instance, queueIds, usesProration)`

#### Result Data Retrieval Methods:
- **M2M**: `GetM2MResults(context, queueIds, billingPeriod)`
- **Mobility**: `GetMobilityResults(context, queueIds, billingPeriod)`
- **Cross-Pool**: `GetM2MSharedPoolResults(context, queueIds, billingPeriod)`

---

### Step 6: Excel Report Generation

#### Statistics Generation:
- **Method**: `RatePoolStatisticsWriter.WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, result)`

#### Assignment File Generation:
- **Method**: `RatePoolAssignmentWriter.WriteRatePoolAssignments(result)`

#### Excel File Creation:
- **Method**: `RatePoolAssignmentWriter.GenerateExcelFileFromByteArrays(statFileBytes, assignmentFileBytes, sharedPoolStatFileBytes, sharedPoolAssignmentFileBytes)`

#### File Storage:
- **Method**: `SaveOptimizationInstanceResultFile(context, instance.Id, assignmentXlsxBytes)`

#### Database Table:
- **OptimizationInstanceResultFile**: Stores Excel results

#### ResultFile Fields:
| Field | Type | Description |
|-------|------|-------------|
| Id | long | File identifier |
| InstanceId | long | Instance reference |
| AssignmentXlsxBytes | byte[] | Excel file data |
| TotalDeviceCount | int | Device count |
| CreatedBy | string | System user |
| CreatedDate | DateTime | Creation time |
| IsDeleted | bool | Deletion flag |

---

### Step 7: Email Notification System

#### Carrier Optimization Email:
- **Method**: `SendResults(context, instance, assignmentXlsxBytes, billingTimeZone, syncResults, integrationType, integrationTypes)`

#### Customer Optimization Email:
- **Method**: `OptimizationCustomerSendResults(context, instance, syncResults, isLastInstance, serviceProviderId)`

#### Email Configuration Fields:
| Setting | Description |
|---------|-------------|
| FromEmailAddress | Sender email |
| ToEmailAddresses | Recipient list (semicolon-separated) |
| BccEmailAddresses | BCC list |
| ResultsEmailSubject | Email subject template |
| ResultsCustomerEmailSubject | Customer email subject |

#### Device Count Retrieval:
- **Rev Customers**: 
  - `GetTotalSimCountForCustomer(context, revAccountNumber, tenantId)` (M2M)
  - `GetTotalMobilitySimCountForCustomer(context, revAccountNumber, tenantId)` (Mobility)
- **AMOP Customers**:
  - `GetTotalSimCountForAMOPCustomerId(context, amopCustomerId, tenantId)` (M2M)
  - `GetTotalMobilitySimCountForAMOPCustomerId(context, amopCustomerId, tenantId)` (Mobility)

#### Customer Information Retrieval:
- **Rev**: `GetRevCustomerById(context, customerId)`
- **AMOP**: `GetAMOPCustomerById(context, amopCustomerId)`

#### Database Tables:
- **RevCustomer**: Rev customer details
- **Site**: AMOP customer details

---

### Step 8: Customer Optimization Processing Tracking

#### Processing Table Updates:
- **Method**: `UpdateOptCustomerProcessing(context, customerId, DateTime.UtcNow, deviceCount, serviceProviderId, siteType, instance)`

#### Database Table:
- **OptimizationCustomerProcessing**: Tracks customer processing

#### Processing Fields:
| Field | Type | Description |
|-------|------|-------------|
| ServiceProviderId | int | Service provider |
| CustomerId | string | Rev customer ID |
| AMOPCustomerId | int | AMOP customer ID |
| CustomerName | string | Customer name |
| DeviceCount | int | Total device count |
| IsProcessed | bool | Processing complete flag |
| StartTime | DateTime | Process start |
| EndTime | DateTime | Process end |
| SessionId | long | Session reference |
| InstanceId | long | Instance reference |

#### Email Step Processing:
- **Method**: `OptCustomerSendEmail(context, instanceId, sessionId, serviceProviderId, retryCount)`
- **Check Method**: `CheckOptCustomerProcessing(context, serviceProviderId, sessionId)`

#### Final Cleanup:
- **Method**: `DeleteDataFromOptCustomerProcessing(context, serviceProviderId, sessionId)`

---

### Step 9: Rate Plan Update Management

#### Rate Plan Update Decision:
- **Method**: `DoesHaveTimeToProcessRatePlanUpdates(instance, ratePlansToUpdateCount, connectionString, logger, DateTime.UtcNow, billingTimeZone)`

#### Update Count Calculation:
- **Method**: `CountRatePlansToUpdate(instanceId, connectionString, logger)`
- **Stored Procedure**: `usp_Optimization_RatePlanChangeCount`

#### Time Calculations:
- **Method**: `MinutesRemainingInBillCycle(logger, instance.BillingPeriodEndDate, currentSystemTimeUtc, timeZoneInfo)`
- **Method**: `MinutesToUpdateRatePlans(ratePlansToUpdateCount, ratePlanUpdateSummaryRecords, logger)`

#### Rate Plan Update History:
- **Method**: `GetPreviousRatePlanUpdateSummary(instanceId, connectionString, logger)`
- **Stored Procedure**: `usp_Optimization_PreviousRatePlanUpdateSummary`

#### Update Summary Fields:
| Field | Type | Description |
|-------|------|-------------|
| Id | long | Summary identifier |
| InstanceId | long | Instance reference |
| QueueCount | int | Number of queues |
| MinSecondsToUpdate | int | Minimum update time |
| MaxSecondsToUpdate | int | Maximum update time |
| AvgSecondsToUpdate | decimal | Average update time |
| UpdateRateDevicesPerMinute | decimal | Update rate |

#### Update Decisions:
- **Auto-Update**: `QueueRatePlanUpdates(context, instance.Id, instance.TenantId)`
- **Go Email**: `SendGoForRatePlanUpdatesEmail(context, instance, billingTimeZone)`
- **No-Go Email**: `SendNoGoForRatePlanUpdatesEmail(context, instance, billingTimeZone)`

---

### Step 10: Integration and Sync Information

#### Sync Results Retrieval:
- **Method**: `GetSummaryValues(context, integrationType, serviceProviderId)`

#### Sync Summary Fields:
| Field | Type | Description |
|-------|------|-------------|
| DetailLastSyncDate | DateTime? | Last detail sync |
| UsageLastSyncDate | DateTime? | Last usage sync |
| DeviceCount | int | Total device count |

#### Integration Types:
- **Database**: IntegrationTypeModel table
- **Types**: Jasper, POD19, TMobileJasper, Rogers

#### AMOP 2.0 Integration:
- **API Model**: `OptimizationCustomerEndProcess`
- **Proxy Call**: `client.OptCustomerSendEmailProxy(_proxyUrl, payload, context.logger)`

#### Integration Fields:
| Field | Type | Description |
|-------|------|-------------|
| InstanceId | long | Instance ID |
| SessionId | long | Session ID |
| ServiceProviderId | int | Service provider |
| SiteType | int | Customer type |
| DetailLastSyncDate | DateTime? | Detail sync date |
| UsageLastSyncDate | DateTime? | Usage sync date |
| BillingPeriodEndDate | DateTime | Billing end date |
| TenantId | int | Tenant ID |

---

### Step 11: Instance Finalization

#### Instance Status Update:
- **Method**: `StopOptimizationInstance(context, instanceId, OptimizationStatus.CompleteWithSuccess)`

#### Final Database Updates:
- **OptimizationInstance.RunStatusId**: Set to CompleteWithSuccess
- **OptimizationInstance.RunEndTime**: Set to completion time

#### Error Handling:
- **Status**: `OptimizationStatus.CompleteWithErrors`
- **Notifications**: Error emails and AMOP 2.0 API calls

---

## Database Tables Summary

### Core Tables:
1. **OptimizationInstance** - Main optimization instances
2. **OptimizationQueue** - Individual optimization queues  
3. **CommPlanGroup** - Communication plan groupings
4. **BillingPeriod** - Billing period information
5. **CustomerRatePool** - Customer rate pool definitions

### Device Tables:
6. **Device** - M2M device master data
7. **MobilityDevice** - Mobility device master data  
8. **OptimizationDevice** - Optimization device processing
9. **vwOptimizationSimCard** - Optimization device view

### Rate Plan Tables:
10. **JasperCustomerRatePlan** - Customer rate plans
11. **JasperCarrierRatePlan** - Carrier rate plans
12. **CommGroup_RatePlan** - Rate plan group assignments
13. **QueueRatePool** - Queue rate plan assignments

### Result Tables:
14. **OptimizationDeviceResult** - M2M optimization results
15. **OptimizationMobilityDeviceResult** - Mobility results
16. **OptimizationSharedPoolResult** - Cross-customer pooling results
17. **OptimizationInstanceResultFile** - Excel result files

### Tracking Tables:
18. **OptimizationInstanceTrackingRecord** - Session tracking
19. **OptimizationCustomerProcessing** - Customer process tracking
20. **QueueTotalCost** - Queue cost calculations

### Reference Tables:
21. **ServiceProvider** - Service provider master
22. **Site** - AMOP customer sites
23. **RevCustomer** - Rev customer information
24. **IntegrationTypeModel** - Integration type definitions

### Rate Plan Update Tables:
25. **OptimizationRatePlanUpdateSummary** - Update performance history

This comprehensive breakdown provides clear visibility into each Lambda function's responsibilities, the specific methods involved, and the database schema supporting the optimization process.