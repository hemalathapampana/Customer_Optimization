using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.SqlClient;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using Amazon;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amop.Core.Constants;
using Amop.Core.Helpers;
using Amop.Core.Logger;
using CsvHelper;
using CsvHelper.Configuration;
using IdGen;
using KeySys.BaseMultiTenant.Helpers;
using KeySys.BaseMultiTenant.Helpers.Http;
using KeySys.BaseMultiTenant.Models;
using KeySys.BaseMultiTenant.Models.CustomClasses;
using KeySys.BaseMultiTenant.Models.CustomerCharge;
using KeySys.BaseMultiTenant.Models.Device;
using KeySys.BaseMultiTenant.Models.M2M;
using KeySys.BaseMultiTenant.Models.Mobility;
using KeySys.BaseMultiTenant.Models.Optimization;
using KeySys.BaseMultiTenant.Models.Report;
using KeySys.BaseMultiTenant.Models.Repositories;
using KeySys.BaseMultiTenant.Repositories.Optimization;
using KeySys.BaseMultiTenant.Repositories.Rev;
using KeySys.BaseMultiTenant.Services;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Renci.SshNet;

namespace KeySys.BaseMultiTenant.Controllers.AmopInternal
{
    public class OptimizationApiController : BaseCoreApiController
    {
        protected HttpWebApiSessionState session;
        protected string userName;
        protected string xTenantId;
        AltaWorxCentral_Entities altaWrxDb = new AltaWorxCentral_Entities();
        AmopBaseController amopBaseController = new AmopBaseController();
        private const string CREATE_CUSTOMER_CHARGE_QUEUE_NAME = "Create Customer Charge Queue";
        private const string CARRIER_OPTIMIZATION_QUEUE_NAME = "Carrier Optimization Queue";
        private const string M2M_CUSTOMER_OPTIMIZATION_QUEUE_NAME = "Customer Optimization Queue";
        private const string MOBILITY_CUSTOMER_OPTIMIZATION_QUEUE_NAME = "Mobility Customer Optimization Queue";
        private const string CROSS_PROVIDER_CUSTOMER_OPTIMIZATION_QUEUE_NAME = "Cross Provider Customer Optimization Queue";
        private const string RATE_PLAN_QUEUE_NAME = "Rate Plan Queue";
        private const string CUSTOMER_CHARGE_QUEUE_NAME = "Customer Charge Queue";
        protected const string S3_BUCKET_NAME = "S3 Bucket Name";
        private string optimizationSessionGuid = "";
        private int deviceCount = 0;

        protected virtual PermissionManager permissionManager { get; set; }
        protected virtual User user { get; set; }

        protected virtual void ValidateHeader()
        {
            if (!Request.Headers.TryGetValues("user-name", out var userNameValues))
            {
                throw new HttpResponseException(System.Net.HttpStatusCode.BadRequest);
            }
            else
            {
                userName = userNameValues.FirstOrDefault();
                userName = userName.Trim().ToLower();
            }
            if (!Request.Headers.TryGetValues("x-tenant-id", out var xTenantIdValues))
            {
                throw new HttpResponseException(System.Net.HttpStatusCode.BadRequest);
            }
            else
            {
                xTenantId = xTenantIdValues.FirstOrDefault();
            }
            var userRepository = new UserRepository(Database);
            user = userRepository.GetByUsername(userName) ?? throw new HttpResponseException(System.Net.HttpStatusCode.BadRequest);
            session = new HttpWebApiSessionState();
            SessionHelper.SetValidated(session, true);
            SessionHelper.SetUserId(session, user.id);
            SessionHelper.SetUser(session, user);
            SessionHelper.SetLoggedInUserId(session, user.id);
            SessionHelper.SetLoggedInUser(session, user);
            permissionManager = new PermissionManager(Database, session, user.id, int.TryParse(xTenantId, out var tid) ? tid : (int?)null);
            SessionHelper.SetPermissionManager(session, permissionManager);

        }
        #region Start Optimization Button
        [System.Web.Http.HttpPost]
        [System.Web.Http.ActionName("start-confirm")]
        public async Task<string> StartConfirm([System.Web.Http.FromBody] OptimizationRequestDto request)
        {
            try
            {

                ValidateRequest();
                ValidateHeader();
                if (!permissionManager.UserCanCreate(session, ModuleEnum.Optimization))
                    return "Access denied";

                var siteType = SiteType.Rev;
                if (!permissionManager.UserCanAccess(session, ModuleEnum.RevCustomers))
                {
                    siteType = SiteType.AMOP;
                }
                // get tenant custom fields                
                var customObjectDbList = permissionManager.CustomFields;
                string awsAccessKey = amopBaseController.AwsAccessKeyFromCustomObjects(customObjectDbList);
                string awsSecretAccessKey = amopBaseController.AwsSecretAccessKeyFromCustomObjects(customObjectDbList);
                string errorMessage;
                OptimizationSessionRepository optimizationSessionRepository = new OptimizationSessionRepository(altaWrxDb);

                BillingPeriod billPeriod = new BillingPeriod();
                CustomerBillingPeriod customerBillPeriod = new CustomerBillingPeriod();
                long optimizationSessionId = 0;
                string serviceProvidersString = string.Empty;
                List<DateTime> endDateList = new List<DateTime>();
                var additionalDataObject = new
                {
                    data = new
                    {
                        BillPeriodId = request.BillPeriodId,
                        SiteId = request.SiteId,
                        ServiceProviderId = request.ServiceProviderId,
                        ServiceProviderIds = string.IsNullOrWhiteSpace(request.ServiceProviderIds)
                                                ? string.Empty
                                                : request.ServiceProviderIds.Replace("[", "").Replace("]", ""),
                        OptimizationType = request.OptimizationType,
                        OptimizationFrom = request.OptimizationFrom,
                        BillingPeriodStartDate = request.BillingPeriodStartDate,
                        BillingPeriodEndDate = request.BillingPeriodEndDate,
                        DeviceCount = request.DeviceCount,
                        TenantId = xTenantId,
                    }
                };
                string additionalData = Newtonsoft.Json.JsonConvert.SerializeObject(additionalDataObject);

                if (request.OptimizationType == OptimizationType.Carrier || !permissionManager.OptimizationSettings.OptIntoCrossProviderCustomerOptimization)
                {
                    billPeriod = altaWrxDb.BillingPeriods.AsNoTracking().FirstOrDefault(x => x.id == request.BillPeriodId);
                    if (billPeriod != null)
                    {
                        optimizationSessionId = await CreateOptimizationSession(billPeriod.BillingCycleStartDate, billPeriod.BillingCycleEndDate, request.ServiceProviderId, null, request.SiteId, request.OptimizationType);
                    }
                }
                else
                {
                    Site site = new Site();
                    using (var db = new AltaWorxCentral_Entities())
                    {
                        customerBillPeriod = db.CustomerBillingPeriods.AsNoTracking().FirstOrDefault(x => x.id == request.BillPeriodId);
                        site = altaWrxDb.Sites.AsNoTracking().FirstOrDefault(x => x.id == request.SiteId);
                    }
                    DateTime billingCycleEndDate;
                    DateTime billingCycleStartDate;
                    if (site == null)
                    {
                        var endDayList = request.BillingEndDayList.Split(',').ToList();
                        endDateList = endDayList.Select(x => new DateTime(customerBillPeriod.BillYear, customerBillPeriod.BillMonth,
                            int.Parse(x.Split('/')[0]))).OrderBy(x => x).ToList();
                        billingCycleEndDate = endDateList[endDateList.Count - 1];
                        billingCycleStartDate = endDateList[0].AddMonths(-1);
                    }
                    else
                    {
                        billingCycleEndDate = new DateTime(customerBillPeriod?.BillYear ?? 18, customerBillPeriod?.BillMonth ?? 18, site?.CustomerBillPeriodEndDay ?? 0, site?.CustomerBillPeriodEndHour.Value ?? 0, 0, 0);
                        billingCycleStartDate = billingCycleEndDate.AddMonths(-1);
                    }
                    var serviceProviders = new List<int>();
                    if (request.ServiceProviderIds != null)
                    {
                        serviceProviders = JsonConvert.DeserializeObject<List<int>>(request.ServiceProviderIds);
                    }
                    serviceProvidersString = string.Join(",", serviceProviders);
                    if (billPeriod != null)
                    {
                        optimizationSessionId = await CreateOptimizationSession(billingCycleStartDate, billingCycleEndDate, request.ServiceProviderId, serviceProvidersString, request.SiteId, request.OptimizationType);
                    }
                }

                if (optimizationSessionId == 0)
                    errorMessage = "Error creating Optimization Session. Please contact AMOP Support.";
                else
                {
                    Log.Error($"Progress 0 - Started");
                    //triggger AMOP2.0 to send Optimization Progress
                    await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 0, "", additionalData);
                    switch (request.OptimizationType)
                    {
                        case OptimizationType.Customer:
                            errorMessage = await EnqueueCustomerOptimizationAsync(billPeriod, customerBillPeriod, request.SiteId, request.ServiceProviderId, serviceProvidersString, customObjectDbList,
                                awsAccessKey, awsSecretAccessKey, optimizationSessionId, siteType, endDateList, additionalData);
                            break;
                        case OptimizationType.Carrier:
                            errorMessage = await EnqueueCarrierOptimizationAsync(billPeriod, request.ServiceProviderId, customObjectDbList, awsAccessKey,
                                awsSecretAccessKey, optimizationSessionId, additionalData);
                            break;
                        default:
                            errorMessage = $"Unhandled optimization type: {request.OptimizationType}";
                            break;
                    }
                }

                if (string.IsNullOrEmpty(errorMessage))
                {
                    var delay = request.OptimizationType == OptimizationType.Carrier ? "within 10 minutes, after syncing most recent usage" : "within 60 seconds";
                    var successMessage = $"Successfully started optimization should appear in the list {delay}. Please allow ample time for the optimization to complete. Larger jobs can take >30 minutes to process.";
                    SessionHelper.SetAlert(session, successMessage);
                    SessionHelper.SetAlertType(session, "success");
                    //To send 100% progress only after Optimization is completed
                    var startTime = DateTime.UtcNow;
                    var waitTime = TimeSpan.FromSeconds(30);

                    int count = 0;
                    try
                    {
                        while (true)
                        {
                            Log.Error($"While loop running - wait time: " + waitTime + " - " + count);
                            var result = CheckOptimizationIsRunning(Convert.ToInt32(xTenantId));
                            Log.Info($"Optimization running: " + result);
                            deviceCount = GetOptimizationDeviceCountByInstance(Convert.ToInt32(optimizationSessionId));
                            Log.Info($"Device Count: " + deviceCount);
                            await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, deviceCount, null, 40, "", additionalData);
                            if (!result)
                            {
                                Log.Error($"Optimization Completed");
                                break;// Exit the loop when the condition is false
                            }
                            await Task.Delay(waitTime);
                            count++;
                        }
                        Log.Error($"Progress 50 - Success");
                        await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, deviceCount, "Success", 50, "", additionalData);
                        return ("Sucess");
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"Progress 50 - Failed");
                        await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, "Failed - " + ex.ToString(), 50, "", additionalData);
                        return ("Failed");
                    }
                }
                else
                {
                    SessionHelper.SetAlert(session, errorMessage);
                    SessionHelper.SetAlertType(session, "danger");
                    Log.Error($"Progress 50 - Failed");
                    await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, "Failed", 50, "", additionalData);
                    return ("Failed");
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex).ToString();
            }
        }
        private async Task<long> CreateOptimizationSession(DateTime? billPeriodBillingCycleStartDate, DateTime? billPeriodBillingCycleEndDate, int? serviceProviderId, string serviceProviderIds, int? siteId, OptimizationType optimizationType)
        {
            var optimizationSesstionRepository = new OptimizationSessionRepository(altaWrxDb);
            var optimizationSession = new OptimizationSession
            {
                SessionId = Guid.NewGuid(),
                BillingPeriodStartDate = billPeriodBillingCycleStartDate.GetValueOrDefault(),
                BillingPeriodEndDate = billPeriodBillingCycleEndDate.GetValueOrDefault(),
                TenantId = permissionManager.Tenant.id,
                ServiceProviderId = serviceProviderId,
                ServiceProviderIds = serviceProviderIds,
                SiteId = siteId,
                OptimizationTypeId = (int)optimizationType,
                CreatedBy = SessionHelper.GetAuditByName(session),
                CreatedDate = DateTime.UtcNow,
                IsActive = true,
                IsDeleted = false
            };
            optimizationSessionGuid = optimizationSession.SessionId.ToString();
            await optimizationSesstionRepository.CreateOptimizationSession(optimizationSession);
            return optimizationSession.Id;
        }
        private async Task<string> EnqueueCustomerOptimizationAsync(BillingPeriod billPeriod, CustomerBillingPeriod customerBillPeriod, int? siteId, int? serviceProviderId, string serviceProviderIds, IList<CustomObject> customObjectDbList,
            string awsAccessKey, string awsSecretAccessKey, long optimizationSessionId, SiteType siteType, List<DateTime> endDateList, string additionalData = null)
        {
            var hasCustomer = siteId.HasValue;
            if (!hasCustomer && !serviceProviderId.HasValue && !permissionManager.OptimizationSettings.OptIntoCrossProviderCustomerOptimization)
            {
                return "Service Provider or Customer must be supplied to start.";
            }

            var siteFilter = permissionManager.PermissionFilter.GetSiteIdFilter();
            if (!permissionManager.UserIsSuperAdmin(session) && !permissionManager.UserIsTenantAdmin(session) && siteFilter.IsRestricted && siteId.HasValue && !siteFilter.FilterValues.Contains(siteId.Value))
            {
                return "Insufficient privileges to queue optimization for customer.";
            }

            string m2mCustomerOptimizationQueueName = M2MCustomerOptimizationQueueFromCustomObjects(customObjectDbList);
            string mobilityCustomerOptimizationQueueName = MobilityCustomerOptimizationQueueFromCustomObjects(customObjectDbList);
            string crossProviderCustomerOptimizationQueueName = CrossProviderCustomerOptimizationQueueFromCustomObjects(customObjectDbList);
            await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 10, "", additionalData);
            if (hasCustomer)
            {
                return await EnqueueSingleCustomerOptimizationAsync(billPeriod, customerBillPeriod, permissionManager.Tenant.id, siteId.Value, serviceProviderId, serviceProviderIds, awsAccessKey,
                    awsSecretAccessKey, m2mCustomerOptimizationQueueName, mobilityCustomerOptimizationQueueName, crossProviderCustomerOptimizationQueueName, optimizationSessionId, siteType, additionalData);
            }

            var serviceProvider = altaWrxDb.ServiceProviders.Include(sp => sp.Integration).FirstOrDefault(sp => sp.id == serviceProviderId);
            if (serviceProvider == null && !permissionManager.OptimizationSettings.OptIntoCrossProviderCustomerOptimization)
            {
                return "Service Provider not found.";
            }

            PortalTypes portalType;
            if (permissionManager.OptimizationSettings.OptIntoCrossProviderCustomerOptimization)
            {
                portalType = PortalTypes.CrossProvider;
            }
            else
            {
                portalType = (PortalTypes)serviceProvider.Integration.PortalTypeId;
            }
            await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 20, "", additionalData);
            switch (portalType)
            {
                case PortalTypes.M2M:
                    return await EnqueueAllCustomersOptimizationAsync(billPeriod, permissionManager.Tenant.id, serviceProvider,
                        awsAccessKey, awsSecretAccessKey, m2mCustomerOptimizationQueueName, optimizationSessionId, siteType, additionalData);
                case PortalTypes.Mobility:
                    return await EnqueueAllCustomersOptimizationAsync(billPeriod, permissionManager.Tenant.id, serviceProvider,
                        awsAccessKey, awsSecretAccessKey, mobilityCustomerOptimizationQueueName, optimizationSessionId, siteType, additionalData);
                case PortalTypes.CrossProvider:
                    return await EnqueueCrossAllCustomersOptimizationAsync(customerBillPeriod, permissionManager.Tenant.id, serviceProviderIds,
                        awsAccessKey, awsSecretAccessKey, crossProviderCustomerOptimizationQueueName, optimizationSessionId, siteType, endDateList, additionalData);
                default:
                    return $"Unhandled portal type: {portalType}";
            }
        }
        private async Task<string> EnqueueCarrierOptimizationAsync(BillingPeriod billPeriod, int? serviceProviderId,
            IList<CustomObject> customObjectDbList, string awsAccessKey, string awsSecretAccessKey, long optimizationSessionId, string additionalData = null)
        {
            if (!serviceProviderId.HasValue)
            {
                return "Service Provider must be supplied to start carrier optimization.";
            }

            if (!permissionManager.UserIsSuperAdmin(session) && !permissionManager.UserIsTenantAdmin(session))
            {
                return "Insufficient privileges to queue carrier optimization.";
            }

            string carrierOptimizationQueueName = CarrierOptimizationQueueFromCustomObjects(customObjectDbList);
            await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 20, "", additionalData);
            return await EnqueueCarrierOptimizationSqsAsync(billPeriod, permissionManager.Tenant.id, awsAccessKey,
                awsSecretAccessKey, carrierOptimizationQueueName, serviceProviderId.Value, optimizationSessionId, additionalData);
        }
        private async Task<string> EnqueueCarrierOptimizationSqsAsync(BillingPeriod billPeriod, int tenantId, string awsAccessKey,
            string awsSecretAccessKey, string carrierOptimizationQueueName, int serviceProviderId, long optimizationSessionId, string additionalData = null)
        {
            try
            {
                await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 30, "", additionalData);
                var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(awsAccessKey, awsSecretAccessKey);
                using (var client = new AmazonSQSClient(awsCredentials, Amazon.RegionEndpoint.USEast1))
                {
                    var queueList = client.ListQueues(carrierOptimizationQueueName);
                    if (queueList.HttpStatusCode == System.Net.HttpStatusCode.OK && queueList.QueueUrls != null && queueList.QueueUrls.Count > 0)
                    {
                        var requestMsgBody = $"Carrier to optimize is for Billing Period {billPeriod.BillYear}/{billPeriod.BillMonth}";
                        var request = new SendMessageRequest
                        {
                            MessageAttributes = new Dictionary<string, MessageAttributeValue>
                            {
                                {"ServiceProviderId", new MessageAttributeValue {DataType = "String", StringValue = serviceProviderId.ToString()}},
                                {"TenantId", new MessageAttributeValue {DataType = "String", StringValue = tenantId.ToString()}},
                                {"BillPeriodId", new MessageAttributeValue {DataType = "String", StringValue = billPeriod.id.ToString()}},
                                {"OptimizationSessionId", new MessageAttributeValue {DataType = "String", StringValue = optimizationSessionId.ToString()}},
                                {SQSMessageKeyConstant.PORTAL_TYPE_ID, new MessageAttributeValue {DataType = nameof(String), StringValue = billPeriod.ServiceProvider.Integration.PortalTypeId.ToString()}}
                            },
                            MessageBody = requestMsgBody,
                            QueueUrl = queueList.QueueUrls[0]
                        };

                        // Skip sync if billing period is closed or carrier does not support sync on optimization
                        if (billPeriod.BillingCycleEndDate < DateTime.Now
                            || billPeriod.ServiceProvider.IntegrationId == (int)IntegrationEnum.Telegence)
                        {
                            request.MessageAttributes.Add("HasSynced", new MessageAttributeValue { DataType = "String", StringValue = true.ToString() });
                        }
                        var response = await client.SendMessageAsync(request);
                        await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 40, "", additionalData);
                        return response.HttpStatusCode.IsSuccessStatusCode()
                            ? string.Empty
                            : $"Error Queuing Optimization: {response.HttpStatusCode:D} {response.HttpStatusCode:G}";
                    }

                    return "Error Queuing Optimization: Queue not found";
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error Queuing Optimization", ex);
                return "Error Queue Optimization: Exception occured";
            }
        }
        private async Task<string> EnqueueSingleCustomerOptimizationAsync(BillingPeriod billPeriod, CustomerBillingPeriod customerBillPeriod, int tenantId, int siteId,
            int? serviceProviderId, string serviceProviderIds, string awsAccessKey, string awsSecretAccessKey, string m2mCustomerOptimizationQueueName,
            string mobilityCustomerOptimizationQueueName, string crossProviderCustomerOptimizationQueueName, long optimizationSessionId, SiteType siteType, string additionalData = null)
        {
            var site = altaWrxDb.Sites.Include(s => s.RevCustomer).FirstOrDefault(x => x.id == siteId);
            if (site == null)
            {
                return "Customer not found.";
            }

            if (siteType == SiteType.Rev && site.RevCustomer != null && site.RevCustomer?.IntegrationAuthenticationId == null)
            {
                return "No valid billing provider credentials found for customer.";
            }
            var revCustId = string.Empty;
            int? integrationAuthId = null;
            var optCus = new OptimizationCustomerProcessing()
            {
                StartTime = DateTime.UtcNow,
                IsProcessed = false,
                ServiceProviderId = serviceProviderId,
                SessionId = optimizationSessionId
            };
            if (siteType == SiteType.Rev)
            {
                revCustId = site.RevCustomer.id.ToString();
                integrationAuthId = site.RevCustomer.IntegrationAuthenticationId.Value;

                optCus.CustomerId = site.RevCustomer.RevCustomerId;
                optCus.CustomerName = $"{site.RevCustomer.CustomerName} ({site.RevCustomer.RevCustomerId})";
            }
            else
            {
                optCus.AMOPCustomerId = site.id;
                optCus.AMOPCustomerName = site.Name;
            }
            altaWrxDb.OptimizationCustomerProcessings.Add(optCus);
            altaWrxDb.SaveChanges();
            await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 20, "", additionalData);
            return await EnqueueCustomerOptimizationSqsAsync(billPeriod, customerBillPeriod, tenantId, site.id,
                integrationAuthId, serviceProviderId, serviceProviderIds, awsAccessKey, awsSecretAccessKey,
                m2mCustomerOptimizationQueueName, mobilityCustomerOptimizationQueueName, crossProviderCustomerOptimizationQueueName, revCustId,
                optimizationSessionId, siteType, additionalData);
        }
        private async Task<string> EnqueueAllCustomersOptimizationAsync(BillingPeriod billPeriod, int tenantId, ServiceProvider serviceProvider,
           string awsAccessKey, string awsSecretAccessKey, string customerOptimizationQueueName, long optimizationSessionId, SiteType siteType, string additionalData = null)
        {
            //var serviceProvider = altaWrxDb.ServiceProviders.Include(sp => sp.Integration).FirstOrDefault(sp => sp.id == serviceProvider.id);
            var serviceProviderId = serviceProvider.id;
            if (serviceProvider == null)
            {
                return "Service provider not found";
            }

            var portalType = (PortalTypes)serviceProvider.Integration.PortalTypeId;
            var dateHelper = new DateHelper(altaWrxDb, serviceProviderId, billPeriod.BillYear, billPeriod.BillMonth);
            var billingPeriodEnd = dateHelper.BillingPeriodEnd(billPeriod);
            var customers = GetOptimizationCustomers(tenantId, serviceProviderId, billPeriod.id, portalType, null, null, null);
            var amopCustomers = GetOptimizationAMOPCustomers(tenantId, serviceProviderId, billPeriod.id, portalType);
            if (!customers.Any() && !amopCustomers.Any())
            {
                return "No customers found with eligible SIMs";
            }

            try
            {
                await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 30, "", additionalData);
                var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(awsAccessKey, awsSecretAccessKey);
                using (var client = new AmazonSQSClient(awsCredentials, Amazon.RegionEndpoint.USEast1))
                {
                    var queueList = client.ListQueues(customerOptimizationQueueName);
                    if (queueList.HttpStatusCode != System.Net.HttpStatusCode.OK || queueList.QueueUrls == null || queueList.QueueUrls.Count <= 0)
                    {
                        return "Error Queuing Customer Optimization: Queue not found";
                    }

                    var queueUrl = queueList.QueueUrls[0];
                    var tasks = new List<Task<string>>();

                    if (siteType == SiteType.Rev)
                    {
                        for (var i = 0; i < customers.Count; i++)
                        {
                            var customer = customers[i];
                            var delaySeconds = Math.Min(i * 2, 900); // Prevent DoS'ing the database with a flood of downstream connections
                            var isLastInstance = false;
                            if (i == customers.Count - 1)
                                isLastInstance = true;
                            tasks.Add(EnqueueCustomerOptimizationSqsAsync(client, queueUrl, tenantId, customer.AmopCustomerId.ToString(),
                                customer.RevIntegrationAuthId.GetValueOrDefault(), serviceProviderId, billPeriod.BillYear, billPeriod.BillMonth,
                                billPeriod.id, optimizationSessionId, null, isLastInstance, siteType, delaySeconds));

                            // insert to optimization customer processing table
                            var optCus = new OptimizationCustomerProcessing()
                            {
                                CustomerId = customer.RevCustomerId,
                                StartTime = DateTime.UtcNow,
                                IsProcessed = false,
                                CustomerName = $"{customer.RevCustomerName} ({customer.RevCustomerId})",
                                ServiceProviderId = serviceProvider.id,
                                SessionId = optimizationSessionId
                            };
                            altaWrxDb.OptimizationCustomerProcessings.Add(optCus);
                        }
                        altaWrxDb.SaveChanges();
                    }

                    if (siteType == SiteType.AMOP)
                    {
                        for (var i = 0; i < amopCustomers.Count; i++)
                        {
                            var amopCustomer = amopCustomers[i];
                            var delaySeconds = Math.Min(i * 2, 900); // Prevent DoS'ing the database with a flood of downstream connections
                            var isLastInstance = false;
                            if (i == amopCustomers.Count - 1)
                                isLastInstance = true;
                            tasks.Add(EnqueueCustomerOptimizationSqsAsync(client, queueUrl, tenantId, null,
                                null, serviceProviderId, billPeriod.BillYear, billPeriod.BillMonth,
                                billPeriod.id, optimizationSessionId, amopCustomer.SiteId, isLastInstance, siteType, delaySeconds));

                            // insert to optimization customer processing table
                            var optCus = new OptimizationCustomerProcessing()
                            {
                                AMOPCustomerId = amopCustomer.SiteId,
                                AMOPCustomerName = amopCustomer.SiteName,
                                StartTime = DateTime.UtcNow,
                                IsProcessed = false,
                                ServiceProviderId = serviceProvider.id,
                                SessionId = optimizationSessionId
                            };
                            altaWrxDb.OptimizationCustomerProcessings.Add(optCus);
                        }
                        altaWrxDb.SaveChanges();
                    }
                    await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 40, "", additionalData);
                    var results = await Task.WhenAll(tasks);
                    var errorMessage = string.Join(Environment.NewLine, results.Where(result => !string.IsNullOrWhiteSpace(result)));
                    SendAllCustomersOptimizationSummaryEmail(serviceProviderId, billingPeriodEnd, customers, errorMessage);
                    return errorMessage;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error Queuing Customer Optimization for All Customers", ex);
                return "Error Queuing Customer Optimization: Exception occured";
            }
        }

        private async Task<string> EnqueueCrossAllCustomersOptimizationAsync(CustomerBillingPeriod billPeriod, int tenantId, string serviceProviderIds,
            string awsAccessKey, string awsSecretAccessKey, string customerOptimizationQueueName, long optimizationSessionId, SiteType siteType, List<DateTime> endDateList, string additionalData = null)
        {
            var portalType = PortalTypes.CrossProvider;
            var billingPeriodEnd = endDateList[endDateList.Count - 1];
            var billingPeriodStart = endDateList[0].AddMonths(-1);
            var allCustomer = GetCrossCustomerOptimization(billPeriod, tenantId, serviceProviderIds, endDateList, billingPeriodStart, billingPeriodEnd);
            var revCustomers = allCustomer.Where(x => x.RevCustomerId != null && x.RevIntegrationAuthId != null).ToList();
            var amopCustomers = allCustomer.Where(x => x.RevCustomerId == null || x.RevIntegrationAuthId == null).ToList();
            if (!revCustomers.Any() && !amopCustomers.Any())
            {
                return "No customers found with eligible SIMs";
            }

            try
            {
                await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 30, "", additionalData);
                var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(awsAccessKey, awsSecretAccessKey);
                using (var client = new AmazonSQSClient(awsCredentials, Amazon.RegionEndpoint.USEast1))
                {
                    var queueList = client.ListQueues(customerOptimizationQueueName);
                    if (queueList.HttpStatusCode != System.Net.HttpStatusCode.OK || queueList.QueueUrls == null || queueList.QueueUrls.Count <= 0)
                    {
                        return "Error Queuing Customer Optimization: Queue not found";
                    }

                    var queueUrl = queueList.QueueUrls[0];
                    var tasks = new List<Task<string>>();

                    if (siteType == SiteType.Rev)
                    {
                        for (var i = 0; i < revCustomers.Count; i++)
                        {
                            var customer = revCustomers[i];
                            var delaySeconds = Math.Min(i * 2, 900);
                            var isLastInstance = false;
                            if (i == revCustomers.Count - 1)
                                isLastInstance = true;
                            tasks.Add(EnqueueCrossProviderCustomerOptimizationSqsAsync(client, queueUrl, tenantId, customer.RevCustomerId, customer.RevIntegrationAuthId, serviceProviderIds, billPeriod.BillYear, billPeriod.BillMonth, billPeriod.id, optimizationSessionId, customer.SiteId, isLastInstance, siteType));

                            var optCus = new OptimizationCustomerProcessing()
                            {
                                CustomerId = customer.RevCustomerId,
                                StartTime = DateTime.UtcNow,
                                IsProcessed = false,
                                CustomerName = $"{customer.RevCustomerName} ({customer.RevCustomerId})",
                                ServiceProviderId = null,
                                SessionId = optimizationSessionId
                            };
                            altaWrxDb.OptimizationCustomerProcessings.Add(optCus);
                        }
                        altaWrxDb.SaveChanges();
                    }

                    if (siteType == SiteType.AMOP)
                    {
                        for (var i = 0; i < amopCustomers.Count; i++)
                        {
                            var amopCustomer = amopCustomers[i];
                            var delaySeconds = Math.Min(i * 2, 900);
                            var isLastInstance = false;
                            if (i == amopCustomers.Count - 1)
                                isLastInstance = true;
                            tasks.Add(EnqueueCrossProviderCustomerOptimizationSqsAsync(client, queueUrl, tenantId, null, null, serviceProviderIds, billPeriod.BillYear, billPeriod.BillMonth, billPeriod.id, optimizationSessionId, amopCustomer.SiteId, isLastInstance, siteType));

                            var optCus = new OptimizationCustomerProcessing()
                            {
                                AMOPCustomerId = amopCustomer.SiteId,
                                AMOPCustomerName = amopCustomer.SiteName,
                                StartTime = DateTime.UtcNow,
                                IsProcessed = false,
                                ServiceProviderId = null,
                                SessionId = optimizationSessionId
                            };
                            altaWrxDb.OptimizationCustomerProcessings.Add(optCus);
                        }
                        altaWrxDb.SaveChanges();
                    }
                    await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 40, "", additionalData);
                    var results = await Task.WhenAll(tasks);
                    var errorMessage = string.Join(Environment.NewLine, results.Where(result => !string.IsNullOrWhiteSpace(result)));
                    SendAllCustomersCrossProviderOptimizationSummaryEmail(serviceProviderIds, billingPeriodEnd, revCustomers, errorMessage);
                    return errorMessage;
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error Queuing Customer Optimization for All Customers", ex);
                return "Error Queuing Customer Optimization: Exception occured";
            }


        }
        private async Task<string> EnqueueCustomerOptimizationSqsAsync(BillingPeriod billPeriod, CustomerBillingPeriod customerBillPeriod, int tenantId, int siteId,
            int? integrationAuthenticationId, int? serviceProviderId, string serviceProviderIds, string awsAccessKey, string awsSecretAccessKey,
            string m2mCustomerOptimizationQueueName, string mobilityCustomerOptimizationQueueName, string crossProviderCustomerOptimizationQueueName, string revCustId, long optimizationSessionId, SiteType siteType, string additionalData = null)
        {
            try
            {
                var m2mServiceProviders = altaWrxDb.usp_OptimizationServiceProvidersByCustomer(siteId.ToString(), (int)PortalTypes.M2M)?.ToList();
                var mobilityServiceProviders = altaWrxDb.usp_OptimizationServiceProvidersByCustomer(siteId.ToString(), (int)PortalTypes.Mobility)?.ToList();
                await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 30, "", additionalData);
                string result = string.Empty;
                if (m2mServiceProviders != null && m2mServiceProviders.Any() && (serviceProviderId == null || m2mServiceProviders.Contains(serviceProviderId.Value)) && !permissionManager.OptimizationSettings.OptIntoCrossProviderCustomerOptimization)
                {
                    result = await EnqueueCustomerOptimizationSqsAsync(billPeriod, tenantId, revCustId, integrationAuthenticationId,
                        serviceProviderId, awsAccessKey, awsSecretAccessKey, m2mCustomerOptimizationQueueName, optimizationSessionId, siteId, siteType);
                }

                if (mobilityServiceProviders != null && mobilityServiceProviders.Any() && (serviceProviderId == null || mobilityServiceProviders.Contains(serviceProviderId.Value)) && !permissionManager.OptimizationSettings.OptIntoCrossProviderCustomerOptimization)
                {
                    mobilityServiceProviders = serviceProviderId != null ? new List<int?> { serviceProviderId } : mobilityServiceProviders;
                    result = await EnqueueCustomerOptimizationSqsAsync(billPeriod, tenantId, revCustId, integrationAuthenticationId,
                        mobilityServiceProviders, awsAccessKey, awsSecretAccessKey, mobilityCustomerOptimizationQueueName, optimizationSessionId, siteId, siteType);
                }

                if (!string.IsNullOrWhiteSpace(serviceProviderIds) && permissionManager.OptimizationSettings.OptIntoCrossProviderCustomerOptimization)
                {
                    result = await EnqueueCrossProviderCustomerOptimizationSqsAsync(customerBillPeriod, tenantId, revCustId, integrationAuthenticationId,
                        serviceProviderIds, awsAccessKey, awsSecretAccessKey, crossProviderCustomerOptimizationQueueName, optimizationSessionId, siteId, siteType);
                }
                await SendResponseToAMOP20("Progress", optimizationSessionId.ToString(), optimizationSessionGuid, 0, null, 40, "", additionalData);
                return result;
            }
            catch (Exception ex)
            {
                Log.Error($"Error Queuing Optimization for {siteId}", ex);
                return "Error Queuing Optimization: Exception occured";
            }
        }
        private async Task<string> EnqueueCustomerOptimizationSqsAsync(BillingPeriod billPeriod, int tenantId, string revCustId,
            int? integrationAuthenticationId, int? serviceProviderId, string awsAccessKey, string awsSecretAccessKey,
            string customerOptimizationQueueName, long optimizationSessionId, int? AMOPCustomerId, SiteType siteType)
        {
            try
            {
                var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(awsAccessKey, awsSecretAccessKey);
                using (var client = new AmazonSQSClient(awsCredentials, Amazon.RegionEndpoint.USEast1))
                {
                    var queueList = client.ListQueues(customerOptimizationQueueName);
                    if (queueList.HttpStatusCode != System.Net.HttpStatusCode.OK || queueList.QueueUrls == null || queueList.QueueUrls.Count <= 0)
                    {
                        return "Error Queuing Customer Optimization: Queue not found";
                    }

                    var queueUrl = queueList.QueueUrls[0];
                    return await EnqueueCustomerOptimizationSqsAsync(client, queueUrl, tenantId, revCustId, integrationAuthenticationId,
                        serviceProviderId, billPeriod.BillYear, billPeriod.BillMonth, billPeriod.id, optimizationSessionId, AMOPCustomerId, true, siteType);

                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error Queuing Customer Optimization for {revCustId}", ex);
                return "Error Queuing Customer Optimization: Exception occured";
            }
        }
        private async Task<string> EnqueueCustomerOptimizationSqsAsync(BillingPeriod billPeriod, int tenantId, string revCustId,
            int? integrationAuthenticationId, List<int?> serviceProviders, string awsAccessKey, string awsSecretAccessKey,
            string customerOptimizationQueueName, long optimizationSessionId, int? AMOPCustomerId, SiteType siteType)
        {
            StringBuilder sb = new StringBuilder();
            foreach (var providerId in serviceProviders)
            {
                var result = await EnqueueCustomerOptimizationSqsAsync(billPeriod, tenantId, revCustId, integrationAuthenticationId, providerId,
                    awsAccessKey, awsSecretAccessKey, customerOptimizationQueueName, optimizationSessionId, AMOPCustomerId, siteType);
                if (!string.IsNullOrWhiteSpace(result))
                {
                    sb.AppendLine(result);
                }
            }

            return sb.ToString();
        }

        private static async Task<string> EnqueueCustomerOptimizationSqsAsync(IAmazonSQS sqsClient, string queueUrl, int tenantId, string revCustId,
           int? integrationAuthenticationId, int? serviceProviderId, int billYear, int billMonth, int billPeriodId, long optimizationSessionId, int? AMOPCustomerId, bool isLastInstance,
           SiteType siteType = SiteType.Rev, int delaySeconds = 0)
        {
            var requestMsgBody = $"Customer to optimize is {revCustId} for Billing Period {billYear}/{billMonth}";

            var messageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                { "TenantId", new MessageAttributeValue { DataType = "String", StringValue = tenantId.ToString() } },
                { "BillPeriodId", new MessageAttributeValue { DataType = "String", StringValue = billPeriodId.ToString() } },
                { "OptimizationSessionId", new MessageAttributeValue { DataType = "String", StringValue = optimizationSessionId.ToString() } },
                { "CustomerType", new MessageAttributeValue { DataType = "String", StringValue = ((int)siteType).ToString() } },
                { "IsLastInstance", new MessageAttributeValue { DataType = "String", StringValue = isLastInstance.ToString() } }
        };
            if (siteType == SiteType.AMOP && AMOPCustomerId.HasValue)
            {
                messageAttributes.Add("AMOPCustomerId", new MessageAttributeValue { DataType = "String", StringValue = AMOPCustomerId.ToString() });
            }

            if (siteType == SiteType.Rev)
            {
                messageAttributes.Add("IntegrationAuthenticationId", new MessageAttributeValue { DataType = "String", StringValue = integrationAuthenticationId.ToString() });
                messageAttributes.Add("CustomerId", new MessageAttributeValue { DataType = "String", StringValue = revCustId });
            }
            // include service provider id, if specified
            if (serviceProviderId != null)
            {
                messageAttributes.Add("ServiceProviderId",
                    new MessageAttributeValue { DataType = "String", StringValue = serviceProviderId.Value.ToString() });
            }

            var request = new SendMessageRequest
            {
                MessageAttributes = messageAttributes,
                MessageBody = requestMsgBody,
                QueueUrl = queueUrl,
                DelaySeconds = delaySeconds
            };

            var response = await sqsClient.SendMessageAsync(request);
            return response.HttpStatusCode.IsSuccessStatusCode()
                ? string.Empty
                : $"Error Queuing Customer Optimization: {response.HttpStatusCode:D} {response.HttpStatusCode:G}";
        }
        private IList<IOptimizationCustomersGetResult> GetOptimizationCustomers(int? tenantId, int? serviceProviderId, int? billPeriodId, PortalTypes portalType, DateTime? customerStartDate, DateTime? customerEndDate, string crossServiceProviderIds)
        {
            switch (portalType)
            {
                case PortalTypes.M2M:
                    return altaWrxDb.usp_OptimizationCustomersGet(tenantId, serviceProviderId, billPeriodId).Cast<IOptimizationCustomersGetResult>().ToList();
                case PortalTypes.Mobility:
                    return altaWrxDb.usp_Optimization_Mobility_CustomersGet(tenantId, serviceProviderId, billPeriodId).Cast<IOptimizationCustomersGetResult>().ToList();
                case PortalTypes.CrossProvider:
                    return altaWrxDb.usp_CrossProviderOptimizationCustomersGet(tenantId, crossServiceProviderIds, billPeriodId, customerStartDate, customerEndDate).Cast<IOptimizationCustomersGetResult>().ToList();
                default:
                    return new List<IOptimizationCustomersGetResult>();
            }
        }
        private IList<IOptimizationCustomersGetResult> GetCrossCustomerOptimization(CustomerBillingPeriod billPeriod, int tenantId, string serviceProviderIds, List<DateTime> endDateList, DateTime billingPeriodStart, DateTime billingPeriodEnd)
        {
            var allCustomer = GetOptimizationCustomers(tenantId, null, billPeriod.id, PortalTypes.CrossProvider, billingPeriodStart, billingPeriodEnd, serviceProviderIds);
            var siteIdList = altaWrxDb.Sites.Include(s => s.RevCustomer)
                    .Where(x => x.RevCustomer.id != null
                        && x.TenantId == tenantId
                        && x.RevCustomer.IsActive
                        && !x.RevCustomer.IsDeleted
                        && x.IsActive
                        && !x.IsDeleted
                        && endDateList.Any(date => date.Day == x.CustomerBillPeriodEndDay))
                    .Select(x => x.id)
                    .ToList();
            return allCustomer.Where(x => siteIdList.Any(siteId => siteId == x.SiteId)).ToList();
        }

        private IList<IOptimizationAMOPCustomersGetResult> GetOptimizationAMOPCustomers(int? tenantId, int? serviceProviderId, int? billPeriodId, PortalTypes portalType)
        {
            switch (portalType)
            {
                case PortalTypes.M2M:
                    return altaWrxDb.usp_Optimization_AMOPCustomersGet(tenantId, serviceProviderId, billPeriodId).Cast<IOptimizationAMOPCustomersGetResult>().ToList();
                case PortalTypes.Mobility:
                    return altaWrxDb.usp_Optimization_Mobility_AMOPCustomersGet(tenantId, serviceProviderId, billPeriodId).Cast<IOptimizationAMOPCustomersGetResult>().ToList();
                default:
                    return new List<IOptimizationAMOPCustomersGetResult>();
            }
        }
        private void SendAllCustomersOptimizationSummaryEmail(int serviceProviderId, DateTime billingPeriodEnd, IEnumerable<IOptimizationCustomersGetResult> customers, string errorMessage)
        {
            var settings = altaWrxDb.OptimizationSettings.Where(setting => !setting.IsDeleted).ToList();
            var toEmails = settings.FirstOrDefault(setting => setting.SettingKey == "CustomerOptimizationToEmailAddresses")
                ?.SettingValue?.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var fromEmail = settings.FirstOrDefault(setting => setting.SettingKey == "CustomerOptimizationFromEmailAddress")?.SettingValue;
            if (toEmails == null || toEmails.Length < 1 || fromEmail == null)
            {
                Log.Error("Error sending 'All Customers' summary email - missing configuration");
                return;
            }

            var serviceProvider = altaWrxDb.ServiceProviders.Find(serviceProviderId);
            var serviceProviderName = serviceProvider?.DisplayName ?? string.Empty;

            var emailClient = new SESWrapper
            {
                From = fromEmail,
                Subject = BuildAllCustomersSummaryEmailSubject(serviceProviderName, billingPeriodEnd),
                Body = BuildAllCustomersSummaryEmailBody(serviceProviderName, billingPeriodEnd, customers, errorMessage)
            };
            foreach (var toEmail in toEmails)
            {
                emailClient.AddRecipient(toEmail);
            }

            if (user != null)
            {
                emailClient.AddCCRecipient(user.Email);
            }

            emailClient.SendEmail();
        }
        private string M2MCustomerOptimizationQueueFromCustomObjects(IList<CustomObject> customObjectDbList)
        {
            return ValueFromCustomObjects(customObjectDbList, M2M_CUSTOMER_OPTIMIZATION_QUEUE_NAME);
        }

        private string MobilityCustomerOptimizationQueueFromCustomObjects(IList<CustomObject> customObjectDbList)
        {
            return ValueFromCustomObjects(customObjectDbList, MOBILITY_CUSTOMER_OPTIMIZATION_QUEUE_NAME);
        }

        private string CrossProviderCustomerOptimizationQueueFromCustomObjects(IList<CustomObject> customObjectDbList)
        {
            return ValueFromCustomObjects(customObjectDbList, CROSS_PROVIDER_CUSTOMER_OPTIMIZATION_QUEUE_NAME);
        }
        private async Task<string> EnqueueCrossProviderCustomerOptimizationSqsAsync(CustomerBillingPeriod customerBillingPeriod, int tenantId, string revCustId,
            int? integrationAuthenticationId, string serviceProviderIds, string awsAccessKey, string awsSecretAccessKey,
            string customerOptimizationQueueName, long optimizationSessionId, int? AMOPCustomerId, SiteType siteType)
        {
            try
            {
                var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(awsAccessKey, awsSecretAccessKey);
                using (var client = new AmazonSQSClient(awsCredentials, Amazon.RegionEndpoint.USEast1))
                {
                    var queueList = client.ListQueues(customerOptimizationQueueName);
                    if (queueList.HttpStatusCode != System.Net.HttpStatusCode.OK || queueList.QueueUrls == null || queueList.QueueUrls.Count <= 0)
                    {
                        return LogCommonStrings.ERROR_QUEUING_CUSTOMER_OPTIMIZATION_QUEUE_NOT_FOUND;
                    }

                    var queueUrl = queueList.QueueUrls[0];
                    return await EnqueueCrossProviderCustomerOptimizationSqsAsync(client, queueUrl, tenantId, revCustId, integrationAuthenticationId,
                        serviceProviderIds, customerBillingPeriod.BillYear, customerBillingPeriod.BillMonth, customerBillingPeriod.id, optimizationSessionId, AMOPCustomerId, true, siteType);

                }
            }
            catch (Exception ex)
            {
                Log.Error(string.Format(LogCommonStrings.ERROR_QUEUING_CUSTOMER_OPTIMIZATION_FOR_CUSTOMER, revCustId), ex);
                return LogCommonStrings.ERROR_QUEUING_CUSTOMER_OPTIMIZATION_EXCEPTION_OCCURED;
            }
        }
        private async Task<string> EnqueueCrossProviderCustomerOptimizationSqsAsync(IAmazonSQS sqsClient, string queueUrl, int tenantId, string revCustId,
           int? integrationAuthenticationId, string serviceProviderIds, int billYear, int billMonth, int billPeriodId, long optimizationSessionId, int? AMOPCustomerId, bool isLastInstance,
           SiteType siteType = SiteType.Rev, int delaySeconds = 0)
        {
            var requestMsgBody = $"Customer to optimize is {revCustId} for Billing Period {billYear}/{billMonth}";

            var messageAttributes = new Dictionary<string, MessageAttributeValue>
            {
                { SQSMessageKeyConstant.TENANT_ID, new MessageAttributeValue { DataType = nameof(String), StringValue = tenantId.ToString() } },
                { SQSMessageKeyConstant.OPTIMIZATION_SESSION_ID, new MessageAttributeValue { DataType = nameof(String), StringValue = optimizationSessionId.ToString() } },
                { SQSMessageKeyConstant.CUSTOMER_TYPE, new MessageAttributeValue { DataType = nameof(String), StringValue = ((int)siteType).ToString() } },
                { SQSMessageKeyConstant.IS_LAST_INSTANCE, new MessageAttributeValue { DataType = nameof(String), StringValue = isLastInstance.ToString() } },
                { SQSMessageKeyConstant.PORTAL_TYPE_ID, new MessageAttributeValue { DataType = nameof(String), StringValue = ((int)PortalTypes.CrossProvider).ToString() } },
            };

            if (permissionManager.OptimizationSettings.OptIntoCrossProviderCustomerOptimization)
            {
                messageAttributes.Add(SQSMessageKeyConstant.SERVICE_PROVIDER_IDS, new MessageAttributeValue { DataType = nameof(String), StringValue = serviceProviderIds.ToString() });
                messageAttributes.Add(SQSMessageKeyConstant.AMOP_CUSTOMER_ID, new MessageAttributeValue { DataType = nameof(String), StringValue = AMOPCustomerId.ToString() });
                messageAttributes.Add(SQSMessageKeyConstant.CUSTOMER_BILLING_PERIOD_ID, new MessageAttributeValue { DataType = nameof(String), StringValue = billPeriodId.ToString() });
            }
            else
            {
                messageAttributes.Add(SQSMessageKeyConstant.BILL_PERIOD_ID, new MessageAttributeValue { DataType = nameof(String), StringValue = billPeriodId.ToString() });
                if (siteType == SiteType.Rev)
                {
                    messageAttributes.Add(SQSMessageKeyConstant.INTEGRATION_AUTHENTICATION_ID, new MessageAttributeValue { DataType = nameof(String), StringValue = integrationAuthenticationId.ToString() });
                    messageAttributes.Add(SQSMessageKeyConstant.CUSTOMER_ID, new MessageAttributeValue { DataType = nameof(String), StringValue = revCustId });
                }
            }

            var request = new SendMessageRequest
            {
                MessageAttributes = messageAttributes,
                MessageBody = requestMsgBody,
                QueueUrl = queueUrl,
                DelaySeconds = delaySeconds
            };

            var response = await sqsClient.SendMessageAsync(request);
            return response.HttpStatusCode.IsSuccessStatusCode()
                ? string.Empty
                : $"{LogCommonStrings.ERROR_QUEUING_CUSTOMER_OPTIMIZATION}: {response.HttpStatusCode:D} {response.HttpStatusCode:G}";
        }
        private void SendAllCustomersCrossProviderOptimizationSummaryEmail(string serviceProviderIds, DateTime billingPeriodEnd, IEnumerable<IOptimizationCustomersGetResult> customers, string errorMessage)
        {
            var settings = altaWrxDb.OptimizationSettings.Where(setting => !setting.IsDeleted).ToList();
            var toEmails = settings.FirstOrDefault(setting => setting.SettingKey == "CustomerOptimizationToEmailAddresses")
                ?.SettingValue?.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var fromEmail = settings.FirstOrDefault(setting => setting.SettingKey == "CustomerOptimizationFromEmailAddress")?.SettingValue;
            if (toEmails == null || toEmails.Length < 1 || fromEmail == null)
            {
                Log.Error("Error sending 'All Customers' summary email - missing configuration");
                return;
            }

            var serviceProviderIdList = serviceProviderIds.Split(',');

            var serviceProviderList = altaWrxDb.ServiceProviders.Where(x => serviceProviderIdList.Any(s => s == x.id.ToString()) && x.DisplayName != null).Select(x => x.DisplayName).ToList();
            var serviceProviderNameList = string.Join(",", serviceProviderList);

            var emailClient = new SESWrapper
            {
                From = fromEmail,
                Subject = BuildAllCustomersSummaryEmailSubject(serviceProviderNameList, billingPeriodEnd),
                Body = BuildAllCustomersSummaryEmailBody(serviceProviderNameList, billingPeriodEnd, customers, errorMessage)
            };
            foreach (var toEmail in toEmails)
            {
                emailClient.AddRecipient(toEmail);
            }

            if (user != null)
            {
                emailClient.AddCCRecipient(user.Email);
            }

            emailClient.SendEmail();
        }

        private static string BuildAllCustomersSummaryEmailSubject(string serviceProviderName, DateTime billingPeriodEnd)
        {
            var env = ConfigurationManager.AppSettings["Published_Environment"];
            var subject = $"{serviceProviderName} Optimization Summary - All Customers";
            return env == "Production" ? subject : subject + $" ({env})";
        }

        private static string BuildAllCustomersSummaryEmailBody(string serviceProviderName, DateTime billingPeriodEnd, IEnumerable<IOptimizationCustomersGetResult> customers, string errorMessage)
        {
            var stringBuilder = new StringBuilder($@"
                <html>
                <head>
                <style>
                body {{
                    background-color: #fff;
                    font-family: ""Lato"", ""Helvetica Neue"", Helvetica, Arial, sans-serif;
                    z-index: 0;
                    position: relative;
                    top: 0;
                    left: 0;
                    top: 0;
                    bottom: 0;
                }}

                tr {{
                    text-align: left;
                }}

                th,td {{
                    padding-right: 10px;
                }}

                </style>
                </head>
                <h1>{serviceProviderName} Optimization Summary - All Customers</h1>
                <h2>For billing period ending {billingPeriodEnd:g}</h2>
                <table>
                <tr><th>Service Provider</th><th>Billing Period End</th><th>Customer Name</th><th>Rev Account #</th><th>Eligible Device Count</th></tr>");

            foreach (var customer in customers)
            {
                stringBuilder.Append(
                    $"<tr><td>{serviceProviderName}</td><td>{billingPeriodEnd:g}</td><td>{customer.RevCustomerName}</td><td>{customer.RevCustomerId}</td><td>{customer.SimCount ?? 0}</td></tr>");
            }

            stringBuilder.Append("</table>");

            if (!string.IsNullOrWhiteSpace(errorMessage))
            {
                stringBuilder.Append($"<h2>Errors</h2><p>{errorMessage.Replace(Environment.NewLine, "<br>")}</p>");
            }

            stringBuilder.Append("</html>");
            return stringBuilder.ToString();
        }

        private string CarrierOptimizationQueueFromCustomObjects(IList<CustomObject> customObjectDbList)
        {
            return ValueFromCustomObjects(customObjectDbList, CARRIER_OPTIMIZATION_QUEUE_NAME);
        }
        #endregion

        #region Create and Upload Button
        [System.Web.Http.HttpPost]
        [System.Web.Http.ActionName("Create-Confirm-Session")]
        public async Task<string> CreateConfirmSession([System.Web.Http.FromBody] CustomerUploadRequestDto request)
        {
            ValidateRequest();
            ValidateHeader();
            if (permissionManager.UserCannotAccess(session, ModuleEnum.CustomerCharge))
            {
                return "Access denied";
            }
            var errorMessage = string.Empty;
            var hasErrorWhenPushingCharge = false;
            var hasErrorWhenPushingUsage = false;
            var isPushingForSingleCustomer = false;
            long chargeInstanceId = 0;
            var isCrossProviderCustomerOptimization = false;
            if (permissionManager.OptimizationSettings != null && permissionManager.OptimizationSettings.OptIntoCrossProviderCustomerOptimization)
            {
                var optimizationSession = altaWrxDb.OptimizationSessions.Where(x => x.SessionId.Equals(request.SessionId)).FirstOrDefault();
                if (optimizationSession == null)
                {
                    Log.Info($"{errorMessage} Session not found.");
                    errorMessage = $"{errorMessage}\n Session not found.";
                    hasErrorWhenPushingCharge = true;
                }
                else if (!string.IsNullOrWhiteSpace(optimizationSession.ServiceProviderIds))
                {
                    isCrossProviderCustomerOptimization = true;
                }
            }

            if (string.IsNullOrWhiteSpace(request.pushType) || (request.pushType != CommonConstants.CHARGES && request.pushType != CommonConstants.USAGE && request.pushType != CommonConstants.BOTH))
            {
                Log.Info($"{LogCommonStrings.UNABLE_TO_PUSH_WITHOUT_PUSH_TYPE}");
                errorMessage = LogCommonStrings.UNABLE_TO_PUSH_WITHOUT_PUSH_TYPE;
                hasErrorWhenPushingCharge = true;
                hasErrorWhenPushingUsage = true;
            }

            var customObjectDbList = permissionManager.CustomFields;
            if (request.pushType == CommonConstants.CHARGES || request.pushType == CommonConstants.BOTH)
            {
                // get tenant custom fields
                var awsAccessKey = amopBaseController.AwsAccessKeyFromCustomObjects(customObjectDbList);
                var awsSecretAccessKey = amopBaseController.AwsSecretAccessKeyFromCustomObjects(customObjectDbList);
                var createCustomerChargeQueueName = CreateCustomerChargeQueueFromCustomObjects(customObjectDbList);
                var createCDRCustomerChargeQueueName = amopBaseController.CreateCDRCustomerChargeQueueFromCustomObjects(customObjectDbList);
                var pushCustomerChargeType = amopBaseController.PushCustomerChargeTypeFromCustomObjects(customObjectDbList);

                // validate custom fields
                if (string.IsNullOrWhiteSpace(awsAccessKey) || string.IsNullOrWhiteSpace(awsSecretAccessKey) ||
                    string.IsNullOrWhiteSpace(createCustomerChargeQueueName))
                {
                    Log.Info($"{LogCommonStrings.UNABLE_TO_PUSH_CHARGES_AWS_IS_NOT_SETUP}");
                    errorMessage = $"{errorMessage}\n{LogCommonStrings.UNABLE_TO_PUSH_CHARGES_AWS_IS_NOT_SETUP}";
                    hasErrorWhenPushingCharge = true;
                }

                // validate selected instances
                var instances = request.selectedInstances?.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (instances == null || instances.Length == 0)
                {
                    Log.Info($"{LogCommonStrings.UNABLE_TO_PUSH_CHARGES_WITH_NO_INSTANCES}");
                    errorMessage = $"{errorMessage}\n{LogCommonStrings.UNABLE_TO_PUSH_CHARGES_WITH_NO_INSTANCES}";
                    hasErrorWhenPushingCharge = true;
                }

                long[] instanceIds;
                try
                {
                    if (!hasErrorWhenPushingCharge)
                    {
                        instanceIds = instances.Select(x => long.Parse(x.Replace("\"", ""))).ToArray();
                        if (string.Equals(pushCustomerChargeType, PushCustomerChargeType.CDR, StringComparison.OrdinalIgnoreCase))
                        {
                            var enqueueChargesErrorMessage = await InitializeUploadCustomerChargeCDRs(request.SessionId, awsAccessKey, awsSecretAccessKey, createCDRCustomerChargeQueueName, instanceIds, isCrossProviderCustomerOptimization);
                            if (!string.IsNullOrWhiteSpace(enqueueChargesErrorMessage))
                            {
                                Log.Info($"{errorMessage} {enqueueChargesErrorMessage}");
                                errorMessage = $"{errorMessage}\n{enqueueChargesErrorMessage}";
                                hasErrorWhenPushingCharge = true;
                            }
                        }
                        else
                        {
                            foreach (var item in instanceIds.Select((instanceId, index) => new { instanceId, index }))
                            {
                                var isLastInstanceId = 0;
                                if (item.index == instanceIds.Length - 1)
                                {
                                    isLastInstanceId = 1;
                                }
                                var tenantId = permissionManager.Tenant.id;
                                var enqueueCreateCustomerChargeErrorMessage = EnqueueCreateCustomerChargesWithSesstionSqs(item.instanceId, awsAccessKey, awsSecretAccessKey, createCustomerChargeQueueName, altaWrxDb, tenantId, 1, isLastInstanceId, string.Join(",", instanceIds));
                                if (!string.IsNullOrWhiteSpace(enqueueCreateCustomerChargeErrorMessage))
                                {
                                    Log.Info($"{errorMessage} {enqueueCreateCustomerChargeErrorMessage}");
                                    errorMessage = $"{errorMessage}\n{enqueueCreateCustomerChargeErrorMessage}";
                                    hasErrorWhenPushingCharge = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (InvalidCastException)
                {
                    Log.Info($"{LogCommonStrings.UNABLE_TO_PUSH_CHARGES_WITH_INVALID_INSTANCES}");
                    errorMessage = $"{errorMessage}\n{LogCommonStrings.UNABLE_TO_PUSH_CHARGES_WITH_INVALID_INSTANCES}";
                    hasErrorWhenPushingCharge = true;
                }
            }

            if (request.pushType == CommonConstants.USAGE || request.pushType == CommonConstants.BOTH)
            {
                // get tenant custom fields for Rev.io FTP
                var revFTPHost = amopBaseController.RevFTPHostFromCustomObjects(customObjectDbList);
                var revFTPUsername = amopBaseController.RevFTPUsernameFromCustomObjects(customObjectDbList);
                var revFTPPassword = amopBaseController.RevFTPPasswordFromCustomObjects(customObjectDbList);
                var revFTPPath = amopBaseController.RevFTPPathFromCustomObjects(customObjectDbList);

                // validate custom fields for Rev.io FTP
                if (string.IsNullOrWhiteSpace(revFTPHost) || string.IsNullOrWhiteSpace(revFTPUsername) ||
                    string.IsNullOrWhiteSpace(revFTPPassword) || string.IsNullOrWhiteSpace(revFTPPath))
                {
                    Log.Info($"{LogCommonStrings.UNABLE_TO_PUSH_USAGE_REV_FTP_IS_NOT_SETUP}");
                    errorMessage = $"{errorMessage}\n{LogCommonStrings.UNABLE_TO_PUSH_USAGE_REV_FTP_IS_NOT_SETUP}";
                    hasErrorWhenPushingUsage = true;
                }

                // validate usage instances
                var instances = request.selectedUsageInstances?.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (instances == null || instances.Length == 0)
                {
                    Log.Info($"{LogCommonStrings.UNABLE_TO_PUSH_USAGE_WITH_NO_INSTANCES}");
                    errorMessage = $"{errorMessage}\n{LogCommonStrings.UNABLE_TO_PUSH_USAGE_WITH_NO_INSTANCES}";
                    hasErrorWhenPushingUsage = true;
                }
                try
                {
                    if (!hasErrorWhenPushingUsage)
                    {
                        var createSessionCustomerCharge = new CreateSessionCustomerChargeModel(altaWrxDb, request.SessionId, permissionManager.PermissionFilter.GetSiteIdFilter(), permissionManager.Tenant.id, permissionManager, null);
                        var dataToUpload = createSessionCustomerCharge.CreateCustomerUsageInstanceList
                            .Where(x => instances.Contains(x.ICCID))
                            .Select(x => new UploadDeviceUsageByLineModel()
                            {
                                BilledNumber = x.MSISDN,
                                CallDate = createSessionCustomerCharge.BillingPeriodEnd.ToString(CommonConstants.AMOP_DATE_TIME_FORMAT),
                                CarrierRateType = CommonConstants.CARRIER_RATE_TYPE,
                                Kilobytes = (decimal)x.DataUsageMB * 1024,
                            }).ToList();
                        string fileName = $"UsagePush_{FileNameTimestamp()}.csv";
                        StringBuilder stringBuilder = new StringBuilder();
                        PropertyInfo[] properties = typeof(UploadDeviceUsageByLineModel).GetProperties();
                        foreach (PropertyInfo property in properties)
                        {
                            var displayAttribute = property.GetCustomAttribute<DisplayNameAttribute>();
                            if (displayAttribute != null)
                            {
                                stringBuilder.Append(displayAttribute.DisplayName + ",");
                            }
                        }
                        stringBuilder.Remove(stringBuilder.Length - 1, 1).AppendLine();
                        foreach (var usage in dataToUpload)
                        {
                            stringBuilder.Append($"{usage.BilledCountry},{usage.BilledNumber},{usage.CallDate},{usage.OtherCountry},{usage.OtherNumber},{usage.Seconds},{usage.CarrierCode},{usage.CarrierRateType},{usage.Charge},{usage.Kilobytes}");
                            stringBuilder.AppendLine();
                        }
                        byte[] fileByte = Encoding.UTF8.GetBytes(stringBuilder.ToString());
                        RevFTPHelper.SendUsagePushToRevFTP(fileByte, fileName, revFTPHost, revFTPUsername, revFTPPassword, revFTPPath);
                        if (!isCrossProviderCustomerOptimization)
                        {
                            var session = altaWrxDb.vwOptimizationSessions.FirstOrDefault(x => x.SessionId == request.SessionId);
                            var serviceProvider = altaWrxDb.ServiceProviders.FirstOrDefault(sp => sp.id == session.ServiceProviderId);
                            var portalType = (PortalTypes)serviceProvider.Integration.PortalTypeId;
                            var usageByLineIds = createSessionCustomerCharge.CreateCustomerUsageInstanceList.Where(x => instances.Contains(x.ICCID)).Select(x => x.DeviceHistoryId).ToList();
                            if (portalType == PortalTypes.M2M)
                            {
                                var usageByLines = altaWrxDb.DeviceHistories.Where(x => usageByLineIds.Contains(x.DeviceHistoryId)).ToList();
                                foreach (var usageByLine in usageByLines)
                                {
                                    usageByLine.IsPushed = true;
                                    altaWrxDb.Entry(usageByLine).State = EntityState.Modified;
                                }
                            }
                            else
                            {
                                var usageByLines = altaWrxDb.MobilityDeviceHistories.Where(x => usageByLineIds.Contains(x.DeviceHistoryId)).ToList();
                                foreach (var usageByLine in usageByLines)
                                {
                                    usageByLine.IsPushed = true;
                                    altaWrxDb.Entry(usageByLine).State = EntityState.Modified;
                                }
                            }
                            altaWrxDb.SaveChanges();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error($"{LogCommonStrings.ERROR_PUSHING_USAGE} {ex.Message}");
                    errorMessage = $"{errorMessage}\n{LogCommonStrings.ERROR_PUSHING_USAGE}";
                    hasErrorWhenPushingUsage = true;
                }
            }

            var chargeInstances = request.selectedInstances?.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            if (chargeInstances?.Length == 1)
            {
                isPushingForSingleCustomer = true;
                chargeInstanceId = chargeInstances.Select(x => long.Parse(x.Replace("\"", ""))).ToArray()[0];
            }

            if (hasErrorWhenPushingCharge || hasErrorWhenPushingUsage)
            {
                SessionHelper.SetAlert(session, errorMessage);
                SessionHelper.SetAlertType(session, CommonConstants.DANGER);
                if (isPushingForSingleCustomer)
                {
                    if (request.pushType == CommonConstants.USAGE)
                    {
                        return "Create Confirm";
                    }
                    return "Customer Charge Confirm";
                }
                return "Create Confirm Session";
            }

            SessionHelper.SetAlert(session, LogCommonStrings.SUCCESSFULLY_PUSHED_CHARGES_AND_USAGE);
            SessionHelper.SetAlertType(session, CommonConstants.SUCCESS);
            if (isPushingForSingleCustomer)
            {
                if (request.pushType == CommonConstants.USAGE)
                {
                    return "Create Confirm";
                }
                return "Customer Charge Confirm";
            }
            if (request.pushType == CommonConstants.CHARGES || request.pushType == CommonConstants.BOTH)
            {
                return "Customer Charge Session Confirm";
            }
            else
            {
                return "Create Confirm Session";
            }
        }
        private string CreateCustomerChargeQueueFromCustomObjects(IList<CustomObject> customObjectDbList)
        {
            return ValueFromCustomObjects(customObjectDbList, CREATE_CUSTOMER_CHARGE_QUEUE_NAME);
        }
        private static string EnqueueCreateCustomerChargesWithSesstionSqs(long instanceId, string awsAccessKey,
            string awsSecretAccessKey, string createCustomerChargeQueueName, AltaWorxCentral_Entities altaWrxDb, int tenantId, int isMultipleInstanceId = 0, int isLastInstanceId = 0, string instanceIds = "")
        {
            try
            {
                var awsCredentials = new BasicAWSCredentials(awsAccessKey, awsSecretAccessKey);
                var integrationAuthenticationRepository = new IntegrationAuthenticationRepository(altaWrxDb);
                var integrationAuthentication = integrationAuthenticationRepository.GetAuthByIntegrationId(IntegrationEnum.RevIO.AsInt(), tenantId);
                using (var client = new AmazonSQSClient(awsCredentials, RegionEndpoint.USEast1))
                {
                    var queueList = client.ListQueues(createCustomerChargeQueueName);
                    if (queueList.HttpStatusCode == HttpStatusCode.OK && queueList.QueueUrls != null &&
                        queueList.QueueUrls.Count > 0)
                    {
                        var requestMsgBody = $"Instance to work is {instanceId}";
                        var request = new SendMessageRequest
                        {
                            DelaySeconds = isLastInstanceId == 1 ? 90 : 0,
                            MessageAttributes = new Dictionary<string, MessageAttributeValue>
                            {
                                {
                                    "InstanceId", new MessageAttributeValue {DataType = "String", StringValue = instanceId.ToString()}
                                },
                                {
                                    "IsMultipleInstanceId", new MessageAttributeValue {DataType = "String", StringValue = isMultipleInstanceId.ToString()}

                                },
                                {
                                    "IsLastInstanceId", new MessageAttributeValue {DataType = "String", StringValue = isLastInstanceId.ToString()}
                                },
                                {
                                    "InstanceIds", new MessageAttributeValue {DataType = "String", StringValue = instanceIds}
                                },
                                {
                                    "CurrentIntegrationAuthenticationId", new MessageAttributeValue {DataType = "String", StringValue = integrationAuthentication.id.ToString()}
                                }
                            },
                            MessageBody = requestMsgBody,
                            QueueUrl = queueList.QueueUrls[0]
                        };
                        var response = client.SendMessageAsync(request);
                        response.Wait();
                        if (response.Status == TaskStatus.Faulted || response.Status == TaskStatus.Canceled)
                        {
                            return $"Error Queuing Charges: {response.Status}";
                        }

                        // success
                        return string.Empty;
                    }

                    return "Error Queuing Charges: Queue not found";
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error Queuing Charges for {instanceId}", ex);
                return "Error Queue Charges: Exception occured";
            }
        }

        protected static string ValueFromCustomObjects(IList<CustomObject> customObjectDbList, string fieldName)
        {
            foreach (var customObject in customObjectDbList)
            {
                if (customObject.Object.ObjectName == fieldName)
                {
                    return customObject.CustomName;
                }
            }
            return null;
        }
        private async Task<string> InitializeUploadCustomerChargeCDRs(Guid sessionId, string awsAccessKey, string awsSecretAccessKey, string createCDRCustomerChargeQueueName, long[] instanceIds, bool isCrossProviderCustomerOptimization)
        {
            var sessionID = altaWrxDb.vwOptimizationSessions.FirstOrDefault(x => x.SessionId == sessionId);
            if (sessionID == null)
            {
                return string.Format(LogCommonStrings.SESSION_ID_NOT_FOUND, sessionId);
            }

            var serviceProvider = altaWrxDb.ServiceProviders.FirstOrDefault(sp => sp.id == sessionID.ServiceProviderId);
            if (serviceProvider == null)
            {
                return string.Format(LogCommonStrings.SERVICE_PROVIDER_ID_NOT_FOUND, sessionID.ServiceProviderId);
            }

            var customerChargeRepository = new RevCustomerChargeRepository();
            customerChargeRepository.CreateCDRCustomerChargeQueues(altaWrxDb, instanceIds, serviceProvider.Integration.PortalTypeId, isCrossProviderCustomerOptimization);
            var queueIds = altaWrxDb.CustomerChargeQueueToProcesses.AsNoTracking().Select(x => x.QueueId).ToList();
            var enqueueErrorMessageBuilder = new StringBuilder();
            foreach (var queueId in queueIds)
            {
                var enqueueErrorMessage = await EnqueueUploadCustomerChargeCDRs(awsAccessKey, serviceProvider.Integration.PortalTypeId, awsSecretAccessKey, createCDRCustomerChargeQueueName, instanceIds, queueId);
                if (!string.IsNullOrEmpty(enqueueErrorMessage))
                {
                    enqueueErrorMessageBuilder.AppendLine(enqueueErrorMessage);
                }
            }
            return enqueueErrorMessageBuilder.ToString();
        }
        private async Task<string> EnqueueUploadCustomerChargeCDRs(string awsAccessKey, int portalTypeId, string awsSecretAccessKey, string CustomerChargeFileQueueName, long[] instanceIds, long queueId)
        {
            try
            {
                var awsCredentials = new BasicAWSCredentials(awsAccessKey, awsSecretAccessKey);
                using (var client = new AmazonSQSClient(awsCredentials, RegionEndpoint.USEast1))
                {
                    var queueList = client.ListQueues(CustomerChargeFileQueueName);
                    var queueUrl = queueList.QueueUrls[0];
                    if (queueList.HttpStatusCode == HttpStatusCode.OK && queueList.QueueUrls != null && queueList.QueueUrls.Count > 0)
                    {
                        var requestMsgBody = string.Format(LogCommonStrings.SENDING_SQS_MESSAGE_TO_URL, queueUrl);
                        var request = new SendMessageRequest
                        {
                            MessageAttributes = new Dictionary<string, MessageAttributeValue>
                            {
                                {
                                    SQSMessageKeyConstant.QUEUE_ID, new MessageAttributeValue
                                    { DataType = nameof(String), StringValue = queueId.ToString()}
                                },
                                {
                                    SQSMessageKeyConstant.PORTAL_TYPE_ID, new MessageAttributeValue {DataType = nameof(String), StringValue = portalTypeId.ToString()}
                                },
                                {
                                    SQSMessageKeyConstant.INSTANCE_IDS, new MessageAttributeValue {DataType = nameof(String), StringValue = string.Join(",", instanceIds)}
                                }
                            },
                            MessageBody = requestMsgBody,
                            QueueUrl = queueUrl
                        };
                        var response = await RetryPolicyHelper.PollyRetryForSQSMessage().ExecuteAsync(async () => await client.SendMessageAsync(request).ConfigureAwait(false)).ConfigureAwait(false);
                        // Log error on any 4xx or 5xx response statuses
                        if (response.HttpStatusCode >= HttpStatusCode.BadRequest)
                        {
                            return $"{LogCommonStrings.ERROR_WHILE_QUEUING_CHARGES}: {response.HttpStatusCode:d}";
                        }
                        return string.Empty;
                    }
                    return $"{LogCommonStrings.ERROR_WHILE_QUEUING_CHARGES}: {string.Format(LogCommonStrings.QUEUE_NOT_FOUND, CustomerChargeFileQueueName)}";
                }
            }
            catch (Exception ex)
            {
                Log.Error($"{LogCommonStrings.ERROR_WHILE_QUEUING_CHARGES}", ex);
                return $"{LogCommonStrings.ERROR_WHILE_QUEUING_CHARGES}: {ex.Message}";
            }
        }

        protected static string FileNameTimestamp()
        {
            return $"{DateTime.UtcNow.Year}{DateTime.UtcNow.Month}{DateTime.UtcNow.Day}{DateTime.UtcNow.Hour}{DateTime.UtcNow.Minute}";
        }
        #endregion

        #region Rate Plan Confirm
        [System.Web.Http.HttpPost]
        [System.Web.Http.ActionName("Queue-Rate-Plan-Changes")]
        public async Task<string> QueueRatePlanChangesConfirm([System.Web.Http.FromBody] RatePlanUploadDto request)
        {
            ValidateRequest();
            ValidateHeader();
            if (!permissionManager.UserCanCreate(session, ModuleEnum.Optimization))
                return "Access denied";

            // get tenant custom fields
            var customObjectDbList = permissionManager.CustomFields;
            string awsAccessKey = amopBaseController.AwsAccessKeyFromCustomObjects(customObjectDbList);
            string awsSecretAccessKey = amopBaseController.AwsSecretAccessKeyFromCustomObjects(customObjectDbList);
            foreach (long id in request.ids)
            {
                var optimizationInstance = altaWrxDb.OptimizationInstances.FirstOrDefault(x => x.Id == id);
                if (!CheckExistRatePlanAdjustmentInBillingPeriod(id, optimizationInstance?.ServiceProvider))
                {
                    if (permissionManager.UserIsSuperAdmin(session) || permissionManager.UserIsTenantAdmin(session))
                    {
                        string ratePlanQueueName = RatePlanQueueFromCustomObjects(customObjectDbList);

                        var errorMessage = await EnqueueRatePlanSqsAsync(id, awsAccessKey, awsSecretAccessKey, ratePlanQueueName, optimizationInstance?.ServiceProvider);
                        if (!string.IsNullOrEmpty(errorMessage))
                        {
                            SessionHelper.SetAlert(session, errorMessage);
                            SessionHelper.SetAlertType(session, CommonConstants.DANGER);
                        }
                        else
                        {
                            SessionHelper.SetAlert(session, string.Format(LogCommonStrings.SUCCESSFULLY_STARTED_RATE_PLAN_CHANGES, CommonConstants.DEFAULT_LAMBDA_INSTANCE_REMAINING_SECONDS_LIMIT));
                            SessionHelper.SetAlertType(session, CommonConstants.SUCCESS);

                            return "Rate Plan Confirm";
                        }
                    }
                    else
                    {
                        SessionHelper.SetAlert(session, LogCommonStrings.INSUFFICIENT_PRIVILEGES_TO_QUEUE_RATE_PLAN_CHANGES);
                        SessionHelper.SetAlertType(session, CommonConstants.DANGER);
                    }
                }
                else
                {
                    SessionHelper.SetAlert(session, LogCommonStrings.CHANGES_HAVE_ALREADY_BEEN_PUSHED_FOR_THIS_BILL_PERIOD);
                    SessionHelper.SetAlertType(session, CommonConstants.DANGER);
                    return "Queue Rate Plan Changes";
                }
            }

            return "Ok";
        }
        private async Task<string> EnqueueRatePlanSqsAsync(long instanceId, string awsAccessKey, string awsSecretAccessKey, string ratePlanQueueName, ServiceProvider serviceProvider)
        {
            try
            {
                var awsCredentials = new Amazon.Runtime.BasicAWSCredentials(awsAccessKey, awsSecretAccessKey);
                using (var client = new AmazonSQSClient(awsCredentials, Amazon.RegionEndpoint.USEast1))
                {
                    var queueList = client.ListQueues(ratePlanQueueName);
                    if (queueList.HttpStatusCode == System.Net.HttpStatusCode.OK && queueList.QueueUrls != null && queueList.QueueUrls.Count > 0)
                    {
                        var requestMsgBody = $"Rate Plan Update for Instance {instanceId}";
                        var messageAttributes = new Dictionary<string, MessageAttributeValue>
                        {
                            {
                                "InstanceId", new MessageAttributeValue
                                { DataType = "String", StringValue = instanceId.ToString()}
                            }
                        };
                        if (serviceProvider.IntegrationId == (int)IntegrationEnum.Telegence)
                        {
                            messageAttributes.Add("SyncedDevices", new MessageAttributeValue
                            {
                                DataType = "String",
                                StringValue = true.ToString()
                            });
                        }
                        var request = new SendMessageRequest
                        {
                            MessageAttributes = messageAttributes,
                            MessageBody = requestMsgBody,
                            QueueUrl = queueList.QueueUrls[0]
                        };

                        var response = await client.SendMessageAsync(request);
                        return response.HttpStatusCode.IsSuccessStatusCode()
                            ? string.Empty
                            : $"Error Queuing Rate Plan Changes: {response.HttpStatusCode:D} {response.HttpStatusCode:G}";
                    }

                    return "Error Queuing Rate Plan Changes: Queue not found";
                }
            }
            catch (Exception ex)
            {
                Log.Error("Error Queuing Optimization", ex);
                return "Error Queue Optimization: Exception occured";
            }
        }

        private bool CheckExistRatePlanAdjustmentInBillingPeriod(long instanceId, KeySys.BaseMultiTenant.Models.Repositories.ServiceProvider serviceProvider)
        {
            var optimizationInstance = altaWrxDb.OptimizationInstances.FirstOrDefault(x => x.Id == instanceId);
            if (serviceProvider.IntegrationId == (int)IntegrationEnum.Telegence)
            {
                var optimizationRatePlanUpdateSummaries = altaWrxDb.OptimizationRatePlanUpdateSummaries.Include(x => x.OptimizationInstance)
                    .Any(x => x.OptimizationInstance.ServiceProviderId == optimizationInstance.ServiceProvider.id &&
                                x.OptimizationInstance.BillingPeriodStartDate == optimizationInstance.BillingPeriodStartDate &&
                                x.OptimizationInstance.BillingPeriodEndDate == optimizationInstance.BillingPeriodEndDate);
                return optimizationRatePlanUpdateSummaries;
            }
            else
            {
                return false;
            }

        }
        private string RatePlanQueueFromCustomObjects(IList<CustomObject> customObjectDbList)
        {
            return ValueFromCustomObjects(customObjectDbList, RATE_PLAN_QUEUE_NAME);
        }
        #endregion

        #region File Upload Customer Charges
        [System.Web.Http.HttpPost]
        [System.Web.Http.ActionName("Upload")]
        public string Upload(IFormFile file)
        {
            ValidateRequest();
            ValidateHeader();
            if (!permissionManager.UserCanEdit(session, ModuleEnum.M2M) && !permissionManager.UserCanEdit(session, ModuleEnum.Mobility))
            {
                return "Access Denied";
            }
            if (file.Length == 0)
            {
                return "OK";
            }

            //var uploadedFile = file;
            var uploadedFile = new HttpPostedFileWrapperDto(file);
            if (uploadedFile?.FileName == null)
            {
                return "Empty file name. Must select a valid file to process.";
            }

            if (Path.GetExtension(uploadedFile.FileName).ToUpper() != ".CSV")
            {
                return $"Invalid File: {uploadedFile.FileName}.  The file must be in .CSV format.";
            }

            if (uploadedFile.FileName.Length > 255)
            {
                return $"Invalid File: {uploadedFile.FileName}.  The filename must be less than 255 characters long.";
            }

            // A file name must be unique.  Check to see if the file name exists in [dbo].[JasperDeviceStatus_UploadedFile] already.
            bool fileExists;
            fileExists = altaWrxDb.CustomerCharge_UploadedFile.Any(f =>
                f.FileName.Replace(" ", "").ToLower() == uploadedFile.FileName.ToLower());
            if (fileExists)
            {
                return $"'{uploadedFile.FileName}' was processed already and exists in the uploaded customer charges list. File name must be unique.";
            }

            try
            {
                //var stream = uploadedFile.OpenReadStream();
                using (var streamReader = new StreamReader(uploadedFile.InputStream))
                {
                    var csvConfiguration = new CsvConfiguration(CultureInfo.InvariantCulture, prepareHeaderForMatch: Helpers.CsvHelper.PrepareHeaderForMatch);
                    using (var csv = new CsvReader(streamReader, csvConfiguration))
                    {
                        IEnumerable<CustomerChargeCsvRow> lines;
                        try
                        {
                            lines = csv.GetRecords<CustomerChargeCsvRow>().ToList();
                        }
                        catch (Exception e)
                        {
                            var exceptionMessage =
                                $"Error processing file '{uploadedFile.FileName}'. Error: {e.GetBaseException().Message}";
                            return exceptionMessage;
                        }

                        if (!lines.Any()) // There must be at least one line after the header.
                        {
                            return $"Customer charge information was not found in '{uploadedFile.FileName}'.";
                        }
                        Entities db = new Entities();
                        var tenantRepository = new TenantRepository(db);
                        var tenant = tenantRepository.GetParentTenantId(permissionManager.Tenant.id);
                        var revIntegrationId = (int)Amop.Core.Models.IntegrationType.RevIO;
                        var intAuth = altaWrxDb.Integration_Authentication.FirstOrDefault(x => x.TenantId == tenant.id && x.IntegrationId == revIntegrationId);

                        if (intAuth == null)
                        {
                            var errorMessage = "This tenant does not have valid credentials for Rev.IO or Catapult. Please contact the admin to enter credentials and enable this feature.";
                            return errorMessage;
                        }

                        var revServiceRepo = new Repositories.Rev.RevServiceRepository(altaWrxDb, intAuth.id);
                        var revServices = revServiceRepo.GetAll();

                        var missingServiceNumbers = lines
                            .Where(line => !revServices.Any(s => string.Equals(s.Number, line.RevIoServiceNumber,
                                StringComparison.InvariantCultureIgnoreCase)))
                            .Select(line => line.RevIoServiceNumber).ToList();
                        if (missingServiceNumbers.Any())
                        {
                            var errorMessage =
                                $"'{uploadedFile.FileName}' contains invalid Rev.IO Service Numbers. Missing Service Numbers: ${string.Join(", ", missingServiceNumbers)}";
                            return errorMessage;
                        }

                        var revProductTypeRepo = new RevProductTypeRepository(altaWrxDb);
                        var revProductTypeIds = revProductTypeRepo.GetAllProductTypeIds(tenant.id);
                        var missingProductTypes = lines
                            .Where(line => !revProductTypeIds.Any(s => s == line.RevIoProductTypeId))
                            .Select(line => line.RevIoProductTypeId).ToList();
                        if (missingProductTypes.Any())
                        {
                            var errorMessage =
                                $"'{uploadedFile.FileName}' contains invalid Rev.IO Product Types. Missing Product Types: ${string.Join(", ", missingProductTypes)}";
                            return errorMessage;
                        }

                        var appFileRepository = new AppFileRepository(altaWrxDb);
                        try
                        {
                            var awsFile = UploadFileToAWS(uploadedFile);
                            if (!string.IsNullOrEmpty(awsFile))
                            {
                                var appFile = new Models.Repositories.AppFile
                                {
                                    AmazonFileName = awsFile,
                                    FileName = uploadedFile.FileName,
                                    TenantId = permissionManager.Tenant.id,
                                    CreatedBy = userName,
                                    CreatedDate = DateTime.UtcNow,
                                    IsActive = true,
                                    IsDeleted = false
                                };
                                appFileRepository.SaveNew(session, appFile);
                            }
                        }
                        catch (Exception ex)
                        {
                            return $"Could not save the file to S3 bucket. {ex.Message}";
                        }

                        var billingPeriodRepo = new Repositories.BillingPeriod.BillingPeriodRepository(altaWrxDb);
                        var deviceTenantRepo = new DeviceTenantRepository(altaWrxDb, tenant.id);
                        var m2mDevicesWithService = deviceTenantRepo.GetAllM2MRevServiceIds(intAuth.id);
                        var m2mServices = revServices
                            .Where(rs => m2mDevicesWithService.FirstOrDefault(x => x.RevServiceId.Value == rs.id) != null)
                            .Select(rs => new M2MCustomerChargeUploadRecord()
                            {
                                M2MDeviceRevServiceRecord = m2mDevicesWithService.First(x => x.RevServiceId.Value == rs.id),
                                RevService = rs
                            })
                            .ToList();

                        var mobilityDeviceTenantRepo = new MobilityDeviceTenantRepository(altaWrxDb, tenant.id);
                        var mobilityDevicesWithService = mobilityDeviceTenantRepo.GetAllMobilityRevServiceIds(intAuth.id);
                        var mobilityServices = revServices
                            .Where(rs => mobilityDevicesWithService.FirstOrDefault(x => x.RevServiceId.Value == rs.id) != null)
                            .Select(rs => new MobilityCustomerChargeUploadRecord()
                            {
                                MobilityDeviceRevServiceRecord = mobilityDevicesWithService.First(x => x.RevServiceId.Value == rs.id),
                                RevService = rs
                            })
                            .ToList();
                        var m2mLines = lines
                            .Where(line =>
                                m2mServices.Any(s => string.Equals(s.RevService.Number, line.RevIoServiceNumber, StringComparison.InvariantCultureIgnoreCase))
                            )
                            .Select(line =>
                                m2mServices.First(s => string.Equals(s.RevService.Number, line.RevIoServiceNumber, StringComparison.InvariantCultureIgnoreCase)).AddCsvRow(line)
                            )
                            .ToList();
                        var mobilityLines = lines
                            .Where(line =>
                                mobilityServices.Any(s => string.Equals(s.RevService.Number, line.RevIoServiceNumber, StringComparison.InvariantCultureIgnoreCase))
                            )
                            .Select(line =>
                                mobilityServices.First(s => string.Equals(s.RevService.Number, line.RevIoServiceNumber, StringComparison.InvariantCultureIgnoreCase)).AddCsvRow(line)
                            )
                            .ToList();
                        var customerChargeFileRepository = new CustomerChargeUploadedFileRepository(altaWrxDb);
                        var savedFile = customerChargeFileRepository.Create(session, uploadedFile.FileName, intAuth.id, appFileRepository.Object.id);
                        var m2mQueueEntries = m2mLines.Select(r => MapOptimizationDeviceResultCustomerChargeQueue(billingPeriodRepo, r, savedFile, intAuth));
                        var mobilityQueueEntries = mobilityLines.Select(r => MapOptimizationMobilityDeviceResultCustomerChargeQueue(billingPeriodRepo, r, savedFile, intAuth));
                        try
                        {
                            using (var transaction = altaWrxDb.Database.BeginTransaction())
                            {
                                var customObjectDbList = permissionManager.CustomFields;
                                var awsAccessKey = amopBaseController.AwsAccessKeyFromCustomObjects(customObjectDbList);
                                var awsSecretAccessKey = amopBaseController.AwsSecretAccessKeyFromCustomObjects(customObjectDbList);
                                var deviceCustomerChargeQueueName = CustomerChargeQueueFromCustomObjects(customObjectDbList);

                                altaWrxDb.OptimizationDeviceResult_CustomerChargeQueue.AddRange(m2mQueueEntries);
                                altaWrxDb.OptimizationMobilityDeviceResult_CustomerChargeQueue.AddRange(mobilityQueueEntries);
                                altaWrxDb.SaveChanges();
                                var response = EnqueueCustomerChargesSqs(savedFile.id, awsAccessKey, awsSecretAccessKey,
                                    deviceCustomerChargeQueueName);
                                if (string.IsNullOrEmpty(response))
                                {
                                    transaction.Commit();
                                }
                                else
                                {
                                    return $"Could not save the file. {response}";
                                }
                            }
                        }
                        catch (DbUpdateException ex)
                        {
                            return $"Could not save the file. {ex.Message}";
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                return $"Error adding the file. {ex.Message}";
            }

            SessionHelper.SetAlert(session, "Successfully uploaded customer charges.");
            SessionHelper.SetAlertType(session, "success");
            return "OK";
        }
        private string CustomerChargeQueueFromCustomObjects(IList<CustomObject> customObjectDbList)
        {
            return ValueFromCustomObjects(customObjectDbList, CUSTOMER_CHARGE_QUEUE_NAME);
        }
        private string S3BucketNameFromCustomObject(IList<CustomObject> customObjectDbList)
        {
            return ValueFromCustomObjects(customObjectDbList, S3_BUCKET_NAME);
        }
        private OptimizationDeviceResult_CustomerChargeQueue MapOptimizationDeviceResultCustomerChargeQueue(Repositories.BillingPeriod.BillingPeriodRepository repo, M2MCustomerChargeUploadRecord record, CustomerCharge_UploadedFile uploadedFile, Integration_Authentication integrationAuth)
        {
            var billingPeriod = GetBillingPeriod(repo, record.M2MDeviceRevServiceRecord.ServiceProviderId, record.CustomerChargeCsvRow.BillingEndDate);

            return new OptimizationDeviceResult_CustomerChargeQueue
            {
                RevProductTypeId = record.CustomerChargeCsvRow.RevIoProductTypeId,
                UploadedFileId = uploadedFile.id,
                ChargeAmount = record.CustomerChargeCsvRow.OverageChargeAmount,
                BaseChargeAmount = record.CustomerChargeCsvRow.BaseChargeAmount,
                TotalChargeAmount = record.CustomerChargeCsvRow.BaseChargeAmount + record.CustomerChargeCsvRow.OverageChargeAmount,
                CreatedBy = SessionHelper.GetAuditByName(session),
                CreatedDate = DateTime.UtcNow,
                RevServiceNumber = record.CustomerChargeCsvRow.RevIoServiceNumber,
                Description = record.CustomerChargeCsvRow.Description,
                BillingStartDate = record.CustomerChargeCsvRow.BillingStartDate,
                BillingEndDate = record.CustomerChargeCsvRow.BillingEndDate,
                IntegrationAuthenticationId = integrationAuth.id,
                BillingPeriodId = billingPeriod?.id,
                SmsChargeAmount = record.CustomerChargeCsvRow.SmsChargeAmount,
                SmsRevProductTypeId = record.CustomerChargeCsvRow.SmsRevIoProductTypeId
            };
        }
        private OptimizationMobilityDeviceResult_CustomerChargeQueue MapOptimizationMobilityDeviceResultCustomerChargeQueue(Repositories.BillingPeriod.BillingPeriodRepository repo, MobilityCustomerChargeUploadRecord record, CustomerCharge_UploadedFile uploadedFile, Integration_Authentication integrationAuth)
        {
            var billingPeriod = GetBillingPeriod(repo, record.MobilityDeviceRevServiceRecord.ServiceProviderId, record.CustomerChargeCsvRow.BillingEndDate);

            return new OptimizationMobilityDeviceResult_CustomerChargeQueue
            {
                RevProductTypeId = record.CustomerChargeCsvRow.RevIoProductTypeId,
                UploadedFileId = uploadedFile.id,
                ChargeAmount = record.CustomerChargeCsvRow.OverageChargeAmount,
                BaseChargeAmount = record.CustomerChargeCsvRow.BaseChargeAmount,
                TotalChargeAmount = record.CustomerChargeCsvRow.BaseChargeAmount + record.CustomerChargeCsvRow.OverageChargeAmount,
                CreatedBy = SessionHelper.GetAuditByName(session),
                CreatedDate = DateTime.UtcNow,
                RevServiceNumber = record.CustomerChargeCsvRow.RevIoServiceNumber,
                Description = record.CustomerChargeCsvRow.Description,
                BillingStartDate = record.CustomerChargeCsvRow.BillingStartDate,
                BillingEndDate = record.CustomerChargeCsvRow.BillingEndDate,
                IntegrationAuthenticationId = integrationAuth.id,
                BillingPeriodId = billingPeriod?.id,
                SmsChargeAmount = record.CustomerChargeCsvRow.SmsChargeAmount,
                SmsRevProductTypeId = record.CustomerChargeCsvRow.SmsRevIoProductTypeId
            };
        }
        private static MemoryCache billingPeriodByServiceProviderAndDate = MemoryCache.Default;
        private static object cacheLockObject = new object();
        private BillingPeriod GetBillingPeriod(Repositories.BillingPeriod.BillingPeriodRepository repo, int serviceProviderId, DateTime billingPeriodEndDate)
        {
            var cacheKey = $"{serviceProviderId}_{billingPeriodEndDate.ToShortDateString()}";
            lock (cacheLockObject)
            {
                if (billingPeriodByServiceProviderAndDate.Contains(cacheKey))
                {
                    return (BillingPeriod)billingPeriodByServiceProviderAndDate[cacheKey];
                }
                else
                {
                    var billingPeriod = repo.GetBillingPeriodByServiceProviderAndDate(serviceProviderId, billingPeriodEndDate);

                    CacheItemPolicy policy = new CacheItemPolicy
                    {
                        AbsoluteExpiration = DateTime.UtcNow + TimeSpan.FromMinutes(10)
                    };

                    billingPeriodByServiceProviderAndDate.Add(cacheKey, billingPeriod, policy);
                    return billingPeriod;
                }
            }
        }
        private static string EnqueueCustomerChargesSqs(int fileId, string awsAccessKey, string awsSecretAccessKey,
           string deviceCustomerChargeQueueName)
        {
            try
            {
                var awsCredentials = new BasicAWSCredentials(awsAccessKey, awsSecretAccessKey);
                using (var client = new AmazonSQSClient(awsCredentials, RegionEndpoint.USEast1))
                {
                    var queueList = client.ListQueues(deviceCustomerChargeQueueName);
                    if (queueList.HttpStatusCode == HttpStatusCode.OK && queueList.QueueUrls != null &&
                        queueList.QueueUrls.Count > 0)
                    {
                        var requestMsgBody = $"File to work is {fileId}";
                        var request = new SendMessageRequest
                        {
                            MessageAttributes = new Dictionary<string, MessageAttributeValue>
                            {
                                {
                                    "FileId", new MessageAttributeValue
                                        {DataType = "String", StringValue = fileId.ToString()}
                                }
                            },
                            MessageBody = requestMsgBody,
                            QueueUrl = queueList.QueueUrls[0]
                        };

                        var response = client.SendMessageAsync(request);
                        response.Wait();
                        if (response.Status == TaskStatus.Faulted || response.Status == TaskStatus.Canceled)
                        {
                            return $"Error Queuing Charges: {response.Status}";
                        }

                        // success
                        return string.Empty;
                    }

                    return "Error Queuing Charges: Queue not found";
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error Queuing Charges for {fileId}", ex);
                return "Error Queue Charges: Exception occured";
            }
        }
        private string UploadFileToAWS(HttpPostedFileWrapperDto uploadedFile)
        {
            var customObjectDbList = permissionManager.CustomFields;
            string awsAccessKey = amopBaseController.AwsAccessKeyFromCustomObjects(customObjectDbList);
            string awsSecretAccessKey = amopBaseController.AwsSecretAccessKeyFromCustomObjects(customObjectDbList);
            string s3BucketName = S3BucketNameFromCustomObject(customObjectDbList);

            var credentials = new BasicAWSCredentials(awsAccessKey, awsSecretAccessKey);
            S3Wrapper s3wrapper = new S3Wrapper(credentials, s3BucketName);
            var awsFile = s3wrapper.UploadAwsFile(uploadedFile.InputStream, null);
            return awsFile;
        }
        #endregion

        #region Optimization check 
        public List<vwOptimizationSession> GetOptimizationSessionsByTenantId(int tenantId, OptimizationType optimizationType, string filter)
        {
            var optimizationSessions = optimizationType == OptimizationType.All
                ? altaWrxDb.vwOptimizationSessions.Where(os => os.TenantId == tenantId && os.IsActive && !os.IsDeleted)
                    .OrderByDescending(os => os.CreatedDate).ToList()
                : altaWrxDb.vwOptimizationSessions
                    .Where(os => os.TenantId == tenantId && os.OptimizationTypeId == (int)optimizationType && os.IsActive && !os.IsDeleted)
                    .OrderByDescending(os => os.CreatedDate).ToList();

            if (string.IsNullOrEmpty(filter))
                return optimizationSessions;

            filter = filter.ToLower();
            optimizationSessions = optimizationSessions.Where(os =>
                (os.ServiceProvider != null && os.ServiceProvider.ToLower().Contains(filter)) || os.BillingPeriodEndDate.ToString().Contains(filter)).ToList();
            return optimizationSessions;
        }

        public List<vwOptimizationSessionRunning> GetOptimizationRunning(int optimizationSessionId)
        {
            var optimizationRunningList = new List<vwOptimizationSessionRunning>();
            try
            {
                using (SqlConnection conn = new SqlConnection(permissionManager.AltaworxCentralConnectionStringWithoutEF))
                {
                    using (SqlCommand cmd = new SqlCommand("usp_OptimizationSessionRunningList", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.CommandTimeout = SQLConstant.PortalTimeoutSeconds;
                        cmd.Parameters.AddWithValue("@OptimizationSessionId", optimizationSessionId);
                        conn.Open();
                        using (var dataReader = cmd.ExecuteReader())
                        {
                            Log.Info("dataReader.HasRows - " + dataReader.HasRows);
                            if (dataReader.HasRows)
                            {
                                while (dataReader.Read())
                                {
                                    BuildOptimizationList(dataReader, optimizationRunningList);
                                }
                                conn.Close();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Exception - " + ex.Message.ToString());
            }
            return optimizationRunningList;
        }
        private static void BuildOptimizationList(SqlDataReader dataReader, List<vwOptimizationSessionRunning> optimizationRunningList)
        {
            var optimizationRunningLists = new vwOptimizationSessionRunning
            {
                Id = int.Parse(dataReader[CommonColumnNames.Id].ToString()),
                OptimizationSessionId = Convert.ToInt64(dataReader[CommonColumnNames.OptimizationSessionId].ToString()),
                OptimizationInstanceStatusId = Convert.ToInt32(dataReader["OptimizationInstanceStatusId"].ToString()),
                ServiceProviderId = int.Parse(dataReader["ServiceProviderId"].ToString()),
                ServiceProviderIds = dataReader["ServiceProviderIds"].ToString(),
            };
            optimizationRunningList.Add(optimizationRunningLists);
        }
        public bool CheckOptimizationIsRunning(int tenantId)
        {
            var optimizationSessionList = GetOptimizationSessionsByTenantId(tenantId, OptimizationType.All, "");
            if (optimizationSessionList == null || optimizationSessionList.Count == 0)
            {
                return false;
            }

            var optimizationSessionRunningList = GetOptimizationRunning(Convert.ToInt32(optimizationSessionList.FirstOrDefault().Id));
            if (optimizationSessionRunningList == null || optimizationSessionRunningList.Count == 0)
            {
                return false;
            }

            var optRunning = optimizationSessionRunningList.Where(x => x.OptimizationSessionId == optimizationSessionList.FirstOrDefault().Id).ToList();
            if (optRunning != null && optRunning.Count > 0)
            {
                return !optRunning.Any(x => x.OptimizationInstanceStatusId == (int)OptimizationStatusEnum.CompleteWithError);
            }
            return false;
        }

        #endregion
        public int GetOptimizationDeviceCountByInstance(int optimizationSessionId)
        {
            int deviceCount = 0;
            try
            {
                deviceCount = altaWrxDb.vwOptimizationInstances.Where(os => os.OptimizationSessionId == optimizationSessionId).Sum(x => x.DeviceCount ?? 0);
            }
            catch (Exception ex)
            {
                Log.Error("Exception - " + ex.Message.ToString());
            }
            return deviceCount;
        }

        #region Send notification trigger to 2.0 for Optimization
        public static async Task SendResponseToAMOP20(string jobName, string optimizationSessionId, string optimizationSessionGuid, int deviceCount, string errorMessage = null, int progress = 0, string customerId = null, string additionalJson = null)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls13 | SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
            var amop20ApiUrl = ConfigurationManager.AppSettings["Amop20ApiUrl"];
            using (var client = new HttpClient(new PortalLoggingHandler()))
            {
                client.BaseAddress = new Uri(amop20ApiUrl);
                string path = jobName == "Progress"
                    ? "/get_optimization_progress_bar_data"
                    : "/get_optimization_error_details_data";

                var requestData = new
                {
                    data = new
                    {
                        path,
                        job_name = jobName,
                        SessionId = optimizationSessionId,
                        OptimizationSessionGuid = optimizationSessionGuid,
                        ErrorMessage = errorMessage,
                        Progress = progress,
                        CustomerId = customerId,
                        AdditionalJson = additionalJson,
                        DeviceCount = deviceCount
                    }
                };

                string jsonRequest = JsonConvert.SerializeObject(requestData);
                var contDevice = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(client.BaseAddress, contDevice);
                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    Log.Info($"Response: {responseBody}");
                }
                else
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    Log.Error($"Response: {responseBody}");
                }
            }
        }
        #endregion

        #region Send notification trigger to 2.0
        public async Task SendTriggerAmopSync(string keyName, int? tenantId = null, string tenantName = null)
        {
            string amop20SyncUpdateApiUrl = ConfigurationManager.AppSettings["AmopSyncUpdateApiUrl"];
            Log.Error($"API URL - " + amop20SyncUpdateApiUrl);
            using (HttpClient client = new HttpClient(new PortalLoggingHandler()))
            {
                client.BaseAddress = new Uri(amop20SyncUpdateApiUrl);
                client.DefaultRequestHeaders.Add("Accept", "application/json");
                string jsonRequest = null;
                string path = "/lambda_sync_jobs_";

                var requestData = new
                {
                    data = new
                    {
                        path,
                        key_name = keyName,
                        tenant_id = tenantId,
                        tenant_name = tenantName
                    }
                };

                jsonRequest = JsonConvert.SerializeObject(requestData);
                var contDevice = new StringContent(jsonRequest, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await client.PostAsync(client.BaseAddress, contDevice);
                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    Log.Error($"Sent Response to AMOP2.0 - Success");
                }
                else
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    Log.Error($"Sent Response to AMOP2.0 - Failed" + responseBody);
                }
            }
        }
        #endregion
    }
}