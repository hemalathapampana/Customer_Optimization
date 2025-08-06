using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using Altaworx.SimCard.Cost.Optimizer.Core;
using Altaworx.SimCard.Cost.Optimizer.Core.Enumerations;
using Altaworx.SimCard.Cost.Optimizer.Core.Helpers;
using Altaworx.SimCard.Cost.Optimizer.Core.Models;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amop.Core.Logger;
using Amop.Core.Models;
// to use LogTypeConstant
using Altaworx.AWS.Core.Helpers.Constants;
using System.Threading.Tasks;
using Altaworx.SimCard.Cost.Optimizer.Core.Factories;
using Amop.Core.Constants;
using System.Net.Http;
using System.Text;
using Amop.Core.Helpers.Pond;
using Amop.Core.Repositories.Environment;
using Amazon.S3.Model;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.SystemTextJson.DefaultLambdaJsonSerializer))]

namespace Altaworx.SimCard.Cost.MobilityCustomerOptimization
{
    public class Function : AwsFunctionBase
    {
        private readonly int DEFAULT_QUEUES_PER_INSTANCE = 5;
        private readonly int QueuesPerInstance;
        private readonly string ErrorNotificationEmailReceiver;
        private bool IsUsingRedisCache = false;
        // current lambda is for Mobility customer optimization so initialize with PortalTypes.Mobility
        public Function() : base(PortalTypes.Mobility)
        {
            if (!int.TryParse(Environment.GetEnvironmentVariable(EnvironmentVariableKeyConstants.QUEUES_PER_INSTANCE), out QueuesPerInstance))
            {
                QueuesPerInstance = DEFAULT_QUEUES_PER_INSTANCE;
            }
            ErrorNotificationEmailReceiver = Environment.GetEnvironmentVariable(EnvironmentVariableKeyConstants.ERROR_NOTIFICATION_EMAIL_RECEIVER);
        }

        /// <summary>
        /// Mobility Customer Optimization
        /// </summary>
        /// <param name="sqsEvent"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task FunctionHandle(SQSEvent sqsEvent, ILambdaContext context)
        {
            KeySysLambdaContext keysysContext = null;
            try
            {
                keysysContext = BaseFunctionHandler(context);
                //if a Redis cache connection string is set but not reachable => considered as an error & continue without cache
                IsUsingRedisCache = keysysContext.TestRedisConnection();
                await ProcessEvent(keysysContext, sqsEvent);
            }
            catch (Exception ex)
            {
                LogInfo(keysysContext, "EXCEPTION", ex.Message);
            }

            CleanUp(keysysContext);
        }

        private async Task ProcessEvent(KeySysLambdaContext context, SQSEvent sqsEvent)
        {
            LogInfo(context, "SUB", "ProcessEvent");
            if (sqsEvent.Records.Count > 0)
            {
                if (sqsEvent.Records.Count == 1)
                {
                    await ProcessEventRecord(context, sqsEvent.Records[0]);
                }
                else
                {
                    LogInfo(context, "EXCEPTION", $"Expected a single message, received {sqsEvent.Records.Count}");
                }
            }
        }

        private async Task ProcessEventRecord(KeySysLambdaContext context, SQSEvent.SQSMessage message)
        {
            LogInfo(context, "SUB", "ProcessEventRecord");
            if (!message.MessageAttributes.ContainsKey("CustomerType"))
            {
                LogInfo(context, "EXCEPTION", "No Customer Type provided in message");
                return;
            }

            if (!message.MessageAttributes.ContainsKey("CustomerId") && !message.MessageAttributes.ContainsKey("AMOPCustomerId"))
            {
                LogInfo(context, "EXCEPTION", "No Customer Id provided in message");
                return;
            }

            if (!message.MessageAttributes.ContainsKey("BillPeriodId") && !(message.MessageAttributes.ContainsKey("BillYear") && message.MessageAttributes.ContainsKey("BillMonth")))
            {
                LogInfo(context, "EXCEPTION", "No Billing Period provided in message");
                return;
            }

            var serviceProviderId = GetServiceProviderId(message);
            if (!serviceProviderId.HasValue)
            {
                LogInfo(context, "EXCEPTION", "No Service Provider provided in message");
                return;
            }

            int? billingPeriodId = null;
            if (message.MessageAttributes.ContainsKey("BillPeriodId"))
            {
                if (!int.TryParse(message.MessageAttributes["BillPeriodId"].StringValue, out var sqsBillingPeriodId))
                {
                    LogInfo(context, "EXCEPTION", "Invalid Billing Period provided in message");
                    return;
                }

                billingPeriodId = sqsBillingPeriodId;
            }

            int tenantId = int.Parse(message.MessageAttributes["TenantId"].StringValue);
            var isLastInstance = false;
            SiteTypes customerType = (SiteTypes)int.Parse(message.MessageAttributes["CustomerType"].StringValue);
            Guid customerId = Guid.Empty;
            if (message.MessageAttributes.ContainsKey("CustomerId"))
            {
                customerId = Guid.Parse(message.MessageAttributes["CustomerId"].StringValue);
            }
            if (customerType == SiteTypes.Rev && (string.IsNullOrEmpty(customerId.ToString()) || customerId == Guid.Empty))
            {
                LogInfo(context, "EXCEPTION", "Blank Customer Id provided in message");
                return;
            }
            if (message.MessageAttributes.ContainsKey("IsLastInstance"))
            {
                isLastInstance = Convert.ToBoolean(message.MessageAttributes["IsLastInstance"].StringValue);
            }

            var messageId = message.MessageId;
            var optimizationSessionId = long.Parse(message.MessageAttributes["OptimizationSessionId"].StringValue);

            int? amopCustomerId = null;
            if (message.MessageAttributes.ContainsKey(SQSMessageKeyConstant.AMOP_CUSTOMER_ID))
            {
                amopCustomerId = int.Parse(message.MessageAttributes[SQSMessageKeyConstant.AMOP_CUSTOMER_ID].StringValue);
            }

            var additionalDataObject = new
            {
                data = new
                {
                    BillPeriodId = billingPeriodId,
                    SiteId = 0,
                    ServiceProviderId = serviceProviderId,
                    OptimizationType = 0,
                    OptimizationFrom = "group",
                    BillingPeriodStartDate = "",
                    BillingPeriodEndDate = "",
                    DeviceCount = 0,
                    TenantId = tenantId,
                }
            };
            string additionalData = Newtonsoft.Json.JsonConvert.SerializeObject(additionalDataObject);
            // AWXPORT-1210 - Proration only applies to carrier optimization
            bool usesProration = false;
            try
            {
                if (customerType == SiteTypes.Rev)
                {
                    var integrationAuthenticationId = int.Parse(message.MessageAttributes["IntegrationAuthenticationId"].StringValue);
                    await ProcessCustomerId(context, tenantId, customerId, serviceProviderId.Value, billingPeriodId, messageId,
                        integrationAuthenticationId, optimizationSessionId, usesProration, isLastInstance, customerType, additionalData);
                }
                else
                {
                    ArgumentNullException.ThrowIfNull(amopCustomerId);
                    await ProcessAMOPCustomerId(context, tenantId, customerType, amopCustomerId.Value, serviceProviderId.Value, billingPeriodId, messageId, optimizationSessionId, usesProration, isLastInstance, additionalData);
                }
            }
            finally
            {
                optimizationRepository.MarkProcessedOptimizationInstanceTrackingRecord(context, optimizationSessionId, customerId, amopCustomerId);
            }
        }

        private async Task ProcessCustomerId(KeySysLambdaContext context, int tenantId, Guid customerId,
            int serviceProviderId, int? billingPeriodId, string messageId, int integrationAuthenticationId, long optimizationSessionId,
            bool usesProration, bool isLastInstance, SiteTypes customerType, string additionalData)
        {
            LogInfo(context, "SUB", $"ProcessCustomerId({tenantId},{customerId},{serviceProviderId},{billingPeriodId},{messageId},{integrationAuthenticationId})");

            // get customer account number
            var revAccountNumber = GetRevAccountNumber(context, customerId);

            // get customer rate plans
            var customerRatePlans = GetMobilityCustomerRatePlans(context, customerId, billingPeriodId.GetValueOrDefault(), serviceProviderId, tenantId);
            // Disable bill in advance for Auto Change rate plan logic since it is not yet supported
            var useBillInAdvance = false;

            LogInfo(context, "INFO", $"Use Bill In Advance: {useBillInAdvance}");

            // start instance
            if (billingPeriodId.HasValue)
            {
                var billingPeriod = GetBillingPeriod(context, billingPeriodId.Value);
                var nextBillingPeriod = GetNextBillingPeriod(context, serviceProviderId, billingPeriod.BillingPeriodEnd);
                var billInAdvanceBillingPeriodId = nextBillingPeriod?.Id;

                LogInfo(context, "INFO", $"Bill In Advance Billing Period Id: {billInAdvanceBillingPeriodId}");

                if (useBillInAdvance && (billInAdvanceBillingPeriodId == null || billingPeriod == null))
                {
                    LogInfo(context, "ERROR", $"A Billing Period past Billing Period Id = {billingPeriodId.Value} could not be found for this Customer. So, billing in advance is not possible at this time. Optimization not run.");
                    return;
                }

                if (customerRatePlans.Count > 0)
                {
                    LogInfo(context, "INFO", $"Service Provider: {billingPeriod.ServiceProviderId}, Bill Period: {billingPeriod.BillingPeriodStart} - {billingPeriod.BillingPeriodEnd}, Next Bill Period: {nextBillingPeriod?.BillingPeriodStart} - {nextBillingPeriod?.BillingPeriodEnd}");

                    var instanceId = StartOptimizationInstance(context, tenantId, billingPeriod.ServiceProviderId,
                        customerId, messageId, integrationAuthenticationId, billingPeriod.BillingPeriodStart,
                        billingPeriod.BillingPeriodEnd, PortalTypes.Mobility, optimizationSessionId,
                        billingPeriodId.Value, useBillInAdvance, billInAdvanceBillingPeriodId);

                    var allSimCards = optimizationMobilityDeviceRepository.GetMobilityOptimizationSimCards(context, null, billingPeriod.ServiceProviderId, revAccountNumber, integrationAuthenticationId, billingPeriod.Id, tenantId);
                    // what kind of charge type should we use?
                    var chargeType = OptimizationChargeType.RateChargeAndOverage;
                    if (useBillInAdvance)
                    {
                        chargeType = OptimizationChargeType.OverageOnly;
                    }

                    LogInfo(context, CommonConstants.INFO, $"Charge Type: {chargeType}");

                    // check cache and send email if Redis cache is unreachable but is a valid connection string 
                    if (context.IsRedisConnectionStringValid && !IsUsingRedisCache)
                    {
                        await LogAndSendConfigurationIssueEmailAsync(context, ErrorNotificationEmailReceiver, optimizationSessionId, instanceId);
                    }

                    var isError = await ProcessDevicesByCustomerRatePlans(context, integrationAuthenticationId, usesProration, revAccountNumber, null, customerRatePlans, billingPeriod, nextBillingPeriod, instanceId, chargeType, customerType, tenantId);

                    // enqueue cleanup method
                    if (!isError)
                    {
                        EnqueueCleanup(context, instanceId, CommonConstants.DELAY_IN_SECONDS_FIFTEEN_SECONDS, serviceProviderId, isCustomerOptimization: true, isLastInstance);

                        // Record charges for devices with no rate plans
                        ProcessNoRatePlanDevices(context, usesProration, billingPeriod, instanceId, allSimCards);
                    }
                    else
                    {
                        var errorMessage = "There is an error in Processing Customer Rate Plans";
                        UpdateCustomerOptimization(context, optimizationSessionId, errorMessage, serviceProviderId, revAccountNumber);
                        StopOptimizationInstance(context, instanceId, OptimizationStatus.CompleteWithErrors);
                        //triggger AMOP2.0 to send error message
                        OptimizationAmopApiTrigger.SendResponseToAMOP20(context, "ErrorMessage", optimizationSessionId.ToString(), null, 0, errorMessage, 0, revAccountNumber, additionalData);
                    }
                }
                else
                {
                    StopOptimizationInstance(context, tenantId, customerId, integrationAuthenticationId, null, serviceProviderId, billingPeriodId, messageId, optimizationSessionId, useBillInAdvance, billingPeriod, billInAdvanceBillingPeriodId, additionalData: additionalData);
                }
            }
            else
            {
                LogInfo(context, "ERROR", "No Billing Periods for this Customer");
            }
        }

        private async Task ProcessAMOPCustomerId(KeySysLambdaContext context, int tenantId, SiteTypes customerType, int amopCustomerId,
            int serviceProviderId, int? billingPeriodId, string messageId, long optimizationSessionId,
            bool usesProration, bool isLastInstance, string additionalData)
        {
            LogInfo(context, "SUB", $"ProcessAMOPCustomerId({tenantId},{amopCustomerId},{serviceProviderId},{billingPeriodId},{messageId})");

            // get customer rate plans
            var customerRatePlans = GetMobilityCustomerRatePlans(context, Guid.Empty, billingPeriodId.GetValueOrDefault(), serviceProviderId, tenantId, customerType, amopCustomerId);
            // Disable bill in advance for Auto Change rate plan logic since it is not yet supported
            var useBillInAdvance = false;

            LogInfo(context, "INFO", $"Use Bill In Advance: {useBillInAdvance}");

            // start instance
            if (billingPeriodId.HasValue)
            {
                var billingPeriod = GetBillingPeriod(context, billingPeriodId.Value);
                var nextBillingPeriod = GetNextBillingPeriod(context, serviceProviderId, billingPeriod.BillingPeriodEnd);
                var billInAdvanceBillingPeriodId = nextBillingPeriod?.Id;

                LogInfo(context, "INFO", $"Bill In Advance Billing Period Id: {billInAdvanceBillingPeriodId}");

                if (useBillInAdvance && (billInAdvanceBillingPeriodId == null || billingPeriod == null))
                {
                    LogInfo(context, "ERROR", $"A Billing Period past Billing Period Id = {billingPeriodId.Value} could not be found for this Customer. So, billing in advance is not possible at this time. Optimization not run.");
                    return;
                }

                if (customerRatePlans.Count > 0)
                {
                    LogInfo(context, "INFO", $"Service Provider: {billingPeriod.ServiceProviderId}, Bill Period: {billingPeriod.BillingPeriodStart} - {billingPeriod.BillingPeriodEnd}, Next Bill Period: {nextBillingPeriod?.BillingPeriodStart} - {nextBillingPeriod?.BillingPeriodEnd}");

                    var allSimCards = optimizationMobilityDeviceRepository.GetMobilityOptimizationSimCards(context, null, billingPeriod.ServiceProviderId, null, null, billingPeriod.Id, tenantId, customerType, amopCustomerId);

                    var instanceId = StartOptimizationInstance(context, tenantId, billingPeriod.ServiceProviderId,
                        null, messageId, null, billingPeriod.BillingPeriodStart,
                        billingPeriod.BillingPeriodEnd, PortalTypes.Mobility, optimizationSessionId,
                        billingPeriodId.Value, useBillInAdvance, billInAdvanceBillingPeriodId, amopCustomerId);

                    // what kind of charge type should we use?
                    var chargeType = OptimizationChargeType.RateChargeAndOverage;
                    if (useBillInAdvance)
                    {
                        chargeType = OptimizationChargeType.OverageOnly;
                    }

                    LogInfo(context, "INFO", $"Charge Type: {chargeType}");

                    // check cache and send email if Redis cache is unreachable but is a valid connection string 
                    if (context.IsRedisConnectionStringValid && !IsUsingRedisCache)
                    {
                        await LogAndSendConfigurationIssueEmailAsync(context, ErrorNotificationEmailReceiver, optimizationSessionId, instanceId);
                    }

                    var isError = await ProcessDevicesByCustomerRatePlans(context, null, usesProration, null, amopCustomerId, customerRatePlans, billingPeriod, nextBillingPeriod, instanceId, chargeType, customerType, tenantId);

                    if (!isError)
                    {
                        // enqueue cleanup method
                        EnqueueCleanup(context, instanceId, CommonConstants.DELAY_IN_SECONDS_FIFTEEN_SECONDS, serviceProviderId, isCustomerOptimization: true, isLastInstance);

                        // Record charges for devices with no rate plans
                        ProcessNoRatePlanDevices(context, usesProration, billingPeriod, instanceId, allSimCards);
                    }
                    else
                    {
                        var errorMessage = "There is an error in Processing Customer Rate Plans";
                        UpdateCustomerOptimization(context, optimizationSessionId, errorMessage, serviceProviderId, string.Empty, amopCustomerId);
                        StopOptimizationInstance(context, instanceId, OptimizationStatus.CompleteWithErrors);
                        //triggger AMOP2.0 to send error message
                        OptimizationAmopApiTrigger.SendResponseToAMOP20(context, "ErrorMessage", optimizationSessionId.ToString(), null, 0, errorMessage, 0, amopCustomerId.ToString(), additionalData);
                    }
                }
                else
                {
                    StopOptimizationInstance(context, tenantId, null, null, amopCustomerId, serviceProviderId, billingPeriodId, messageId, optimizationSessionId, useBillInAdvance, billingPeriod, billInAdvanceBillingPeriodId, additionalData: additionalData);
                }
            }
            else
            {
                LogInfo(context, "ERROR", "No Billing Periods for this Customer");
            }
        }

        private void StopOptimizationInstance(KeySysLambdaContext context, int tenantId, Guid? customerId, int? integrationAuthenticationId, int? amopCustomerId, int serviceProviderId, int? billingPeriodId, string messageId, long optimizationSessionId, bool useBillInAdvance, BillingPeriod billingPeriod, int? billInAdvanceBillingPeriodId, long instanceId = 0, string additionalData = null)
        {
            if (instanceId == 0)
            {
                var minStart = billingPeriod.BillingPeriodStart;
                var maxEnd = billingPeriod.BillingPeriodEnd;
                instanceId = StartOptimizationInstance(context, tenantId, billingPeriod.ServiceProviderId,
                    customerId, messageId, integrationAuthenticationId, minStart, maxEnd,
                    PortalTypes.Mobility, optimizationSessionId,
                    billingPeriodId.Value, useBillInAdvance, billInAdvanceBillingPeriodId, amopCustomerId);
            }

            var errorMessage = "No Rate Plans for this Instance";
            LogInfo(context, CommonConstants.ERROR, errorMessage);
            UpdateCustomerOptimization(context, optimizationSessionId, errorMessage, serviceProviderId, string.Empty, amopCustomerId);
            StopOptimizationInstance(context, instanceId, OptimizationStatus.CompleteWithErrors);
            //triggger AMOP2.0 to send error message
            OptimizationAmopApiTrigger.SendResponseToAMOP20(context, "ErrorMessage", optimizationSessionId.ToString(), null, 0, errorMessage, 0, amopCustomerId.ToString(), additionalData);
        }

        protected async Task<bool> ProcessDevicesByCustomerRatePlans(KeySysLambdaContext context, int? integrationAuthenticationId, bool usesProration, string revAccountNumber, int? amopCustomerId, List<RatePlan> ratePlans, BillingPeriod billingPeriod, BillingPeriod nextBillingPeriod, long instanceId, OptimizationChargeType chargeType, SiteTypes customerType, int tenantId)
        {
            List<vwOptimizationSimCard> optimizationSimCards = GetOptimizationSimCardsByPortalType(context, integrationAuthenticationId, revAccountNumber, amopCustomerId, billingPeriod, null, customerType, tenantId);
            if (revAccountNumber != null || amopCustomerId != null)
            {
                optimizationSimCards = optimizationSimCards.Where(s => !string.IsNullOrWhiteSpace(s.CustomerRatePlanCode)).ToList();
            }

            // Process Pooled by Customer Rate Pool
            var ratePlansByCustomerRatePool = ratePlans.Where(ratePlan => !ratePlan.AutoChangeRatePlan).ToList();
            if (ratePlansByCustomerRatePool.Any())
            {
                if (CheckZeroValueRatePlans(context, instanceId, ratePlansByCustomerRatePool, shouldStopInstance: true))
                {
                    return true;
                }
                else
                {
                    // process and return the remaining devices for optimization with algorithm
                    optimizationSimCards = ProcessDevicesWithAutoChangeDisabledRatePlans(context, integrationAuthenticationId, usesProration, revAccountNumber, amopCustomerId, billingPeriod, nextBillingPeriod, instanceId, optimizationSimCards, ratePlansByCustomerRatePool, tenantId);
                }
            }

            var simCardsByRatePoolIds = optimizationSimCards.GroupBy(x => x.CustomerRatePoolId).Distinct();

            foreach (var simCardsByRatePoolId in simCardsByRatePoolIds)
            {
                // Get all rate plan codes from the devices
                var ratePlanCodes = simCardsByRatePoolId.Select(x => x.CustomerRatePlanCode).Distinct();
                var isError = false;
                if (simCardsByRatePoolId.Key != null)
                {
                    // Get all rate plans with matching rate plan codes
                    var ratePlansForPool = ratePlans.Where(x => ratePlanCodes.Contains(x.PlanName));
                    isError = await ProcessRatePoolGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, amopCustomerId, billingPeriod, instanceId, chargeType, ratePlansForPool, simCardsByRatePoolId.ToList(), simCardsByRatePoolId?.Key, queuesPerInstance: QueuesPerInstance);
                }
                else
                {
                    LogInfo(context, CommonConstants.INFO, $"Devices without customer rate pool, running original logic of grouping by rate plan code");
                    // Group rate plans by rate plan code and run auto change optimization logic for this group of devices
                    var ratePlansByCodes = ratePlans.Where(ratePlan => ratePlan.AutoChangeRatePlan && ratePlanCodes.Contains(ratePlan.PlanName)).GroupBy(x => x.PlanName);
                    foreach (var ratePlansByCode in ratePlansByCodes)
                    {
                        isError = await ProcessPlanNameGroup(context, integrationAuthenticationId, usesProration, revAccountNumber, amopCustomerId, billingPeriod, instanceId, chargeType, ratePlansByCode, simCardsByRatePoolId.ToList());
                    }
                }

                // If error, stop optimization midway
                if (isError)
                {
                    return isError;
                }
            }
            return false;
        }

        protected async Task<bool> ProcessPlanNameGroup(KeySysLambdaContext context, int? integrationAuthenticationId, bool usesProration, string revAccountNumber, int? AMOPCustomerId, BillingPeriod billingPeriod, long instanceId, OptimizationChargeType chargeType, IGrouping<string, RatePlan> planNameGroup, List<vwOptimizationSimCard> optimizationSimCards)
        {
            foreach (var ratePlanGroup in planNameGroup.GroupBy(x => x.AllowsSimPooling))
            {
                LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.RATE_PLAN_CODE_MESSAGE, planNameGroup.Key));
                LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.ALLOW_SIM_POOLING_MESSAGE, ratePlanGroup.Key));

                // get rate plans for group
                var groupRatePlans = ratePlanGroup.ToList();
                if (CheckZeroValueRatePlans(context, instanceId, groupRatePlans, shouldStopInstance: true))
                {
                    return true;
                }

                // filter rate plans that are used for auto change rate plan
                if (optimizationSimCards.Count == 0)
                {
                    // No more devices to process the next steps for this rate plan group
                    // if there are devices but no rate plans, the devices could be unassigned devices so it is expected
                    LogInfo(context, LogTypeConstant.Info, string.Format(LogCommonStrings.NO_DEVICE_FOUND_FOR_RATE_PLAN_GROUP_TO_OPTIMIZE, planNameGroup.Key, ratePlanGroup.Key));
                    continue;
                }
                // create new comm plan group
                var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
                var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
                var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
                var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools);

                var baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId,
                    revAccountNumber, integrationAuthenticationId, null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId);
                // add rate plans to comm plan group
                var commGroupRatePlanTable = AddCustomerRatePlansToCommPlanGroup(context, instanceId, commPlanGroupId, calculatedPlans);

                // zero sim card => no need to run optimizer
                // one sim card => swapping between rate plans would be the same as base device assignment
                //              => already calculate that => no need to run optimizer
                if (baseAssignedSimCardsCount > OptimizationConstant.BaseAssignedDeviceLimit)
                {
                    // permute rate plans
                    if (calculatedPlans.Count > OptimizationConstant.RatePlanLimit)
                    {
                        LogInfo(context, CommonConstants.WARNING, string.Format(LogCommonStrings.RATE_PLAN_LIMIT_ERROR, OptimizationConstant.RatePlanLimit, ratePlanGroup.Key));
                        continue;
                    }
                    if (calculatedPlans.Count <= OptimizationConstant.RatePlanMinimumLimit)
                    {

                        LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.AUTO_CHANGE_MINIMUM_RATE_PLAN_LIMIT_REACHED, calculatedPlans.Count, planNameGroup.Key, ratePlanGroup.Key));
                        continue;
                    }
                    GeneratePermutationQueueRatePlans(context, usesProration, billingPeriod, instanceId, commPlanGroupId, ratePoolCollection, commGroupRatePlanTable);

                    // enqueue rate plan permutations
                    await EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, QueuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true);
                }
                else
                {
                    LogInfo(context, LogTypeConstant.Info, string.Format(LogCommonStrings.NOT_ENOUGH_DEVICE_FOR_OPTIMIZATION_USING_PERMUTATION, string.Join(',', ratePlanGroup.Select(plan => plan.Id).ToList()), baseAssignedSimCardsCount));
                }
            }
            return false;
        }

        protected void ProcessNoRatePlanDevices(KeySysLambdaContext context, bool usesProration, BillingPeriod billingPeriod, long instanceId, List<vwOptimizationSimCard> allSimCards)
        {
            // Handle devices with no assigned customer rate plan or carrier rate plan code
            var noRatePlanSimCards = allSimCards
                .Where(c => string.IsNullOrWhiteSpace(c.CustomerRatePlanCode))
                .ToList();

            if (noRatePlanSimCards.Any())
            {
                LogInfo(context, "INFO", $"Unused SIM Cards with no Customer Rate Plan Codes: {noRatePlanSimCards.Count}");

                var unusedCommPlanGroupId = CreateCommPlanGroup(context, instanceId);
                var unusedQueueId = CreateQueue(context, instanceId, unusedCommPlanGroupId, null, usesProration);
                StartQueue(context, unusedQueueId, string.Empty);
                var simsWithNoRatePlanCodes = ProjectDataUsageAndSaveDeviceByPortalType(context, billingPeriod, instanceId, noRatePlanSimCards);

                OptimizationResultDbWriter.RecordRatePool(context, context.ConnectionString, unusedQueueId, billingPeriod.Id, simsWithNoRatePlanCodes, PortalType);
                OptimizationResultDbWriter.RecordTotalCost(context, context.ConnectionString, unusedQueueId, OptimizationConstant.DefaultNoRatePlanDeviceCost);
                StopQueue(context, unusedQueueId);
            }
        }

    }
}
