using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Altaworx.SimCard.Cost.Optimizer.Core.Enumerations;
using Altaworx.SimCard.Cost.Optimizer.Core.Helpers;
using Altaworx.SimCard.Cost.Optimizer.Core.Models;
using Amazon.Lambda.Core;
using Amazon.Lambda.SQSEvents;
using Amazon.SQS;
using Amazon.SQS.Model;
using Amop.Core.Logger;
// to use SQLCommandExtensions
using Amop.Core.Helpers;
using Amop.Core.Models.Settings;
using Amop.Core.Resilience;
using Amop.Core.Models;
using Newtonsoft.Json;
using System.Runtime.CompilerServices;
using MimeKit;
using Polly;
using Altaworx.SimCard.Cost.Optimizer.Core.Factories;
using Amop.Core.Services.Base64Service;
using Amazon;
using System.IO;
using Amazon.SimpleEmail.Model;
// to use FormatLogStringObject 
using Altaworx.AWS.Core.Helpers;
// to use LogTypeConstant
using Altaworx.AWS.Core.Helpers.Constants;
using Altaworx.SimCard.Cost.Optimizer.Core.Repositories.Optimization;
using Amop.Core.Constants;
using SQLConstant = Amop.Core.Constants.SQLConstant;
using Amop.Core.Repositories.Environment;
using Altaworx.SimCard.Cost.Optimizer.Core.Repositories.CarrierRatePlan;
using Altaworx.SimCard.Cost.Optimizer.Core.Repositories.ServiceProvider;
using Altaworx.SimCard.Cost.Optimizer.Core.Repositories.CustomerRatePlan;

namespace Altaworx.SimCard.Cost.Optimizer.Core
{
    public class AwsFunctionBase
    {
        private const int EMAIL_RETRY_MAX_COUNT = 3;
        private const int SQL_RETRY_MAX_COUNT = 3;
        public static List<string> InActiveStatuses = new List<string>() { "DEACTIVATED", "RETIRED" };
        private string RedisReconfigureDocumentationURL = "https://wiki.amop.services/en/amop-redis-configuration";
        protected IOptimizationMobilityDeviceRepository optimizationMobilityDeviceRepository;
        protected PortalTypes PortalType { get; set; }
        protected CarrierRatePlanRepository carrierRatePlanRepository;
        protected ServiceProviderRepository serviceProviderRepository;
        protected ICrossProviderOptimizationRepository crossProviderOptimizationRepository;
        protected ICustomerRatePlanRepository customerRatePlanRepository;
        protected IOptimizationRepository optimizationRepository;

        protected bool IsCrossProviderOptimization
        {
            get
            {
                return PortalType == PortalTypes.CrossProvider;
            }
        }
        //Default to M2M for test cases and lambdas of M2M that is using this base class but not using the portaltype value
        public AwsFunctionBase(PortalTypes portalTypeValue = PortalTypes.M2M)
        {
            optimizationMobilityDeviceRepository = new OptimizationMobilityDeviceRepository();
            optimizationRepository = new OptimizationRepository();
            PortalType = portalTypeValue;
        }

        public void SetPortalType(PortalTypes portalType)
        {
            PortalType = portalType;
        }

        public static void LogInfo(KeySysLambdaContext context, string desc, object detail,
                        [CallerFilePath] string file = "",
                        [CallerLineNumber] int line = 0,
                        [CallerMemberName] string functionName = "")
        {
            context.LogInfo(desc, StringHelper.FormatLogStringObject(desc, detail, file, line, functionName));
        }

        public void LogInfo(KeySysLambdaContext context, LogMessage message)
        {
            if (message != null)
            {
                context.LogInfo(message.Message, message.ObjectToLog);
            }
        }

        public KeySysLambdaContext BaseFunctionHandler(ILambdaContext context)
        {
            return new KeySysLambdaContext(context);
        }

        public virtual void CleanUp(KeySysLambdaContext context)
        {
            context.CleanUp();
        }

        public List<BillingPeriod> GetBillingPeriodsForServiceProviders(KeySysLambdaContext context, List<int> serviceProviderIds, int billingPeriodYear, int billingPeriodMonth)
        {
            List<BillingPeriod> billingPeriods = new List<BillingPeriod>();
            if (serviceProviderIds != null)
            {
                foreach (var serviceProviderId in serviceProviderIds)
                {
                    var billPeriodsForProvider = GetBillingPeriodsForServiceProvider(context, serviceProviderId, billingPeriodYear, billingPeriodMonth);
                    if (billPeriodsForProvider != null && billPeriodsForProvider.Count > 0)
                    {
                        billingPeriods.AddRange(billPeriodsForProvider);
                    }
                }
            }

            return billingPeriods;
        }

        public BillingPeriod GetBillingPeriod(KeySysLambdaContext context, int billingPeriodId)
        {
            LogInfo(context, "SUB", $"GetBillingPeriod({billingPeriodId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                var serviceProviderId = 0;
                var billYear = 0;
                var billMonth = 0;
                var billPeriodEndDay = 0;
                var billPeriodEndHour = 0;
                var billCycleEndDate = DateTime.MinValue;
                using (var connection = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = @"SELECT sp.id ServiceProviderId, BillYear, BillMonth, BillingCycleEndDate, BillPeriodEndDay, BillPeriodEndHour 
                                        FROM [dbo].[BillingPeriod] bp
                                        INNER JOIN [dbo].[ServiceProvider] sp ON bp.ServiceProviderId = sp.id
                                        WHERE bp.id = @billPeriodId";
                        cmd.Parameters.AddWithValue("@billPeriodId", billingPeriodId);
                        connection.Open();

                        var rdr = cmd.ExecuteReader();

                        while (rdr.Read())
                        {
                            billYear = int.Parse(rdr["BillYear"].ToString());
                            billMonth = int.Parse(rdr["BillMonth"].ToString());
                            serviceProviderId = int.Parse(rdr["ServiceProviderId"].ToString());
                            billPeriodEndDay = !rdr.IsDBNull(rdr.GetOrdinal("BillPeriodEndDay"))
                                ? int.Parse(rdr["BillPeriodEndDay"].ToString())
                                : KeySysLambdaContext.DEFAULT_BILLING_PERIOD_END_DAY;
                            billPeriodEndHour = !rdr.IsDBNull(rdr.GetOrdinal("BillPeriodEndHour"))
                                ? int.Parse(rdr["BillPeriodEndHour"].ToString())
                                : KeySysLambdaContext.DEFAULT_BILLING_PERIOD_END_HOUR;

                            context.LogInfo("INFO", $"{billYear}, {billMonth}, {billPeriodEndDay}, {billPeriodEndHour}");
                            billCycleEndDate = !rdr.IsDBNull(rdr.GetOrdinal("BillingCycleEndDate"))
                                ? DateTime.Parse(rdr["BillingCycleEndDate"].ToString())
                                : new DateTime(billYear, billMonth, billPeriodEndDay, billPeriodEndHour, 0, 0);
                        }

                        connection.Close();
                    }
                }
                return billCycleEndDate != DateTime.MinValue
                    ? new BillingPeriod(billingPeriodId, serviceProviderId, billYear, billMonth, billPeriodEndDay, billPeriodEndHour, context.OptimizationSettings.BillingTimeZone, billCycleEndDate)
                    : new BillingPeriod(billingPeriodId, serviceProviderId, billYear, billMonth, billPeriodEndDay, billPeriodEndHour, context.OptimizationSettings.BillingTimeZone);
            });
        }

        public BillingPeriod GetNextBillingPeriod(KeySysLambdaContext context, int serviceProviderId, DateTime billingCycleEndDate)
        {
            LogInfo(context, "SUB", $"GetNextBillingPeriod({billingCycleEndDate})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                var billingPeriodId = 0;
                var newServiceProviderId = 0;
                var billYear = 0;
                var billMonth = 0;
                var billPeriodEndDay = 0;
                var billPeriodEndHour = 0;
                var billCycleEndDate = DateTime.MinValue;
                using (var connection = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandText = @"SELECT TOP 1 bp.id AS BillingPeriodId, sp.id ServiceProviderId, BillYear, BillMonth, BillingCycleEndDate, BillPeriodEndDay, BillPeriodEndHour 
                                FROM [dbo].[BillingPeriod] bp
                                INNER JOIN [dbo].[ServiceProvider] sp ON bp.ServiceProviderId = sp.id
                                WHERE bp.BillingCycleEndDate > @billingCycleEndDate AND bp.ServiceProviderId = @serviceProviderId AND bp.IsDeleted = 0
                                ORDER BY bp.BillingCycleEndDate";
                        cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId);
                        cmd.Parameters.AddWithValue("@billingCycleEndDate", billingCycleEndDate);
                        connection.Open();

                        var rdr = cmd.ExecuteReader();

                        if (rdr.Read())
                        {
                            billingPeriodId = int.Parse(rdr["BillingPeriodId"].ToString());
                            billYear = int.Parse(rdr["BillYear"].ToString());
                            billMonth = int.Parse(rdr["BillMonth"].ToString());
                            newServiceProviderId = int.Parse(rdr["ServiceProviderId"].ToString());
                            billPeriodEndDay = !rdr.IsDBNull(rdr.GetOrdinal("BillPeriodEndDay"))
                                ? int.Parse(rdr["BillPeriodEndDay"].ToString())
                                : KeySysLambdaContext.DEFAULT_BILLING_PERIOD_END_DAY;
                            billPeriodEndHour = !rdr.IsDBNull(rdr.GetOrdinal("BillPeriodEndHour"))
                                ? int.Parse(rdr["BillPeriodEndHour"].ToString())
                                : KeySysLambdaContext.DEFAULT_BILLING_PERIOD_END_HOUR;

                            context.LogInfo("INFO", $"{billYear}, {billMonth}, {billPeriodEndDay}, {billPeriodEndHour}");
                            billCycleEndDate = !rdr.IsDBNull(rdr.GetOrdinal("BillingCycleEndDate"))
                                ? DateTime.Parse(rdr["BillingCycleEndDate"].ToString())
                                : new DateTime(billYear, billMonth, billPeriodEndDay, billPeriodEndHour, 0, 0);
                        }

                        connection.Close();
                    }
                }

                // is there a future billing period
                if (billingPeriodId == 0)
                {
                    return null;
                }

                return billCycleEndDate != DateTime.MinValue
                    ? new BillingPeriod(billingPeriodId, newServiceProviderId, billYear, billMonth, billPeriodEndDay, billPeriodEndHour, context.OptimizationSettings.BillingTimeZone, billCycleEndDate)
                    : new BillingPeriod(billingPeriodId, newServiceProviderId, billYear, billMonth, billPeriodEndDay, billPeriodEndHour, context.OptimizationSettings.BillingTimeZone);
            });
        }

        public List<BillingPeriod> GetBillingPeriodsForServiceProvider(KeySysLambdaContext context, int serviceProviderId, int billPeriodYear, int billPeriodMonth)
        {
            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                var billingPeriods = new List<BillingPeriod>();

                int billingPeriodId = 0;
                int billingPeriodEndDay = KeySysLambdaContext.DEFAULT_BILLING_PERIOD_END_DAY;
                int billingPeriodEndHour = KeySysLambdaContext.DEFAULT_BILLING_PERIOD_END_HOUR;

                using (var Conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = Conn.CreateCommand())
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.CommandText = "dbo.usp_Service_Provider_Get_Bill_Period_Day_And_Hour";
                        cmd.Parameters.AddWithValue("@ServiceProviderId", serviceProviderId);
                        cmd.Parameters.AddWithValue("@BillMonth", billPeriodMonth);
                        cmd.Parameters.AddWithValue("@BillYear", billPeriodYear);
                        Conn.Open();

                        SqlDataReader rdr = cmd.ExecuteReader();

                        while (rdr.Read())
                        {
                            int billPeriodEndDayIndex = rdr.GetOrdinal("BillPeriodEndDay");
                            if (!rdr.IsDBNull(billPeriodEndDayIndex))
                            {
                                billingPeriodEndDay = (int)rdr["BillPeriodEndDay"];
                            }

                            int billPeriodEndHourIndex = rdr.GetOrdinal("BillPeriodEndHour");
                            if (!rdr.IsDBNull(billPeriodEndHourIndex))
                            {
                                billingPeriodEndHour = (int)rdr["BillPeriodEndHour"];
                            }

                            int billPeriodIdIndex = rdr.GetOrdinal("Id");
                            if (!rdr.IsDBNull(billPeriodIdIndex))
                            {
                                billingPeriodId = (int)rdr["Id"];
                            }

                            var billPeriod = new BillingPeriod(billingPeriodId, serviceProviderId, billPeriodYear, billPeriodMonth, billingPeriodEndDay, billingPeriodEndHour, context.OptimizationSettings.BillingTimeZone);
                            billingPeriods.Add(billPeriod);
                        }

                        Conn.Close();
                    }
                }

                return billingPeriods;
            });
        }

        public int GetExpectedOptimizationSimCardCount(KeySysLambdaContext context, int serviceProviderId, string revAccountNumber, int billingPeriodId, int? integrationAuthenticationId, int tenantId)
        {
            context.logger.LogInfo("SUB", $"GetExpectedOptimizationSimCardCount({serviceProviderId},{revAccountNumber},{billingPeriodId},{integrationAuthenticationId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                int simCards = 0;
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("usp_OptimizationSimCardsCount", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@ServiceProviderId", serviceProviderId);
                        cmd.Parameters.AddWithValue(CommonSQLParameterNames.TENANT_ID, tenantId);
                        if (!string.IsNullOrWhiteSpace(revAccountNumber))
                        {
                            cmd.Parameters.AddWithValue("@RevAccountNumber", revAccountNumber);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@RevAccountNumber", DBNull.Value);
                        }
                        cmd.Parameters.AddWithValue("@BillingPeriodId", billingPeriodId);
                        if (integrationAuthenticationId.HasValue)
                        {
                            cmd.Parameters.AddWithValue("@IntegrationAuthenticationId", integrationAuthenticationId.Value);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@IntegrationAuthenticationId", DBNull.Value);
                        }
                        conn.Open();

                        var reader = cmd.ExecuteReader();
                        if (reader.Read())
                        {
                            simCards = reader.GetInt32("SimCardCount");
                        }
                    }
                }

                return simCards;
            });
        }

        private OptimizationSetting SettingFromReader(SqlDataReader rdr)
        {
            return new OptimizationSetting()
            {
                SettingKey = rdr["SettingKey"].ToString(),
                SettingValue = rdr["SettingValue"].ToString()
            };
        }

        public static OptimizationInstance GetInstance(KeySysLambdaContext context, long instanceId)
        {
            return GetInstance(instanceId, context.ConnectionString, context.logger);
        }

        public static OptimizationInstance GetInstance(long instanceId, string connectionString, IKeysysLogger logger)
        {
            logger.LogInfo("SUB", $"GetInstance({instanceId})");

            var policyFactory = new PolicyFactory(logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                var queue = new OptimizationInstance();
                using (var conn = new SqlConnection(connectionString))
                {
                    using (var cmd = new SqlCommand(@"SELECT OI.Id, OI.RunStatusId, OI.RunStartTime, OI.RunEndTime, OI.BillingPeriodStartDate, OI.BillingPeriodEndDate, OI.RevCustomerId, OI.ServiceProviderId, OI.TenantId, OI.PortalTypeId, SP.IntegrationId, OI.OptimizationBillingPeriodId, OI.UseBillInAdvance, OI.BillInAdvanceBillingPeriodId, OI.AMOPCustomerId, OI.OptimizationSessionId, 
                        OI.[CustomerBillingPeriodId],
                        OI.[CustomerBillInAdvanceBillingPeriodId],
                        OI.[ServiceProviderIds]
                        FROM OptimizationInstance OI LEFT JOIN ServiceProvider SP ON OI.ServiceProviderId = SP.id WHERE OI.Id = @instanceId", conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@instanceId", instanceId);
                        conn.Open();

                        var rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            queue = InstanceFromReader(rdr);
                        }
                    }
                }

                return queue;
            });
        }

        private static OptimizationInstance InstanceFromReader(SqlDataReader rdr)
        {
            var columns = rdr.GetColumnsFromReader();
            return new OptimizationInstance
            {
                Id = long.Parse(rdr["Id"].ToString()),
                RunStatusId = int.Parse(rdr["RunStatusId"].ToString()),
                RunStartTime = !rdr.IsDBNull(2) ? DateTime.Parse(rdr["RunStartTime"].ToString()) : new DateTime?(),
                RunEndTime = !rdr.IsDBNull(3) ? DateTime.Parse(rdr["RunEndTime"].ToString()) : new DateTime?(),
                BillingPeriodStartDate = DateTime.Parse(rdr["BillingPeriodStartDate"].ToString()),
                BillingPeriodEndDate = DateTime.Parse(rdr["BillingPeriodEndDate"].ToString()),
                RevCustomerId = !rdr.IsDBNull(6) ? Guid.Parse(rdr["RevCustomerId"].ToString()) : new Guid?(),
                ServiceProviderId = !rdr.IsDBNull(7) ? int.Parse(rdr["ServiceProviderId"].ToString()) : new int?(),
                TenantId = !rdr.IsDBNull(8) ? int.Parse(rdr["TenantId"].ToString()) : 1,
                PortalType = (PortalTypes)int.Parse(rdr["PortalTypeId"].ToString()),
                IntegrationId = !rdr.IsDBNull(10) ? int.Parse(rdr["IntegrationId"].ToString()) : new int?(),
                OptimizationBillingPeriodId = !rdr.IsDBNull(11) ? int.Parse(rdr["OptimizationBillingPeriodId"].ToString()) : new int?(),
                UseBillInAdvance = bool.Parse(rdr["UseBillInAdvance"].ToString()),
                BillInAdvanceBillingPeriodId = !rdr.IsDBNull(13) ? int.Parse(rdr["BillInAdvanceBillingPeriodId"].ToString()) : new int?(),
                AMOPCustomerId = !rdr.IsDBNull(14) ? int.Parse(rdr["AMOPCustomerId"].ToString()) : new int?(),
                SessionId = long.Parse(rdr["OptimizationSessionId"].ToString()),
                CustomerBillingPeriodId = rdr.NullableIntFromReader(columns, CommonColumnNames.CustomerBillingPeriodId),
                CustomerBillInAdvanceBillingPeriodId = rdr.NullableIntFromReader(columns, CommonColumnNames.CustomerBillInAdvanceBillingPeriodId),
                ServiceProviderIds = rdr.StringFromReader(columns, CommonColumnNames.ServiceProviderIds)
            };
        }

        public List<OptimizationCommGroup> GetCommGroups(KeySysLambdaContext context, long instanceId)
        {
            LogInfo(context, "SUB", $"GetCommGroups({instanceId})");
            List<OptimizationCommGroup> commGroups = new List<OptimizationCommGroup>();
            using (var Conn = new SqlConnection(context.ConnectionString))
            {
                using (var Cmd = new SqlCommand("SELECT Id, InstanceId FROM OptimizationCommGroup WHERE InstanceId = @instanceId", Conn))
                {
                    Cmd.CommandType = CommandType.Text;
                    Cmd.Parameters.AddWithValue("@instanceId", instanceId);
                    Conn.Open();

                    SqlDataReader rdr = Cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        var commGroup = CommGroupFromReader(rdr);
                        commGroups.Add(commGroup);
                    }

                    Conn.Close();
                }
            }

            return commGroups;
        }

        private OptimizationCommGroup CommGroupFromReader(SqlDataReader rdr)
        {
            return new OptimizationCommGroup()
            {
                Id = long.Parse(rdr["Id"].ToString()),
                InstanceId = long.Parse(rdr["InstanceId"].ToString())
            };
        }

        public OptimizationQueue GetQueue(KeySysLambdaContext context, long queueId)
        {
            LogInfo(context, "SUB", $"GetQueue({queueId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                OptimizationQueue queue = new OptimizationQueue();
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("SELECT Id, InstanceId, CommPlanGroupId, ServiceProviderId, UsesProration, IsBillInAdvance, RunStatusId FROM OptimizationQueue WHERE Id = @queueId", conn))
                    {
                        cmd.CommandTimeout = 90;
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@queueId", queueId);
                        conn.Open();

                        SqlDataReader rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            queue = QueueFromReader(rdr);
                        }

                        conn.Close();
                    }
                }

                return queue;
            });
        }

        public OptimizationQueue GetBillInAdvanceQueueFromInstance(KeySysLambdaContext context, long instanceId)
        {
            LogInfo(context, "SUB", $"GetQueue({instanceId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                OptimizationQueue queue = new OptimizationQueue();
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("SELECT Id, InstanceId, CommPlanGroupId, ServiceProviderId, UsesProration, IsBillInAdvance, RunStatusId FROM OptimizationQueue WHERE IsBillInAdvance = 1 AND InstanceId = @instanceId", conn))
                    {
                        cmd.CommandTimeout = 90;
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@instanceId", instanceId);
                        conn.Open();

                        SqlDataReader rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            queue = QueueFromReader(rdr);
                        }

                        conn.Close();
                    }
                }

                return queue;
            });
        }

        private OptimizationQueue QueueFromReader(SqlDataReader rdr)
        {
            int serviceProviderIdIndex = rdr.GetOrdinal("ServiceProviderId");
            int? serviceProviderId = null;
            if (!rdr.IsDBNull(serviceProviderIdIndex))
            {
                serviceProviderId = rdr.GetInt32(serviceProviderIdIndex);
            }

            return new OptimizationQueue()
            {
                Id = long.Parse(rdr["Id"].ToString()),
                InstanceId = long.Parse(rdr["InstanceId"].ToString()),
                CommPlanGroupId = long.Parse(rdr["CommPlanGroupId"].ToString()),
                ServiceProviderId = serviceProviderId,
                UsesProration = rdr.GetBoolean("UsesProration"),
                IsBillInAdvance = rdr.GetBoolean("IsBillInAdvance"),
                RunStatusId = (OptimizationStatus)rdr.GetInt32("RunStatusId"),
            };
        }

        public long CreateCommPlanGroup(KeySysLambdaContext context, long instanceId)
        {
            LogInfo(context, "SUB", "CreateCommPlanGroup");
            long commPlanGroupId = 0;

            using (var Conn = new SqlConnection(context.ConnectionString))
            {
                using (var Cmd = new SqlCommand("UPDATE OptimizationInstance SET RunStatusId = @runStatusId, RunStartTime = GETUTCDATE() WHERE Id = @id AND RunStatusId <> @runStatusId", Conn))
                {
                    Cmd.CommandType = CommandType.Text;
                    Cmd.Parameters.AddWithValue("@id", instanceId);
                    Cmd.Parameters.AddWithValue("@runStatusId", OptimizationStatus.CommGroupSetup);
                    Conn.Open();

                    Cmd.ExecuteNonQuery();

                    Conn.Close();
                }

                using (var Cmd = new SqlCommand("INSERT INTO OptimizationCommGroup(InstanceId, CreatedBy, CreatedDate, IsDeleted) VALUES(@instanceId,'System', GETUTCDATE(), 0); SELECT @id = SCOPE_IDENTITY();", Conn))
                {
                    Cmd.CommandType = CommandType.Text;
                    Cmd.Parameters.Add("@id", SqlDbType.BigInt);
                    Cmd.Parameters["@id"].Direction = ParameterDirection.Output;
                    Cmd.Parameters.AddWithValue("@instanceId", instanceId);
                    Conn.Open();

                    Cmd.ExecuteNonQuery();

                    commPlanGroupId = long.Parse(Cmd.Parameters["@id"].Value.ToString());

                    Conn.Close();
                }
            }

            return commPlanGroupId;
        }

        public List<string> GetCommPlansForCommGroup(KeySysLambdaContext context, long commGroupId)
        {
            LogInfo(context, "SUB", $"GetCommPlansForCommGroup({commGroupId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                List<string> commPlans = new List<string>();
                using (var Conn = new SqlConnection(context.ConnectionString))
                {
                    using (var Cmd = new SqlCommand("SELECT jcp.[Id],jcp.[CommunicationPlanName] FROM [dbo].[OptimizationCommGroup_CommPlan] ocgcp INNER JOIN [dbo].[JasperCommunicationPlan] jcp ON ocgcp.CommPlanId = jcp.Id WHERE ocgcp.CommGroupId = @commGroupId AND jcp.IsDeleted = 0", Conn))
                    {
                        Cmd.CommandType = CommandType.Text;
                        Cmd.Parameters.AddWithValue("@commGroupId", commGroupId);
                        Conn.Open();

                        SqlDataReader rdr = Cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            var commPlan = rdr["CommunicationPlanName"].ToString();
                            commPlans.Add(commPlan);
                        }

                        Conn.Close();
                    }
                }

                return commPlans;
            });
        }

        public List<CommPlan> GetCommPlans(KeySysLambdaContext context, int serviceProviderId)
        {
            LogInfo(context, "SUB", "GetCommPlans");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                List<CommPlan> commPlans = new List<CommPlan>();
                using (var Conn = new SqlConnection(context.ConnectionString))
                {
                    using (var Cmd = new SqlCommand("SELECT Id, CommunicationPlanName, RatePlanIds FROM vwJasperCommPlan_RatePlan WHERE ServiceProviderId = @ServiceProviderId", Conn))
                    {
                        Cmd.CommandType = CommandType.Text;
                        Cmd.Parameters.AddWithValue("@ServiceProviderId", serviceProviderId);
                        Conn.Open();

                        SqlDataReader rdr = Cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            var commPlan = CommPlanFromReader(rdr);
                            commPlans.Add(commPlan);
                        }

                        Conn.Close();
                    }
                }

                return commPlans;
            });
        }

        private CommPlan CommPlanFromReader(SqlDataReader rdr)
        {
            return new CommPlan()
            {
                Id = int.Parse(rdr["Id"].ToString()),
                CommunicationPlanName = rdr["CommunicationPlanName"].ToString(),
                RatePlanIds = rdr["RatePlanIds"].ToString()
            };
        }

        public DataTable AddCarrierRatePlansToCommPlanGroup(KeySysLambdaContext context, long instanceId, long commPlanGroupId, List<RatePlan> plans)
        {
            LogInfo(context, "SUB", "AddCarrierRatePlansToCommPlanGroup");

            DataTable table = new DataTable();
            table.Columns.Add("InstanceId", typeof(long));
            table.Columns.Add("CommGroupId", typeof(long));
            table.Columns.Add("CarrierRatePlanId", typeof(int));
            table.Columns.Add("CustomerRatePlanId", typeof(int));
            table.Columns.Add("MaxAvgUsage", typeof(decimal));
            table.Columns.Add("CreatedBy");
            table.Columns.Add("CreatedDate", typeof(DateTime));

            foreach (RatePlan plan in plans)
            {
                var dr = table.NewRow();

                dr[0] = instanceId;
                dr[1] = commPlanGroupId;
                dr[2] = plan.Id;
                dr[3] = DBNull.Value;
                dr[4] = plan.MaxAvgUsage;
                dr[5] = "System";
                dr[6] = DateTime.UtcNow;

                table.Rows.Add(dr);
            }

            List<SqlBulkCopyColumnMapping> columnMappings = new List<SqlBulkCopyColumnMapping>()
            {
                new SqlBulkCopyColumnMapping("InstanceId", "InstanceId"),
                new SqlBulkCopyColumnMapping("CommGroupId", "CommGroupId"),
                new SqlBulkCopyColumnMapping("CarrierRatePlanId", "CarrierRatePlanId"),
                new SqlBulkCopyColumnMapping("CustomerRatePlanId", "CustomerRatePlanId"),
                new SqlBulkCopyColumnMapping("MaxAvgUsage", "MaxAvgUsage"),
                new SqlBulkCopyColumnMapping("CreatedBy", "CreatedBy"),
                new SqlBulkCopyColumnMapping("CreatedDate", "CreatedDate")
            };

            var logMessage = SqlHelper.SqlBulkCopy(context.ConnectionString, table, "OptimizationCommGroup_RatePlan", columnMappings);
            LogInfo(context, logMessage);

            // select comm plan group rate plan records for comm group id from db
            DataTable commGroupRatePlanTable = new DataTable();
            using (var connection = new SqlConnection(context.ConnectionString))
            {
                using (var cmd = new SqlCommand("SELECT Id, InstanceId, CommGroupId, CarrierRatePlanId, CustomerRatePlanId, MaxAvgUsage, CreatedBy, CreatedDate FROM OptimizationCommGroup_RatePlan WHERE CommGroupId = @CommGroupId", connection))
                {
                    cmd.Parameters.AddWithValue("@CommGroupId", commPlanGroupId);

                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        commGroupRatePlanTable.Load(reader);
                    }
                }
            }

            return commGroupRatePlanTable;
        }

        public DataTable AddCustomerRatePlansToCommPlanGroup(KeySysLambdaContext context, long instanceId, long commPlanGroupId, List<RatePlan> plans)
        {
            LogInfo(context, "SUB", "AddCustomerRatePlansToCommPlanGroup");

            DataTable table = new DataTable();
            table.Columns.Add("InstanceId", typeof(long));
            table.Columns.Add("CommGroupId", typeof(long));
            table.Columns.Add("CarrierRatePlanId", typeof(int));
            table.Columns.Add("CustomerRatePlanId", typeof(int));
            table.Columns.Add("MaxAvgUsage", typeof(decimal));
            table.Columns.Add("CreatedBy");
            table.Columns.Add("CreatedDate", typeof(DateTime));

            foreach (RatePlan plan in plans)
            {
                var dr = table.NewRow();

                dr[0] = instanceId;
                dr[1] = commPlanGroupId;
                dr[2] = DBNull.Value;
                dr[3] = plan.Id;
                dr[4] = plan.MaxAvgUsage;
                dr[5] = "System";
                dr[6] = DateTime.UtcNow;

                table.Rows.Add(dr);
            }

            List<SqlBulkCopyColumnMapping> columnMappings = new List<SqlBulkCopyColumnMapping>()
            {
                new SqlBulkCopyColumnMapping("InstanceId", "InstanceId"),
                new SqlBulkCopyColumnMapping("CommGroupId", "CommGroupId"),
                new SqlBulkCopyColumnMapping("CarrierRatePlanId", "CarrierRatePlanId"),
                new SqlBulkCopyColumnMapping("CustomerRatePlanId", "CustomerRatePlanId"),
                new SqlBulkCopyColumnMapping("MaxAvgUsage", "MaxAvgUsage"),
                new SqlBulkCopyColumnMapping("CreatedBy", "CreatedBy"),
                new SqlBulkCopyColumnMapping("CreatedDate", "CreatedDate")
            };

            var logMessage = SqlHelper.SqlBulkCopy(context.ConnectionString, table, "OptimizationCommGroup_RatePlan", columnMappings);
            LogInfo(context, logMessage);

            // select comm plan group rate plan records for comm group id from db
            DataTable commGroupRatePlanTable = new DataTable();
            using (var connection = new SqlConnection(context.ConnectionString))
            {
                using (var cmd = new SqlCommand("SELECT Id, InstanceId, CommGroupId, CarrierRatePlanId, CustomerRatePlanId, MaxAvgUsage, CreatedBy, CreatedDate FROM OptimizationCommGroup_RatePlan WHERE CommGroupId = @CommGroupId", connection))
                {
                    cmd.Parameters.AddWithValue("@CommGroupId", commPlanGroupId);

                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        commGroupRatePlanTable.Load(reader);
                    }
                }
            }

            return commGroupRatePlanTable;
        }

        public List<RatePlan> GetQueueRatePlans(KeySysLambdaContext context, long queueId)
        {
            LogInfo(context, "SUB", $"GetQueueRatePlans({queueId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                List<RatePlan> ratePlans = new List<RatePlan>();
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("SELECT ISNULL(jcust_rp.[Id], jcrp.[Id]) AS [Id],ISNULL(jcust_rp.[RatePlanCode], [jcrp].[RatePlanCode]) AS [RatePlanCode],ISNULL(jcust_rp.[DisplayRate], jcrp.[DisplayRate]) AS [DisplayRate],ISNULL(jcust_rp.[PlanMB], jcrp.[PlanMB]) AS [PlanMB],ISNULL(jcust_rp.[OverageRateCost], jcrp.[OverageRateCost]) AS [OverageRateCost],ISNULL(jcust_rp.[BaseRatePerMB],jcrp.[BaseRatePerMB]) AS [BaseRatePerMB],ISNULL(jcust_rp.[3GSurcharge], jcrp.[3GSurcharge]) AS [3GSurcharge],jcust_rp.[MinPlanDataMB],jcust_rp.[MaxPlanDataMB],jcust_rp.[RatePlanName] AS [PlanDisplayName], ISNULL(jcust_rp.[CreatedBy],jcrp.[CreatedBy]) AS [CreatedBy],ISNULL(jcust_rp.[CreatedDate],jcrp.[CreatedDate]) AS [CreatedDate], ISNULL(jcust_rp.DataPerOverageCharge, jcrp.DataPerOverageCharge) AS DataPerOverageCharge, ISNULL(jcust_rp.AllowsSimPooling, 1) AS AllowsSimPooling, ISNULL(jcust_rp.IsBillInAdvanceEligible, 0) AS IsBillInAdvanceEligible, ISNULL(jcust_rp.SmsRate, 0.0) AS SmsRate, ISNULL(jcust_rp.BaseRate, 0.0) AS BaseRate, ISNULL(jcust_rp.RateChargeAmt, jcrp.[DisplayRate]) AS RateChargeAmt FROM [dbo].[OptimizationQueue_RatePlan] oqrp INNER JOIN [dbo].[OptimizationCommGroup_RatePlan] ocgrp ON oqrp.CommGroup_RatePlanId = ocgrp.Id LEFT JOIN [dbo].[JasperCarrierRatePlan] jcrp ON ocgrp.CarrierRatePlanId = jcrp.Id LEFT JOIN [dbo].[JasperCustomerRatePlan] jcust_rp ON ocgrp.CustomerRatePlanId = jcust_rp.Id WHERE oqrp.QueueId = @queueId AND ISNULL(jcust_rp.IsDeleted, jcrp.IsDeleted) = 0 ORDER BY oqrp.SequenceOrder", conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@queueId", queueId);
                        conn.Open();

                        SqlDataReader rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            var ratePlan = RatePlanFromReader(rdr);
                            ratePlans.Add(ratePlan);
                        }

                        conn.Close();
                    }
                }

                return ratePlans;
            });
        }

        public List<RatePlan> GetRatePlans(KeySysLambdaContext context, int serviceProviderId)
        {
            LogInfo(context, "SUB", "GetRatePlans");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                var ratePlans = new List<RatePlan>();
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("SELECT [Id],[RatePlanCode],[DisplayRate],[PlanMB],[OverageRateCost] as [OverageRateCost],[BaseRatePerMB],[3GSurcharge],NULL AS [MinPlanDataMB],NULL AS [MaxPlanDataMB],[RatePlanCode] AS [PlanDisplayName],[CreatedBy],[CreatedDate],[DataPerOverageCharge],[AllowsSimPooling], 0 AS IsBillInAdvanceEligible, 0.0 AS SmsRate, 0.0 AS BaseRate, [DisplayRate] AS RateChargeAmt FROM [dbo].[JasperCarrierRatePlan] WHERE IsDeleted = 0 AND ServiceProviderId = @serviceProviderId", conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId);
                        conn.Open();

                        SqlDataReader rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            var ratePlan = RatePlanFromReader(rdr);
                            ratePlans.Add(ratePlan);
                        }

                        conn.Close();
                    }
                }

                return ratePlans;
            });
        }

        public List<RatePlan> GetCrossCustomerRatePlans(KeySysLambdaContext context, List<int> additionalRatePlansIds)
        {
            LogInfo(context, LogTypeConstant.Sub, $"({string.Join(',', additionalRatePlansIds)})");

            var ratePlans = new List<RatePlan>();
            if (additionalRatePlansIds.Count == 0)
            {
                return ratePlans;
            }

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand(@"SELECT [Id],
                                                                    [RatePlanCode],
                                                                    [DisplayRate],
                                                                    [PlanMB],
                                                                    [OverageRateCost],
                                                                    [BaseRatePerMB],
                                                                    ""3GSurcharge"",
                                                                    [MinPlanDataMB],
                                                                    [MaxPlanDataMB],
                                                                    [RatePlanName] AS [PlanDisplayName],
                                                                    [CreatedBy],
                                                                    [CreatedDate],
                                                                    [DataPerOverageCharge],
                                                                    [AllowsSimPooling],
                                                                    [IsBillInAdvanceEligible],
                                                                    [SmsRate],
                                                                    [BaseRate],
                                                                    [RateChargeAmt],
                                                                    [AutoChangeRatePlan]
                                                                FROM JasperCustomerRatePlan
                                                                WHERE [AutoChangeRatePlan] = 0 AND [Id] IN (@RatePlanIds)", conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandTimeout = SQLConstant.TimeoutSeconds;
                        cmd.AddArrayParameters("@RatePlanIds", additionalRatePlansIds);
                        conn.Open();

                        SqlDataReader dataReader = cmd.ExecuteReader();
                        while (dataReader.Read())
                        {
                            var ratePlan = RatePlanFromReader(dataReader);
                            ratePlans.Add(ratePlan);
                        }

                        conn.Close();
                    }
                }

                return ratePlans;
            });

        }

        public List<RatePlan> GetNonRetiredRatePlans(KeySysLambdaContext context, int serviceProviderId)
        {
            LogInfo(context, "SUB", "GetNonRetiredRatePlans");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                var ratePlans = new List<RatePlan>();
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("SELECT [Id],[RatePlanCode],[DisplayRate],[PlanMB],[OverageRateCost] as [OverageRateCost],[BaseRatePerMB],[3GSurcharge],NULL AS [MinPlanDataMB],NULL AS [MaxPlanDataMB],[RatePlanCode] AS [PlanDisplayName],[CreatedBy],[CreatedDate],[DataPerOverageCharge],[AllowsSimPooling], 0 AS IsBillInAdvanceEligible, 0.0 AS SmsRate, 0.0 AS BaseRate, [DisplayRate] AS RateChargeAmt FROM [dbo].[JasperCarrierRatePlan] WHERE IsDeleted = 0 AND IsRetired = 0 AND ServiceProviderId = @serviceProviderId", conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId);
                        conn.Open();

                        SqlDataReader rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            var ratePlan = RatePlanFromReader(rdr);
                            ratePlans.Add(ratePlan);
                        }

                        conn.Close();
                    }
                }

                return ratePlans;
            });
        }

        public string GetRevAccountNumber(KeySysLambdaContext context, Guid customerId)
        {
            LogInfo(context, "SUB", $"GetRevAccountNumber({customerId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                var revAccountNumber = string.Empty;
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("SELECT RevCustomerId FROM RevCustomer rc WHERE rc.Id = @customerId", conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@customerId", customerId);
                        conn.Open();

                        SqlDataReader rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            revAccountNumber = rdr[0].ToString();
                        }

                        conn.Close();
                    }
                }

                return revAccountNumber;
            });
        }

        public List<int> GetCustomerServiceProviders(KeySysLambdaContext context, Guid customerId, PortalTypes portalType)
        {
            LogInfo(context, "SUB", $"GetCustomerServiceProviders({customerId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                var serviceProviders = new List<int>();
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("usp_OptimizationServiceProvidersByCustomer", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@RevCustomerId", customerId);
                        cmd.Parameters.AddWithValue("@PortalType", (int)portalType);
                        conn.Open();

                        var rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            serviceProviders.Add(int.Parse(rdr["ServiceProviderId"].ToString()));
                        }

                        conn.Close();
                    }
                }

                return serviceProviders;
            });
        }

        public List<RatePlan> GetCustomerRatePlans(KeySysLambdaContext context, Guid customerId, int billingPeriodId, int? serviceProviderId, int tenantId, SiteTypes siteType = SiteTypes.Rev, int? AMOPCustomerId = null)
        {
            LogInfo(context, "SUB", $"GetCustomerRatePlans({customerId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                var ratePlans = new List<RatePlan>();
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("usp_OptimizationRatePlansByCustomer", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@RevCustomerIds", customerId.ToString());
                        cmd.Parameters.AddWithValue("@ServiceProviderId", serviceProviderId.HasValue ? (object)serviceProviderId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@TenantId", tenantId);
                        cmd.Parameters.AddWithValue("@SiteType", (int)siteType);
                        cmd.Parameters.AddWithValue("@AMOPCustomerIds", AMOPCustomerId.HasValue ? (object)AMOPCustomerId.ToString() : DBNull.Value);
                        cmd.Parameters.AddWithValue("@BillingPeriodId", billingPeriodId);
                        conn.Open();

                        var rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            var ratePlan = RatePlanFromReader(rdr);
                            ratePlans.Add(ratePlan);
                        }
                    }
                }

                return ratePlans;
            });
        }

        public List<RatePlan> GetMobilityCustomerRatePlans(KeySysLambdaContext context, Guid customerId, int billingPeriodId, int? serviceProviderId, int tenantId, SiteTypes siteType = SiteTypes.Rev, int? AMOPCustomerId = null)
        {
            LogInfo(context, "SUB", $"GetMobilityCustomerRatePlans({customerId},{serviceProviderId},{tenantId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                List<RatePlan> ratePlans = new List<RatePlan>();
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("usp_OptimizationMobilityRatePlansByCustomer", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@RevCustomerIds", customerId != Guid.Empty ? (object)customerId.ToString() : DBNull.Value);
                        cmd.Parameters.AddWithValue("@ServiceProviderId", serviceProviderId.HasValue ? (object)serviceProviderId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@TenantId", tenantId);
                        cmd.Parameters.AddWithValue("@SiteType", (int)siteType);
                        cmd.Parameters.AddWithValue("@AMOPCustomerIds", AMOPCustomerId.HasValue ? (object)AMOPCustomerId.ToString() : DBNull.Value);
                        cmd.Parameters.AddWithValue("@BillingPeriodId", billingPeriodId);
                        conn.Open();

                        SqlDataReader rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            var ratePlan = RatePlanFromReader(rdr);
                            ratePlans.Add(ratePlan);
                        }

                        conn.Close();
                    }
                }

                return ratePlans;
            });
        }

        private RatePlan RatePlanFromReader(SqlDataReader dataReader)
        {
            var columns = Enumerable.Range(0, dataReader.FieldCount).Select(dataReader.GetName).ToList();
            var allowsSimPooling = dataReader[CustomerRatePlanColumnNames.AllowsSimPooling].ToString();
            var isBillInAdvanceEligible = dataReader[CustomerRatePlanColumnNames.IsBillInAdvanceEligible].ToString();

            var ratePlan = new RatePlan()
            {
                Id = int.Parse(dataReader[CustomerRatePlanColumnNames.Id].ToString()),
                PlanName = dataReader[CustomerRatePlanColumnNames.RatePlanCode].ToString(),
                BaseUsageRate = decimal.Parse(dataReader[CustomerRatePlanColumnNames.DisplayRate].ToString()),
                BaseUsageMb = decimal.Parse(dataReader[CustomerRatePlanColumnNames.PlanMB].ToString()),
                OverageRate = decimal.Parse(dataReader[CustomerRatePlanColumnNames.OverageRateCost].ToString()),
                DataPerOverageCharge = decimal.Parse(dataReader[CustomerRatePlanColumnNames.DataPerOverageCharge].ToString()),
                AllowsSimPooling = FormatHelper.ToBoolean(allowsSimPooling),
                IsBillInAdvanceEligible = FormatHelper.ToBoolean(isBillInAdvanceEligible)
            };

            if (!columns.Contains(CustomerRatePlanColumnNames.MinPlanDataMB)
                || dataReader.IsDBNull(CustomerRatePlanColumnNames.MinPlanDataMB))
            {
                ratePlan.MinPlanDataMb = null;
            }
            else
            {
                var value = dataReader[CustomerRatePlanColumnNames.MinPlanDataMB].ToString();
                ratePlan.MinPlanDataMb = decimal.Parse(value);
            }

            if (!columns.Contains(CustomerRatePlanColumnNames.MaxPlanDataMB)
                || dataReader.IsDBNull(CustomerRatePlanColumnNames.MaxPlanDataMB))
            {
                ratePlan.MaxPlanDataMb = null;
            }
            else
            {
                ratePlan.MaxPlanDataMb = decimal.Parse(dataReader[CustomerRatePlanColumnNames.MaxPlanDataMB].ToString());
            }

            if (!columns.Contains(CustomerRatePlanColumnNames.PlanDisplayName)
                || dataReader.IsDBNull(CustomerRatePlanColumnNames.PlanDisplayName))
            {
                ratePlan.PlanDisplayName = ratePlan.PlanName;
            }
            else
            {
                ratePlan.PlanDisplayName = dataReader[CustomerRatePlanColumnNames.PlanDisplayName].ToString();
            }

            if (!columns.Contains(CustomerRatePlanColumnNames.SmsRate)
                || dataReader.IsDBNull(CustomerRatePlanColumnNames.SmsRate))
            {
                ratePlan.SmsRate = 0;
            }
            else
            {
                ratePlan.SmsRate = dataReader.GetDecimal(CustomerRatePlanColumnNames.SmsRate);
            }

            if (!columns.Contains(CustomerRatePlanColumnNames.BaseRate)
                || dataReader.IsDBNull(CustomerRatePlanColumnNames.BaseRate))
            {
                ratePlan.BaseRate = 0;
            }
            else
            {
                ratePlan.BaseRate = dataReader.GetDecimal(CustomerRatePlanColumnNames.BaseRate);
            }

            if (!columns.Contains(CustomerRatePlanColumnNames.RateChargeAmt)
                || dataReader.IsDBNull(CustomerRatePlanColumnNames.RateChargeAmt))
            {
                ratePlan.RateCharge = 0;
            }
            else
            {
                ratePlan.RateCharge = dataReader.GetDecimal(CustomerRatePlanColumnNames.RateChargeAmt);
            }

            if (!columns.Contains(CustomerRatePlanColumnNames.AutoChangeRatePlan)
                || dataReader.IsDBNull(CustomerRatePlanColumnNames.AutoChangeRatePlan))
            {
                ratePlan.AutoChangeRatePlan = false;
            }
            else
            {
                ratePlan.AutoChangeRatePlan = dataReader.GetBoolean(CustomerRatePlanColumnNames.AutoChangeRatePlan);
            }

            return ratePlan;
        }

        public long CreateQueue(KeySysLambdaContext context, long instanceId, long commPlanGroupId, int? serviceProviderId, bool usesProration, bool IsBillInAdvance = false)
        {
            LogInfo(context, "SUB", $"CreateQueue(,{instanceId},{commPlanGroupId},{serviceProviderId},{usesProration})");
            if ((serviceProviderId == null || serviceProviderId <= 0) && !IsCrossProviderOptimization)
            {
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("SELECT ServiceProviderId FROM OptimizationInstance oi WHERE oi.id = @instanceId", conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@instanceId", instanceId);
                        conn.Open();

                        SqlDataReader rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            serviceProviderId = int.Parse(rdr[0].ToString());
                        }

                        conn.Close();
                    }
                }
            }

            long queueId = 0;

            using (var conn = new SqlConnection(context.ConnectionString))
            {
                using (var cmd = new SqlCommand("INSERT INTO OptimizationQueue(InstanceId, CommPlanGroupId, RunStatusId, ServiceProviderId, UsesProration, CreatedBy, CreatedDate, IsDeleted, IsBillInAdvance) VALUES(@instanceId, @commPlanGroupId, @runStatusId, @serviceProviderId, @usesProration, 'System', GETUTCDATE(), 0, @IsBillInAdvance); SELECT @queueId = SCOPE_IDENTITY()", conn))
                {
                    cmd.CommandType = CommandType.Text;
                    cmd.Parameters.Add("@queueId", SqlDbType.BigInt);
                    cmd.Parameters["@queueId"].Direction = ParameterDirection.Output;
                    cmd.Parameters.AddWithValue("@instanceId", instanceId);
                    cmd.Parameters.AddWithValue("@commPlanGroupId", commPlanGroupId);
                    cmd.Parameters.AddWithValue("@runStatusId", OptimizationStatus.NotStarted);
                    cmd.Parameters.AddWithValue("@IsBillInAdvance", IsBillInAdvance);
                    if (serviceProviderId == null)
                    {
                        cmd.Parameters.AddWithValue("@serviceProviderId", DBNull.Value);
                    }
                    else
                    {
                        cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId);
                    }
                    cmd.Parameters.AddWithValue("@usesProration", usesProration);

                    conn.Open();

                    cmd.ExecuteNonQuery();

                    queueId = long.Parse(cmd.Parameters["@queueId"].Value.ToString());

                    conn.Close();
                }
            }
            LogInfo(context, "INFO", $"Queue Created: {queueId}");

            return queueId;
        }

        public DataTable AddRatePlansToQueue(long queueId, RatePlanSequence ratePoolSequence, DataTable commGroupRatePlanTable)
        {
            DataTable table = new DataTable();
            table.Columns.Add("QueueId", typeof(long));
            table.Columns.Add("CommGroup_RatePlanId", typeof(long));
            table.Columns.Add("SequenceOrder", typeof(int));
            table.Columns.Add("CreatedBy");
            table.Columns.Add("CreatedDate", typeof(DateTime));

            long commPlanGroup_RatePlanId = 0;
            int sequenceOrder = 0;
            foreach (int planId in ratePoolSequence.RatePlanIds)
            {
                commPlanGroup_RatePlanId = 0;
                foreach (DataRow commGroupRatePlanRow in commGroupRatePlanTable.Rows)
                {
                    if ((!commGroupRatePlanRow.IsNull(3) && (int)commGroupRatePlanRow[3] == planId) || (!commGroupRatePlanRow.IsNull(4) && (int)commGroupRatePlanRow[4] == planId))
                    {
                        commPlanGroup_RatePlanId = (long)commGroupRatePlanRow[0];
                        break;
                    }
                }

                if (commPlanGroup_RatePlanId != 0)
                {
                    var dr = table.NewRow();

                    dr[0] = queueId;
                    dr[1] = commPlanGroup_RatePlanId;
                    dr[2] = sequenceOrder;
                    dr[3] = "System";
                    dr[4] = DateTime.UtcNow;

                    table.Rows.Add(dr);
                }

                sequenceOrder++;
            }

            return table;
        }

        public void CreateQueueRatePlans(KeySysLambdaContext context, DataTable table)
        {
            LogInfo(context, "SUB", "Start CreateQueueRatePlans");

            List<SqlBulkCopyColumnMapping> columnMappings = new List<SqlBulkCopyColumnMapping>()
            {
                new SqlBulkCopyColumnMapping("QueueId", "QueueId"),
                new SqlBulkCopyColumnMapping("CommGroup_RatePlanId", "CommGroup_RatePlanId"),
                new SqlBulkCopyColumnMapping("SequenceOrder", "SequenceOrder"),
                new SqlBulkCopyColumnMapping("CreatedBy", "CreatedBy"),
                new SqlBulkCopyColumnMapping("CreatedDate", "CreatedDate")
            };

            var logMessage = SqlHelper.SqlBulkCopy(context.ConnectionString, table, "OptimizationQueue_RatePlan", columnMappings);
            LogInfo(context, logMessage);
        }

        public List<SimCard> GetSimCards(KeySysLambdaContext context, long instanceId, int? serviceProviderId, List<string> commPlanNames, BillingPeriod billingPeriod, long commGroupId, bool isCustomerOptimization, bool autoChangeRatePlan = true)
        {
            LogInfo(context, LogTypeConstant.Sub, $"instanceId:{instanceId},serviceProviderId:{serviceProviderId},commPlanNames.Count:{commPlanNames.Count},billingPeriod.Id:{billingPeriod.Id},autoChangeRatePlan:{autoChangeRatePlan}");
            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                List<SimCard> simCards = new List<SimCard>();
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand(@"SELECT [Id],
                                                        [InstanceId],
                                                        [CycleDataUsageMB],
                                                        [ProjectedDataUsageMB],
                                                        [CommunicationPlan],
                                                        [MSISDN],
                                                        [ICCID],
                                                        [UsageDate],
                                                        [CreatedBy],
                                                        [CreatedDate],
                                                        [AmopDeviceId],
                                                        [ServiceProviderId],
                                                        [DateActivated],
                                                        [SmsUsage],
                                                        [RatePlanCode]
                                                    FROM OptimizationDevice 
                                                    WHERE [InstanceId] = @instanceId 
                                                            AND (@CommGroupId IS NULL
                                                                OR [OptimizationCommGroupId] = @CommGroupId)
                                                            AND (@serviceProviderId IS NULL OR [ServiceProviderId] = @serviceProviderId)
                                                            AND [AutoChangeRatePlan] = @autoChangeRatePlan", conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@instanceId", instanceId);
                        if (serviceProviderId != null)
                        {
                            cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@serviceProviderId", DBNull.Value);
                        }
                        cmd.Parameters.AddWithValue("@autoChangeRatePlan", autoChangeRatePlan);

                        if (commGroupId == 0)
                        {
                            cmd.Parameters.AddWithValue(CommonSQLParameterNames.COMM_GROUP_ID, DBNull.Value);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue(CommonSQLParameterNames.COMM_GROUP_ID, commGroupId);
                        }

                        conn.Open();

                        var rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            var simCard = SimCardFromReader(rdr);

                            simCard.WasActivatedInThisBillingPeriod = DateIsInBillingPeriod(simCard.DateActivated, billingPeriod);
                            if (simCard.WasActivatedInThisBillingPeriod)
                            {
                                simCard.DaysActivatedInBillingPeriod = DaysLeftInBillingPeriod(simCard.DateActivated, billingPeriod);
                            }
                            else
                            {
                                simCard.DaysActivatedInBillingPeriod = billingPeriod.DaysInBillingPeriod;
                            }
                            // As we don't have communication plans in customer optimization, set to empty
                            if (isCustomerOptimization)
                            {
                                simCard.CommunicationPlan = string.Empty;
                            }
                            if (commPlanNames == null || commPlanNames.Count == 0 || commPlanNames.Contains(simCard.CommunicationPlan))
                            {
                                simCards.Add(simCard);
                            }
                        }
                    }
                }

                return simCards;
            });
        }

        private SimCard SimCardFromReader(SqlDataReader rdr)
        {
            var columns = rdr.GetColumnsFromReader();
            return new SimCard()
            {
                Id = int.Parse(rdr["AmopDeviceId"].ToString()),
                ICCID = rdr["ICCID"].ToString(),
                CommunicationPlan = rdr["CommunicationPlan"].ToString(),
                CycleDataUsageMB = Math.Round(decimal.Parse(rdr["ProjectedDataUsageMB"].ToString()), 3), // round usage
                MSISDN = rdr["MSISDN"].ToString(),
                DateActivated = !rdr.IsDBNull("DateActivated") ? new DateTime?(rdr.GetDateTime("DateActivated")) : null,
                SmsUsage = !rdr.IsDBNull("SmsUsage") ? rdr.GetInt64("SmsUsage") : 0,
                RatePlanCode = rdr.StringFromReader(columns, CommonColumnNames.RatePlanCode),
            };
        }

        public List<vwOptimizationSimCard> GetOptimizationSimCards(KeySysLambdaContext context, List<string> commPlanNames, int? serviceProviderId, string revAccountNumber, int? integrationAuthenticationId, int billingPeriodId, int tenantId, SiteTypes siteType = SiteTypes.Rev, int? amopCustomerId = null, List<int?> poolIds = null)
        {
            LogInfo(context, LogTypeConstant.Sub,
                $"([{string.Join(",", commPlanNames ?? new List<string>())}],{serviceProviderId},{revAccountNumber},{integrationAuthenticationId},{billingPeriodId}, {nameof(tenantId)}: {tenantId}, {siteType},{amopCustomerId},{poolIds?.Count})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                var simCards = new List<vwOptimizationSimCard>();
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("usp_OptimizationSimCardsGet", conn))
                    {
                        cmd.CommandTimeout = 200;
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@BillingPeriodId", billingPeriodId);
                        cmd.Parameters.AddWithValue("@SiteType", (int)siteType);
                        cmd.Parameters.AddWithValue("@AMOPCustomerId", amopCustomerId);
                        cmd.Parameters.AddWithValue(CommonSQLParameterNames.TENANT_ID, tenantId);

                        if (string.IsNullOrWhiteSpace(revAccountNumber))
                        {
                            cmd.Parameters.AddWithValue("@RevAccountNumber", DBNull.Value);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@RevAccountNumber", revAccountNumber);
                        }

                        if (serviceProviderId == null)
                        {
                            cmd.Parameters.AddWithValue("@ServiceProviderId", DBNull.Value);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@ServiceProviderId", serviceProviderId.Value);
                        }

                        if (integrationAuthenticationId == null)
                        {
                            cmd.Parameters.AddWithValue("@IntegrationAuthenticationId", DBNull.Value);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@IntegrationAuthenticationId", integrationAuthenticationId);
                        }

                        if (poolIds == null)
                        {
                            cmd.Parameters.AddWithValue("@PoolIds", DBNull.Value);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@PoolIds", string.Join(',', poolIds));
                        }

                        conn.Open();

                        var rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            var simCard = OptimizationSimCardFromReader(rdr);
                            if (commPlanNames == null || commPlanNames.Count == 0 || commPlanNames.Contains(simCard.CommunicationPlan))
                            {
                                simCards.Add(simCard);
                            }
                        }

                        conn.Close();
                    }
                }

                return simCards;
            });
        }

        protected vwOptimizationSimCard OptimizationSimCardFromReader(SqlDataReader dataReader)
        {
            var columns = Enumerable.Range(0, dataReader.FieldCount).Select(dataReader.GetName).ToList();
            int carrierRatePlanColumnId = dataReader.GetOrdinal(vwOptimizationSimCardColumnNames.CarrierRatePlanCode);
            int providerDateActivatedColumnId = dataReader.GetOrdinal(vwOptimizationSimCardColumnNames.ProviderDateActivated);
            int customerRatePlanColumnId = dataReader.GetOrdinal(vwOptimizationSimCardColumnNames.CustomerRatePlanId);

            var simCard = new vwOptimizationSimCard()
            {
                Id = int.Parse(dataReader[vwOptimizationSimCardColumnNames.DeviceId].ToString()),
                ICCID = dataReader[vwOptimizationSimCardColumnNames.ICCID].ToString(),
                CommunicationPlan = dataReader[vwOptimizationSimCardColumnNames.CommunicationPlan].ToString(),
                CycleDataUsageMB = dataReader.GetInt32(CommonColumnNames.IntegrationId) == (int)IntegrationType.Teal
                    ? Math.Round(long.Parse(dataReader[vwOptimizationSimCardColumnNames.CtdDataUsage].ToString()) / CommonConstants.TEAL_BYTE_CONVERSION_VALUE / CommonConstants.TEAL_BYTE_CONVERSION_VALUE, 3)
                    : Math.Round(long.Parse(dataReader[vwOptimizationSimCardColumnNames.CtdDataUsage].ToString()) / CommonConstants.DEFAULT_BYTE_CONVERSION_VALUE / CommonConstants.DEFAULT_BYTE_CONVERSION_VALUE, 3), // usage is really in bytes, must convert to MB
                MSISDN = dataReader[vwOptimizationSimCardColumnNames.MSISDN].ToString(),
                Status = dataReader[vwOptimizationSimCardColumnNames.Status].ToString(),
                UsageDate = !dataReader.IsDBNull(vwOptimizationSimCardColumnNames.UsageDate) ? DateTime.Parse(dataReader[vwOptimizationSimCardColumnNames.UsageDate].ToString()) : new DateTime?(),
                CustomerRatePlanCode = dataReader[vwOptimizationSimCardColumnNames.CustomerRatePlanCode].ToString(),
                ServiceProviderId = int.Parse(dataReader[vwOptimizationSimCardColumnNames.ServiceProviderId].ToString()),
                CarrierRatePlanCode = !dataReader.IsDBNull(carrierRatePlanColumnId) ? dataReader[carrierRatePlanColumnId].ToString() : "",
                ProviderDateActivated = !dataReader.IsDBNull(providerDateActivatedColumnId) ? DateTime.Parse(dataReader[providerDateActivatedColumnId].ToString()) : (DateTime?)null,
                SmsUsage = !dataReader.IsDBNull(vwOptimizationSimCardColumnNames.SmsUsage) ? dataReader.GetInt64(vwOptimizationSimCardColumnNames.SmsUsage) : 0,
                CustomerRatePlanId = !dataReader.IsDBNull(customerRatePlanColumnId) ? int.Parse(dataReader[customerRatePlanColumnId].ToString()) : (int?)null
            };

            if (!columns.Contains(vwOptimizationSimCardColumnNames.AccountNumberIntegrationAuthenticationId)
                || dataReader.IsDBNull(vwOptimizationSimCardColumnNames.AccountNumberIntegrationAuthenticationId))
            {
                simCard.IntegrationAuthenticationId = 0;
            }
            else
            {
                int.TryParse(dataReader[vwOptimizationSimCardColumnNames.AccountNumberIntegrationAuthenticationId].ToString(), out simCard.IntegrationAuthenticationId);
            }

            if (!columns.Contains(vwOptimizationSimCardColumnNames.CustomerRatePoolId)
                || dataReader.IsDBNull(vwOptimizationSimCardColumnNames.CustomerRatePoolId))
            {
                simCard.CustomerRatePoolId = null;
            }
            else
            {
                simCard.CustomerRatePoolId = dataReader.GetInt32(vwOptimizationSimCardColumnNames.CustomerRatePoolId);
            }

            if (!columns.Contains(vwOptimizationSimCardColumnNames.CustomerDataAllocationMB)
                || dataReader.IsDBNull(vwOptimizationSimCardColumnNames.CustomerDataAllocationMB))
            {
                simCard.CustomerDataAllocationMB = null;
            }
            else
            {
                simCard.CustomerDataAllocationMB = dataReader.GetDecimal(vwOptimizationSimCardColumnNames.CustomerDataAllocationMB);
            }

            if (!columns.Contains(vwOptimizationSimCardColumnNames.CustomerRatePlanMB)
                || dataReader.IsDBNull(vwOptimizationSimCardColumnNames.CustomerRatePlanMB))
            {
                simCard.CustomerRatePlanMB = null;
            }
            else
            {
                simCard.CustomerRatePlanMB = dataReader.GetDecimal(vwOptimizationSimCardColumnNames.CustomerRatePlanMB);
            }

            if (!columns.Contains(vwOptimizationSimCardColumnNames.SiteId)
                || dataReader.IsDBNull(vwOptimizationSimCardColumnNames.SiteId))
            {
                simCard.AmopCustomerId = null;
            }
            else
            {
                simCard.AmopCustomerId = dataReader.GetInt32(vwOptimizationSimCardColumnNames.SiteId);
            }
            return simCard;
        }

        public void StartQueue(KeySysLambdaContext context, long queueId, string messageId)
        {
            LogInfo(context, "SUB", $"StartQueue({queueId})");
            using (var conn = new SqlConnection(context.ConnectionString))
            {
                using (var cmd = new SqlCommand("UPDATE OptimizationQueue SET RunStatusId = @runStatusId, RunStartTime = GETUTCDATE(), SqsMessageId = @messageId WHERE Id = @id", conn))
                {
                    cmd.CommandType = CommandType.Text;
                    cmd.CommandTimeout = Amop.Core.Constants.SQLConstant.ShortTimeoutSeconds;
                    cmd.Parameters.AddWithValue("@id", queueId);
                    cmd.Parameters.AddWithValue("@runStatusId", OptimizationStatus.RunningPermutations);
                    cmd.Parameters.AddWithValue("@messageId", !string.IsNullOrEmpty(messageId) ? messageId : string.Empty);
                    conn.Open();

                    cmd.ExecuteNonQuery();

                    conn.Close();
                }
            }
        }

        public void RecordResults(KeySysLambdaContext context, long queueId, string revAccountNumber, long commPlanGroupId, OptimizationResult result, bool skipLowerCostCheck)
        {
            LogInfo(context, "SUB", $"RecordResults({queueId},{revAccountNumber},{commPlanGroupId},result,{skipLowerCostCheck}");
            if (skipLowerCostCheck)
            {
                RecordResults(context, queueId, revAccountNumber, result);
                return;
            }

            RecordResultsIfBetter(context, queueId, revAccountNumber, commPlanGroupId, result);
        }

        public void RecordResults(KeySysLambdaContext context, long queueId, int amopCustomerId, long commPlanGroupId, OptimizationResult result, bool skipLowerCostCheck)
        {
            LogInfo(context, "SUB", $"RecordResults({queueId},amopCustomerId:{amopCustomerId},{commPlanGroupId},result,{skipLowerCostCheck}");
            if (skipLowerCostCheck)
            {
                RecordResults(context, queueId, amopCustomerId, result);
                return;
            }

            RecordResultsIfBetter(context, queueId, amopCustomerId, commPlanGroupId, result);
        }


        public void RecordResultsIfBetter(KeySysLambdaContext context, long queueId, string revAccountNumber, long commPlanGroupId, OptimizationResult result)
        {
            LogInfo(context, "SUB", $"RecordResultsIfBetter({queueId},{revAccountNumber},{commPlanGroupId},result");

            // check best cost
            var lowestCost = GetLowestCostByCommPlanGroup(context, commPlanGroupId);
            var dataCost = result.TotalDataCost;
            var smsCost = result.TotalSmsCost;
            var thisCost = dataCost + smsCost;
            if (lowestCost > thisCost)
            {
                RecordResults(context, queueId, revAccountNumber, result);
            }
            else
            {
                LogInfo(context, "INFO", $"Results {thisCost} not Lower than {lowestCost} and not Recorded");
            }
        }

        public void RecordResultsIfBetter(KeySysLambdaContext context, long queueId, int amopCustomerId, long commPlanGroupId, OptimizationResult result)
        {
            LogInfo(context, "SUB", $"RecordResultsIfBetter({queueId},amopCustomerId:{amopCustomerId},{commPlanGroupId},result");

            // check best cost
            var lowestCost = GetLowestCostByCommPlanGroup(context, commPlanGroupId);
            var dataCost = result.TotalDataCost;
            var smsCost = result.TotalSmsCost;
            var thisCost = dataCost + smsCost;
            if (lowestCost > thisCost)
            {
                RecordResults(context, queueId, amopCustomerId, result);
            }
            else
            {
                LogInfo(context, "INFO", $"Results {thisCost} not Lower than {lowestCost} and not Recorded");
            }
        }

        public decimal GetLowestCostByCommPlanGroup(KeySysLambdaContext context, long commPlanGroupId)
        {
            LogInfo(context, "SUB", $"GetLowestCostByCommPlanGroup({commPlanGroupId})");
            var lowestCost = decimal.MaxValue;

            try
            {
                var policyFactory = new PolicyFactory(context.logger);
                var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
                return sqlRetryPolicy.Execute(() =>
                {
                    using (var conn = new SqlConnection(context.ConnectionString))
                    {
                        using (var cmd =
                            new SqlCommand("SELECT MIN(TotalCost) AS TotalCost FROM OptimizationQueue WHERE TotalCost IS NOT NULL AND CommPlanGroupId = @commPlanGroupId", conn))
                        {
                            cmd.CommandTimeout = 300;
                            cmd.CommandType = CommandType.Text;
                            cmd.Parameters.AddWithValue("@commPlanGroupId", commPlanGroupId);
                            conn.Open();

                            var rdr = cmd.ExecuteReader();
                            while (rdr.Read())
                            {
                                lowestCost = decimal.Parse(rdr["TotalCost"].ToString());
                            }

                            conn.Close();
                        }
                    }

                    return lowestCost;
                });
            }
            catch (SqlException ex)
            {
                LogInfo(context, "WARN", $"SQL error getting lowest cost for comm plan group {commPlanGroupId}: {ex.Message}");
            }

            return lowestCost;
        }

        public void RecordResults(KeySysLambdaContext context, long queueId, string revAccountNumber, OptimizationResult result)
        {
            LogInfo(context, "SUB", $"RecordResults({queueId},{revAccountNumber})");
            var logMessages = OptimizationResultDbWriter.RecordResults(context, context.ConnectionString, queueId, revAccountNumber, result);
            if (logMessages != null && logMessages.Count > 0)
            {
                foreach (var message in logMessages)
                {
                    LogInfo(context, message);
                }
            }
        }

        public void RecordResults(KeySysLambdaContext context, long queueId, int amopCustomerId, OptimizationResult result)
        {
            LogInfo(context, "SUB", $"RecordResults({queueId},amopCustomerId:{amopCustomerId})");
            var logMessages = OptimizationResultDbWriter.RecordResults(context, context.ConnectionString, queueId, amopCustomerId, result);
            if (logMessages != null && logMessages.Count > 0)
            {
                foreach (var message in logMessages)
                {
                    LogInfo(context, message);
                }
            }
        }

        public void RecordResults(KeySysLambdaContext context, long queueId, string revAccountNumber, CrossProviderCustomerRatePool pool)
        {
            LogInfo(context, "SUB", $"RecordResults({queueId},{revAccountNumber})");
            var logMessages = OptimizationResultDbWriter.RecordResults(context, context.ConnectionString, queueId, revAccountNumber, new List<CrossProviderCustomerRatePool>() { pool }, PortalType);
            if (logMessages != null && logMessages.Count > 0)
            {
                foreach (var message in logMessages)
                {
                    LogInfo(context, message);
                }
            }
        }

        public void RecordResults(KeySysLambdaContext context, long queueId, int amopCustomerId, CrossProviderCustomerRatePool pool)
        {
            LogInfo(context, "SUB", $"RecordResults({queueId},{amopCustomerId})");
            var logMessages = OptimizationResultDbWriter.RecordResults(context, context.ConnectionString, queueId, amopCustomerId, new List<CrossProviderCustomerRatePool>() { pool }, PortalType);
            if (logMessages != null && logMessages.Count > 0)
            {
                foreach (var message in logMessages)
                {
                    LogInfo(context, message);
                }
            }
        }

        public void RecordResults(KeySysLambdaContext context, long queueId, string customerIdentifier, List<CrossProviderCustomerRatePool> pools)
        {
            LogInfo(context, LogTypeConstant.Sub, $"({queueId},{customerIdentifier},{pools?.Count})");
            var logMessages = OptimizationResultDbWriter.RecordResults(context, context.ConnectionString, queueId, customerIdentifier, pools, PortalType);
            if (logMessages != null && logMessages.Count > 0)
            {
                foreach (var message in logMessages)
                {
                    LogInfo(context, message);
                }
            }
        }

        public void RecordResults(KeySysLambdaContext context, long queueId, int amopCustomerId, List<CrossProviderCustomerRatePool> pools)
        {
            LogInfo(context, "SUB", $"RecordResults({queueId},{amopCustomerId})");
            var logMessages = OptimizationResultDbWriter.RecordResults(context, context.ConnectionString, queueId, amopCustomerId, pools, PortalType);
            if (logMessages != null && logMessages.Count > 0)
            {
                foreach (var message in logMessages)
                {
                    LogInfo(context, message);
                }
            }
        }

        public void StopQueue(KeySysLambdaContext context, long queueId, bool isSuccess = true)
        {
            LogInfo(context, "SUB", $"StopQueue({queueId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            sqlRetryPolicy.Execute(() =>
            {
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("UPDATE OptimizationQueue SET RunStatusId = @runStatusId, RunEndTime = GETUTCDATE() WHERE Id = @id", conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.CommandTimeout = Amop.Core.Constants.SQLConstant.ShortTimeoutSeconds;
                        cmd.Parameters.AddWithValue("@id", queueId);
                        cmd.Parameters.AddWithValue("@runStatusId", isSuccess ? OptimizationStatus.CompleteWithSuccess : OptimizationStatus.CompleteWithErrors);
                        conn.Open();

                        cmd.ExecuteNonQuery();

                        conn.Close();
                    }
                }
            });
        }

        public long StartOptimizationInstanceWithBillingPeriod(KeySysLambdaContext context, int tenantId,
            string messageId, int optimizationBillingPeriodId, Guid? customerId, int? integrationAuthenticationId,
            PortalTypes portalType, long optimizationSessionId, bool useBillInAdvance, int? billInAdvanceBillingPeriodId, int? amopCustomerId = null)
        {
            LogInfo(context, "SUB", "StartOptimizationInstance");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                long instanceId = 0;

                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("usp_Optimization_Create_OptimizationInstance", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.Add("@instanceId", SqlDbType.BigInt);
                        cmd.Parameters["@instanceId"].Direction = ParameterDirection.Output;
                        cmd.Parameters.AddWithValue("@runStatus", OptimizationStatus.NotStarted);
                        cmd.Parameters.AddWithValue("@messageId", messageId);
                        cmd.Parameters.AddWithValue("@billingPeriodId", optimizationBillingPeriodId);
                        cmd.Parameters.AddWithValue("@customerId", customerId.HasValue ? (object)customerId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@portalType", (int)portalType);
                        cmd.Parameters.AddWithValue("@tenantId", tenantId);
                        cmd.Parameters.AddWithValue("@integrationAuthenticationId", integrationAuthenticationId.HasValue ? (object)integrationAuthenticationId.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@optimizationSessionId", optimizationSessionId);
                        cmd.Parameters.AddWithValue("@useBillInAdvance", useBillInAdvance);
                        cmd.Parameters.AddWithValue("@billInAdvanceBillingPeriodId", billInAdvanceBillingPeriodId);
                        cmd.Parameters.AddWithValue("@amopCustomerId", amopCustomerId);
                        conn.Open();

                        cmd.ExecuteNonQuery();

                        instanceId = long.Parse(cmd.Parameters["@instanceId"].Value.ToString());

                        conn.Close();
                    }
                }
                return instanceId;
            });
        }

        public async Task<long> StartOptimizationSession(KeySysLambdaContext context, int serviceProviderTenantId, BillingPeriod billingPeriod)
        {
            LogInfo(context, "SUB", "SimCardFromOptimizationSimCard");
            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlAsyncRetryPolicy(SQL_RETRY_MAX_COUNT);

            return await sqlRetryPolicy.ExecuteAsync(async () =>
            {
                await using var conn = new SqlConnection(context.ConnectionString);
                await using var cmd = new SqlCommand("usp_Optimization_Create_OptimizationSession", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add("@optimizationSessionId", SqlDbType.BigInt);
                cmd.Parameters["@optimizationSessionId"].Direction = ParameterDirection.Output;
                cmd.Parameters.AddWithValue("@billingPeriodId", billingPeriod.Id);
                cmd.Parameters.AddWithValue("@tenantId", serviceProviderTenantId);
                cmd.Parameters.AddWithValue("@optimizationTypeId", (int)OptimizationType.Carrier);

                conn.Open();
                await cmd.ExecuteNonQueryAsync();

                return long.Parse(cmd.Parameters["@optimizationSessionId"].Value.ToString());
            });
        }

        public long StartOptimizationInstance(KeySysLambdaContext context, int tenantId, int? serviceProviderId,
            Guid? customerId, string messageId, int? integrationAuthenticationId, DateTime billingPeriodStart,
            DateTime billingPeriodEnd, PortalTypes portalType, long optimizationSessionId, int optimizationBillingPeriodId,
            bool useBillInAdvance, int? billInAdvanceBillingPeriodId, int? amopCustomerId = null)
        {
            LogInfo(context, "SUB", "StartOptimizationInstance");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                long instanceId = 0;

                using (var Conn = new SqlConnection(context.ConnectionString))
                {
                    using (var Cmd = new SqlCommand("INSERT INTO OptimizationInstance(BillingPeriodStartDate, BillingPeriodEndDate, RunStatusId, RunStartTime, RevCustomerId, SqsMessageId, ServiceProviderId, CreatedBy, CreatedDate, IsDeleted, IntegrationAuthenticationId, TenantId, PortalTypeId, OptimizationSessionId, OptimizationBillingPeriodId, UseBillInAdvance, BillInAdvanceBillingPeriodId, AMOPCustomerId) VALUES(@billingPeriodStart, @billingPeriodEnd, @runStatusId, GETUTCDATE(), @revCustomerId, @sqsMessageId, @serviceProviderId, 'System', GETUTCDATE(), 0, @integrationAuthenticationId, @tenantId, @portalTypeId, @optimizationSessionId, @optimizationBillingPeriodId, @useBillInAdvance, @billInAdvanceBillingPeriodId, @amopCustomerId); SELECT @instanceId = SCOPE_IDENTITY()", Conn))
                    {
                        Cmd.CommandType = CommandType.Text;
                        Cmd.Parameters.Add("@instanceId", SqlDbType.BigInt);
                        Cmd.Parameters["@instanceId"].Direction = ParameterDirection.Output;
                        Cmd.Parameters.AddWithValue("@billingPeriodStart", billingPeriodStart);
                        Cmd.Parameters.AddWithValue("@billingPeriodEnd", billingPeriodEnd);
                        Cmd.Parameters.AddWithValue("@runStatusId", OptimizationStatus.NotStarted);
                        Cmd.Parameters.AddWithValue("@optimizationSessionId", optimizationSessionId);
                        if (integrationAuthenticationId == null)
                        {
                            Cmd.Parameters.AddWithValue("@integrationAuthenticationId", DBNull.Value);
                        }
                        else
                        {
                            Cmd.Parameters.AddWithValue("@integrationAuthenticationId", integrationAuthenticationId);
                        }

                        if (customerId == null)
                        {
                            Cmd.Parameters.AddWithValue("@revCustomerId", DBNull.Value);
                        }
                        else
                        {
                            Cmd.Parameters.AddWithValue("@revCustomerId", customerId.Value);
                        }
                        if (serviceProviderId == null)
                        {
                            Cmd.Parameters.AddWithValue("@serviceProviderId", DBNull.Value);
                        }
                        else
                        {
                            Cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId.Value);
                        }

                        if (string.IsNullOrWhiteSpace(messageId))
                        {
                            Cmd.Parameters.AddWithValue("@sqsMessageId", DBNull.Value);
                        }
                        else
                        {
                            Cmd.Parameters.AddWithValue("@sqsMessageId", messageId);
                        }

                        Cmd.Parameters.AddWithValue("@tenantId", tenantId);

                        Cmd.Parameters.AddWithValue("@portalTypeId", (int)portalType);

                        Cmd.Parameters.AddWithValue("@optimizationBillingPeriodId", optimizationBillingPeriodId);
                        Cmd.Parameters.AddWithValue("@useBillInAdvance", useBillInAdvance);
                        if (billInAdvanceBillingPeriodId == null)
                        {
                            Cmd.Parameters.AddWithValue("@billInAdvanceBillingPeriodId", DBNull.Value);
                        }
                        else
                        {
                            Cmd.Parameters.AddWithValue("@billInAdvanceBillingPeriodId", billInAdvanceBillingPeriodId.Value);
                        }

                        if (amopCustomerId == null)
                        {
                            Cmd.Parameters.AddWithValue("@amopCustomerId", DBNull.Value);
                        }
                        else
                        {
                            Cmd.Parameters.AddWithValue("@amopCustomerId", amopCustomerId.Value);
                        }

                        Conn.Open();

                        Cmd.ExecuteNonQuery();

                        instanceId = long.Parse(Cmd.Parameters["@instanceId"].Value.ToString());

                        Conn.Close();
                    }
                }

                return instanceId;
            });
        }

        public DateTime StopOptimizationInstance(KeySysLambdaContext context, long instanceId, OptimizationStatus status)
        {
            LogInfo(context, "SUB", "StopOptimizationInstance");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("UPDATE OptimizationInstance SET RunStatusId = @runStatusId, RunEndTime = GETUTCDATE() WHERE Id = @id", conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@id", instanceId);
                        cmd.Parameters.AddWithValue("@runStatusId", status);
                        conn.Open();

                        cmd.ExecuteNonQuery();

                        conn.Close();
                    }
                }

                return DateTime.UtcNow;
            });
        }

        public void UpdateCustomerOptimization(KeySysLambdaContext context, long sessionId, string errorMessage, int serviceProviderId, string customerId = "", int? AMOPCustomerId = 0)
        {
            LogInfo(context, CommonConstants.SUB, $"({sessionId}, {errorMessage}, {serviceProviderId}, {customerId}, {AMOPCustomerId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);

            var queryString = @"UPDATE OptimizationCustomerProcessing 
                                SET IsProcessed = @isProcessed, 
                                ErrorMessage = @errorMessage, 
                                EndTime = @endTime,
                                DeviceCount = @deviceCount
                                WHERE SessionId  = @sessionId
                                AND ServiceProviderId = @serviceProviderId ";

            if (string.IsNullOrEmpty(customerId))
            {
                queryString += "AND AMOPCustomerId = @amopCustomerId";
            }
            else
            {
                queryString += "AND CustomerId = @CustomerId";
            }

            sqlRetryPolicy.Execute(() =>
            {
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand(queryString, conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@isProcessed", true);
                        cmd.Parameters.AddWithValue("@errorMessage", errorMessage);
                        cmd.Parameters.AddWithValue("@endTime", DateTime.UtcNow);
                        cmd.Parameters.AddWithValue("@deviceCount", 0);
                        cmd.Parameters.AddWithValue("@sessionId", sessionId);
                        cmd.Parameters.AddWithValue("@serviceProviderId", serviceProviderId);

                        if (string.IsNullOrEmpty(customerId))
                        {
                            cmd.Parameters.AddWithValue("@amopCustomerId", AMOPCustomerId ?? (object)DBNull.Value);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@CustomerId", customerId ?? (object)DBNull.Value);
                        }

                        conn.Open();
                        cmd.ExecuteNonQuery();
                        conn.Close();
                    }
                }
            });
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="instanceId"></param>
        /// <param name="commPlanGroupId"></param>
        /// <param name="serviceProviderId"></param>
        /// <param name="revAccountNumber"></param>
        /// <param name="integrationAuthenticationId"></param>
        /// <param name="commPlanNames"></param>
        /// <param name="ratePoolCollection"></param>
        /// <param name="ratePools"></param>
        /// <param name="providerSimList">Assumes these cards are already filtered to a single service provider and customer (if customer optimization)</param>
        /// <param name="billingPeriod"></param>
        /// <param name="usesProration"></param>
        public int BaseDeviceAssignment(KeySysLambdaContext context, long instanceId, long commPlanGroupId, int? serviceProviderId,
            string revAccountNumber, int? integrationAuthenticationId, List<string> commPlanNames, RatePoolCollection ratePoolCollection,
            List<M2MRatePool> ratePools, List<vwOptimizationSimCard> providerSimList, BillingPeriod billingPeriod, bool usesProration, int? AMOPCustomerId = null, bool shouldFilterByRatePlanType = false)
        {
            LogInfo(context, LogTypeConstant.Sub, $"({instanceId},{commPlanGroupId},{serviceProviderId},{revAccountNumber},{integrationAuthenticationId},{AMOPCustomerId})");
            var queueId = CreateQueue(context, instanceId, commPlanGroupId, serviceProviderId, usesProration);

            var simList = providerSimList.ToList();
            if (!string.IsNullOrWhiteSpace(revAccountNumber) || AMOPCustomerId != null)
            {
                // filter sim cards by rate plan code
                var customerRatePlanCodes = ratePoolCollection.RatePools.Select(x => x.RatePlan.PlanName).Distinct()
                    .ToList();
                simList = simList
                    .Where(x => customerRatePlanCodes.Contains(x.CustomerRatePlanCode))
                    .ToList();
            }

            StartQueue(context, queueId, string.Empty);

            var simCards = ProjectDataUsageAndSaveDeviceByPortalType(context, billingPeriod, instanceId, simList, autoChangeRatePlan: true, commPlanGroupId);

            //also save to cache for faster query on optimizer lambda
            var isUsingRedisCache = context.TestRedisConnection();
            if (isUsingRedisCache)
            {
                ProjectDataUsageAndSaveDevicesToCache(context, instanceId, simList, billingPeriod, commPlanGroupId);
            }

            var assigner = new RatePoolAssigner(string.Empty, ratePoolCollection, simCards, context.LambdaContext, isUsingRedisCache,
                PortalType,
                shouldFilterByRatePlanType,
                ratePoolCollection.ShouldPoolByOptimizationGroup);
            assigner.BaseAssignmentOfSimCards(ratePools, queueId);

            // record results
            assigner.SetPortalTypeToBestResult(PortalType);
            var result = assigner.Best_Result;
            var totalCost = result.CombinedRatePools.TotalDataCost;

            // base device assignment
            if (AMOPCustomerId == null)
            {
                RecordResults(context, queueId, revAccountNumber, result);
            }
            else
            {
                RecordResults(context, queueId, AMOPCustomerId.GetValueOrDefault(0), result);
            }

            // stop queue
            StopQueue(context, queueId);

            // return total sim cards actually assigned
            return result.CombinedRatePools.TotalSimCardCount;
        }

        // Function to calculate the projected usage, map the devices from vwOptimizationSimCard -> SimCard and then insert them into database table [OptimizationDevice]
        protected List<SimCard> ProjectDataUsageAndSaveDevices(KeySysLambdaContext context, long instanceId, List<vwOptimizationSimCard> optimizationSimCards, BillingPeriod billingPeriod, bool autoChangeRatePlan, long? commGroupId = null)
        {
            LogInfo(context, LogTypeConstant.Sub, $"instanceId: {instanceId},optimizationSimCards.Count: {optimizationSimCards?.Count},billingPeriod.Id: {billingPeriod.Id}");
            try
            {
                var optimizationDeviceTable = new DataTable();
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.InstanceId, typeof(long));
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.DeviceId, typeof(int));
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.CycleDataUsageMB, typeof(decimal));
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.ProjectedDataUsageMB, typeof(decimal));
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.CommunicationPlan);
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.MSISDN);
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.ICCID);
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.UsageDate, typeof(DateTime));
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.CreatedBy);
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.CreatedDate, typeof(DateTime));
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.AmopDeviceId, typeof(int));
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.ServiceProviderId, typeof(int));
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.DateActivated, typeof(DateTime));
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.WasActivatedInThisBillingPeriod, typeof(bool));
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.DaysActivatedInBillingPeriod, typeof(int));
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.SmsUsage, typeof(long));
                optimizationDeviceTable.Columns.Add(OptimizationDeviceColumnNames.AutoChangeRatePlan, typeof(bool));
                optimizationDeviceTable.Columns.Add(CommonColumnNames.OptimizationCommGroupId, typeof(long));
                optimizationDeviceTable.Columns.Add(CommonColumnNames.RatePlanCode, typeof(string));

                var simCards = new List<SimCard>();
                for (var deviceIndex = 0; deviceIndex < optimizationSimCards.Count; deviceIndex++)
                {
                    var deviceRow = optimizationDeviceTable.NewRow();

                    var optSimCard = optimizationSimCards[deviceIndex];
                    var projectedDataUsage = ProjectDataUsage(optSimCard.CycleDataUsageMB, optSimCard.Status, optSimCard.UsageDate, billingPeriod.BillingPeriodStart, billingPeriod.BillingPeriodEnd, billingPeriod.BillingTimeZone);
                    deviceRow[OptimizationDeviceColumnNames.InstanceId] = instanceId;
                    deviceRow[OptimizationDeviceColumnNames.DeviceId] = DBNull.Value;
                    deviceRow[OptimizationDeviceColumnNames.CycleDataUsageMB] = optSimCard.CycleDataUsageMB;
                    deviceRow[OptimizationDeviceColumnNames.ProjectedDataUsageMB] = projectedDataUsage;
                    deviceRow[OptimizationDeviceColumnNames.CommunicationPlan] = optSimCard.CommunicationPlan;
                    deviceRow[OptimizationDeviceColumnNames.MSISDN] = optSimCard.MSISDN;
                    deviceRow[OptimizationDeviceColumnNames.ICCID] = optSimCard.ICCID;
                    deviceRow[OptimizationDeviceColumnNames.UsageDate] = optSimCard.UsageDate;
                    deviceRow[OptimizationDeviceColumnNames.CreatedBy] = OptimizationConstant.DefaultM2MCreatedByName;
                    deviceRow[OptimizationDeviceColumnNames.CreatedDate] = DateTime.UtcNow;
                    deviceRow[OptimizationDeviceColumnNames.AmopDeviceId] = optSimCard.Id;
                    deviceRow[OptimizationDeviceColumnNames.ServiceProviderId] = optSimCard.ServiceProviderId;

                    if (optSimCard.ProviderDateActivated != null)
                    {
                        deviceRow[OptimizationDeviceColumnNames.DateActivated] = optSimCard.ProviderDateActivated;
                    }
                    else
                    {
                        deviceRow[OptimizationDeviceColumnNames.DateActivated] = DBNull.Value;
                    }

                    optSimCard.CycleDataUsageMB = projectedDataUsage;
                    var simCard = SimCardFromOptimizationSimCard(optSimCard, billingPeriod);
                    simCards.Add(simCard);

                    deviceRow[OptimizationDeviceColumnNames.WasActivatedInThisBillingPeriod] = simCard.WasActivatedInThisBillingPeriod;
                    deviceRow[OptimizationDeviceColumnNames.DaysActivatedInBillingPeriod] = simCard.DaysActivatedInBillingPeriod;
                    deviceRow[OptimizationDeviceColumnNames.SmsUsage] = simCard.SmsUsage;
                    deviceRow[OptimizationDeviceColumnNames.AutoChangeRatePlan] = autoChangeRatePlan;
                    deviceRow[CommonColumnNames.OptimizationCommGroupId] = (object)commGroupId ?? DBNull.Value;
                    deviceRow[CommonColumnNames.RatePlanCode] = optSimCard.CustomerRatePlanCode;
                    optimizationDeviceTable.Rows.Add(deviceRow);
                    optimizationSimCards[deviceIndex] = optSimCard;
                }

                List<SqlBulkCopyColumnMapping> columnMappings = SQLBulkCopyHelper.AutoMapColumns(optimizationDeviceTable);

                SqlHelper.SqlBulkCopy(context.ConnectionString, optimizationDeviceTable, DatabaseTableNames.OptimizationDevice, columnMappings);

                return simCards;
            }
            catch (SqlException ex)
            {
                LogInfo(context, LogTypeConstant.Exception, $"Exception when executing stored procedure: {ex.Message}, ErrorCode:{ex.ErrorCode}-{ex.Number}, Stack Trace: {ex.StackTrace}");
                throw;
            }
            catch (InvalidOperationException ex)
            {
                LogInfo(context, LogTypeConstant.Exception, $"Exception when connecting to database: {ex.Message}, Stack Trace: {ex.StackTrace}");
                throw;
            }
            catch (Exception ex)
            {
                LogInfo(context, LogTypeConstant.Exception, $"Exception when projecting usage and saving devices for instance id {instanceId} : {ex.Message}, Stack Trace: {ex.StackTrace}");
                throw;
            }
        }

        protected List<SimCard> ProjectDataUsageAndSaveDevicesToCache(KeySysLambdaContext context, long instanceId, List<vwOptimizationSimCard> optimizationSimCards, BillingPeriod billingPeriod, long commPlanGroupId)
        {
            LogInfo(context, LogTypeConstant.Sub, $"(,{instanceId},,)");

            var simCards = new List<SimCard>();
            for (var iCard = 0; iCard < optimizationSimCards.Count; iCard++)
            {
                var optSimCard = optimizationSimCards[iCard];
                var projectedDataUsage = ProjectDataUsage(optSimCard.CycleDataUsageMB, optSimCard.Status, optSimCard.UsageDate, billingPeriod.BillingPeriodStart, billingPeriod.BillingPeriodEnd, billingPeriod.BillingTimeZone);
                optSimCard.CycleDataUsageMB = projectedDataUsage;
                var simCard = SimCardFromOptimizationSimCard(optSimCard, billingPeriod);
                simCards.Add(simCard);
            }
            RedisCacheHelper.RecordSimCardsToCache(context, instanceId, simCards, commPlanGroupId);
            return simCards;
        }

        public static SimCard SimCardFromOptimizationSimCard(vwOptimizationSimCard optSimCard, BillingPeriod billingPeriod)
        {
            var simCard = new SimCard()
            {
                Id = optSimCard.Id,
                ICCID = optSimCard.ICCID,
                MSISDN = optSimCard.MSISDN,
                CommunicationPlan = optSimCard.CommunicationPlan,
                CycleDataUsageMB = optSimCard.CycleDataUsageMB,
                DateActivated = optSimCard.ProviderDateActivated,
                SmsUsage = optSimCard.SmsUsage,
                RatePlanCode = optSimCard.CustomerRatePlanCode,
                CustomerRatePoolId = optSimCard.CustomerRatePoolId
            };

            simCard.WasActivatedInThisBillingPeriod = DateIsInBillingPeriod(simCard.DateActivated, billingPeriod);
            simCard.DaysActivatedInBillingPeriod = simCard.WasActivatedInThisBillingPeriod
                ? DaysLeftInBillingPeriod(simCard.DateActivated, billingPeriod)
                : billingPeriod.DaysInBillingPeriod;

            simCard.RatePlanTypeId = optSimCard.RatePlanTypeId;
            simCard.OptimizationGroupId = optSimCard.OptimizationGroupId;
            simCard.PortalType = optSimCard.PortalType;

            return simCard;
        }

        protected static bool DateIsInBillingPeriod(DateTime? dateActivated, BillingPeriod billingPeriod)
        {
            if (dateActivated == null)
            {
                return false;
            }

            return dateActivated >= billingPeriod.BillingPeriodStart && dateActivated <= billingPeriod.BillingPeriodEnd;
        }

        protected static int DaysLeftInBillingPeriod(DateTime? dateActivated, BillingPeriod billingPeriod)
        {
            if (dateActivated == null)
            {
                return billingPeriod.DaysInBillingPeriod;
            }

            var daysUntilEndDbl = billingPeriod.BillingPeriodEnd.Subtract(dateActivated.Value);
            var daysUntilEndInt = (int)Math.Ceiling(daysUntilEndDbl.TotalDays);
            if (daysUntilEndInt > billingPeriod.DaysInBillingPeriod)
            {
                return billingPeriod.DaysInBillingPeriod;
            }
            else
            {
                return daysUntilEndInt;
            }
        }

        public static decimal ProjectDataUsage(decimal cycleDataUsageMb, string status, DateTime? usageDate, DateTime billingPeriodStart, DateTime billingPeriodEnd, TimeZoneInfo billingTimeZone)
        {
            // can the card have any more data usage?
            if (InActiveStatuses.Contains(status))
            {
                // inactive, so no more usage
                return cycleDataUsageMb;
            }

            usageDate = usageDate == null ? billingPeriodEnd : TimeZoneInfo.ConvertTimeFromUtc(usageDate.Value, billingTimeZone);

            // is the billing period already ended?
            var currentLocalDateTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, billingTimeZone);
            if (currentLocalDateTime > billingPeriodEnd)
            {
                // yes, so do not project usage
                return cycleDataUsageMb;
            }

            // is the usage date past the end of the billing period?
            // check incase of incorrect usage sync leads to negative projected usage
            if (usageDate >= billingPeriodEnd || usageDate < billingPeriodStart)
            {
                // yes, so no more usage
                return cycleDataUsageMb;
            }

            var totalBillingPeriodSeconds = Convert.ToInt32(billingPeriodEnd.Subtract(billingPeriodStart).TotalSeconds);
            var totalUsageSeconds = Convert.ToInt32(usageDate.Value.Subtract(billingPeriodStart).TotalSeconds);

            // scale by the seconds remaining + 1%
            var projectedUsage = (totalBillingPeriodSeconds / (decimal)totalUsageSeconds) * cycleDataUsageMb * 1.01M;
            return projectedUsage;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context">KEYSYS Lambda Context</param>
        /// <param name="instanceId">Optimization Instance to Cleanup</param>
        /// <param name="deliveryDelay">Delivery Delay of this Message in seconds. Default is 600 seconds (10 minutes)</param>
        protected void EnqueueCleanup(KeySysLambdaContext context, long instanceId, int deliveryDelay = 600, int serviceProviderId = 0, bool isCustomerOptimization = false, bool isLastInstance = false)
        {
            LogInfo(context, "SUB", "EnqueueCleanup");
            LogInfo(context, "INFO", $"InstanceId: {instanceId}");
            LogInfo(context, "INFO", $"IsCustomerOptimization: {isCustomerOptimization}");
            LogInfo(context, "INFO", $"IsLastInstance: {isLastInstance}");
            LogInfo(context, "INFO", $"ServiceProviderId: {serviceProviderId}");

            var awsCredentials = context.GeneralProviderSettings.AwsCredentials;
            using (var client = new AmazonSQSClient(awsCredentials, Amazon.RegionEndpoint.USEast1))
            {
                var requestMsgBody = $"Instance to Cleanup is {instanceId}";
                var request = new SendMessageRequest
                {
                    DelaySeconds = (int)TimeSpan.FromSeconds(deliveryDelay).TotalSeconds,
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        {
                            "InstanceId", new MessageAttributeValue
                            { DataType = "String", StringValue = instanceId.ToString()}
                        },
                        {
                            "IsCustomerOptimization", new MessageAttributeValue
                            { DataType = "String", StringValue = isCustomerOptimization.ToString()}
                        },
                        {
                            "IsLastInstance", new MessageAttributeValue
                            { DataType = "String", StringValue = isLastInstance.ToString()}
                        },
                        {
                            "ServiceProviderId", new MessageAttributeValue
                            { DataType = "String", StringValue = serviceProviderId.ToString()}
                        },
                    },
                    MessageBody = requestMsgBody,
                    QueueUrl = context.CleanupDestinationQueueUrl
                };

                var response = client.SendMessageAsync(request);
                response.Wait();
                if (response.Status == TaskStatus.Faulted || response.Status == TaskStatus.Canceled)
                {
                    LogInfo(context, "RESPONSE STATUS", $"Error Sending {instanceId}: {response.Status}");
                }
            }
        }

        public async Task EnqueueOptimizationRunsAsync(KeySysLambdaContext context, long instanceId, List<long> commGroupIds, OptimizationChargeType chargeType, int queuesPerInstance, bool skipLowerCostCheck = false, bool isCustomerOptimization = false)
        {
            LogInfo(context, "SUB", "EnqueueOptimizationRunsAsync");

            foreach (var commGroupId in commGroupIds)
            {
                // get queues
                var queueIds = GetUnfinishedQueues(context, instanceId, new List<long> { commGroupId });

                if (queueIds != null && queueIds.Count > 0)
                {
                    var sendCount = 0;
                    var queueIdsToSend = new List<string>();
                    foreach (var queueId in queueIds)
                    {
                        sendCount++;
                        queueIdsToSend.Add(queueId.ToString());

                        if (sendCount % queuesPerInstance == 0)
                        {
                            await SendQueueToSqsAsync(context, queueIdsToSend, skipLowerCostCheck, chargeType, isCustomerOptimization: isCustomerOptimization);
                            queueIdsToSend = new List<string>();
                            sendCount = 0;
                        }
                    }

                    if (sendCount > 0)
                    {
                        await SendQueueToSqsAsync(context, queueIdsToSend, skipLowerCostCheck, chargeType, isCustomerOptimization: isCustomerOptimization);
                    }
                }
            }
        }
        public async Task EnqueueOptimizationContinueProcessAsync(KeySysLambdaContext context, List<long> queueIds, OptimizationChargeType chargeType, bool skipLowerCostCheck = false)
        {
            LogInfo(context, "SUB", $"(,{string.Join(',', queueIds)})");
            var queueIdsToEnqueue = queueIds.Select(queueId => queueId.ToString());
            await SendQueueToSqsAsync(context, queueIdsToEnqueue, skipLowerCostCheck, chargeType, true);
        }

        private static List<long> GetUnfinishedQueues(KeySysLambdaContext context, long instanceId, List<long> commPlanGroupIds)
        {
            context.LogInfo("SUB", "GetUnfinishedQueues");
            if (commPlanGroupIds == null || commPlanGroupIds.Count == 0)
            {
                throw new ArgumentNullException(nameof(commPlanGroupIds));
            }

            var commPlanGroupIdString = string.Join(',', commPlanGroupIds);

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                var queueIds = new List<long>();

                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand($"SELECT [Id] FROM [dbo].[OptimizationQueue] WHERE InstanceId = @instanceId AND CommPlanGroupId IN ({commPlanGroupIdString}) AND RunStartTime IS NULL AND RunStatusId = 1", conn))
                    {
                        cmd.CommandType = CommandType.Text;
                        cmd.Parameters.AddWithValue("@instanceId", instanceId);
                        conn.Open();

                        var rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            // ReSharper disable once AssignNullToNotNullAttribute
                            // Cannot be null in above query
                            var queueId = long.Parse(rdr[0].ToString());
                            queueIds.Add(queueId);
                        }

                        conn.Close();
                    }
                }

                return queueIds;
            });
        }

        private async Task SendQueueToSqsAsync(KeySysLambdaContext context, IEnumerable<string> queueIdsToSend, bool skipLowerCostCheck, OptimizationChargeType chargeType, bool isChainingProcess = false, bool isCustomerOptimization = false)
        {
            LogInfo(context, "SUB", $"(,{string.Join(',', queueIdsToSend)},,,isChainingProcess:{isChainingProcess})");
            var awsCredentials = context.GeneralProviderSettings.AwsCredentials;
            using (var client = new AmazonSQSClient(awsCredentials, Amazon.RegionEndpoint.USEast1))
            {
                var queueIds = string.Join(',', queueIdsToSend);

                var requestMsgBody = $"Next optimization queues are {queueIds}";
                var request = new SendMessageRequest
                {
                    DelaySeconds = 5,
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        {"QueueIds", new MessageAttributeValue {DataType = "String", StringValue = queueIds}},
                        {"SkipLowerCostCheck", new MessageAttributeValue {DataType = "String", StringValue = skipLowerCostCheck.ToString()}},
                        {"ChargeType", new MessageAttributeValue {DataType = "String", StringValue = ((int)chargeType).ToString()}},
                        {"IsChainingProcess", new MessageAttributeValue {DataType = "String", StringValue = isChainingProcess.ToString()}},
                        {SQSMessageKeyConstant.IS_CUSTOMER_OPTIMIZATION, new MessageAttributeValue { DataType = nameof(String), StringValue = isCustomerOptimization.ToString()}}
                    },
                    MessageBody = requestMsgBody,
                    QueueUrl = context.QueueDestinationQueueUrl
                };

                var response = await client.SendMessageAsync(request);
                if ((int)response.HttpStatusCode < 200 || (int)response.HttpStatusCode > 299)
                {
                    LogInfo(context, "EXCEPTION", $"Error Sending {string.Join(',', queueIds)}: {response.HttpStatusCode:d} {response.HttpStatusCode:g}");
                }
            }
        }

        /// <summary>
        /// Used by Customer Optimization for Rate Plans that Bill in Advance
        /// </summary>
        /// <param name="context"></param>
        /// <param name="instanceId"></param>
        /// <param name="commPlanGroupId"></param>
        /// <param name="serviceProviderId"></param>
        /// <param name="revAccountNumber"></param>
        /// <param name="customerRatePlans"></param>
        /// <param name="advanceBillingDevices"></param>
        /// <param name="usesProration"></param>
        /// <param name="billingPeriod"></param>
        /// <param name="chargeType"></param>
        public void CalculateAdvanceBillingDevices(KeySysLambdaContext context, long instanceId, long commPlanGroupId, int serviceProviderId,
            string revAccountNumber, int? amopCustomerId, List<RatePlan> customerRatePlans, List<vwOptimizationSimCard> advanceBillingDevices, bool usesProration,
            BillingPeriod billingPeriod, OptimizationChargeType chargeType)
        {
            LogInfo(context, LogTypeConstant.Sub, $"(,{instanceId},{commPlanGroupId},{serviceProviderId},{revAccountNumber},{amopCustomerId},,,{usesProration},{billingPeriod.Id},{chargeType})");
            var IsBillInAdvance = true;
            long queueId = CreateQueue(context, instanceId, commPlanGroupId, serviceProviderId, usesProration, IsBillInAdvance);
            StartQueue(context, queueId, string.Empty);

            string customerIdentifier;
            if (amopCustomerId == null)
            {
                customerIdentifier = revAccountNumber;
            }
            else
            {
                customerIdentifier = amopCustomerId.ToString();
            }

            var logger = context.logger;

            var pool = CalculatePoolForAdvanceBillingDevices(advanceBillingDevices, customerRatePlans, logger, billingPeriod, chargeType, PortalType);

            RecordResults(context, queueId, customerIdentifier, pool);

            // stop queue
            StopQueue(context, queueId);
        }

        public void CalculateAdvanceBillingDevices(KeySysLambdaContext context, long instanceId, long commPlanGroupId, int serviceProviderId,
            int amopCustomerId, List<RatePlan> customerRatePlans, List<vwOptimizationSimCard> advanceBillingDevices, bool usesProration,
            BillingPeriod billingPeriod, OptimizationChargeType chargeType)
        {
            LogInfo(context, "SUB", $"CalculateAdvanceBillingDevices(,{instanceId},{commPlanGroupId},{serviceProviderId},{amopCustomerId},,,{usesProration},{billingPeriod.Id},{chargeType})");

            var IsBillInAdvance = true;
            long queueId = CreateQueue(context, instanceId, commPlanGroupId, serviceProviderId, usesProration, IsBillInAdvance);
            StartQueue(context, queueId, string.Empty);

            var logger = context.logger;
            var pool = CalculatePoolForAdvanceBillingDevices(advanceBillingDevices, customerRatePlans, logger, billingPeriod, chargeType, PortalType);

            RecordResults(context, queueId, amopCustomerId, pool);

            // stop queue
            StopQueue(context, queueId);
        }

        public static CrossProviderCustomerRatePool CalculatePoolForAdvanceBillingDevices(List<vwOptimizationSimCard> advanceBillingDevices, IReadOnlyCollection<RatePlan> customerRatePlans,
            IKeysysLogger logger, BillingPeriod billingPeriod, OptimizationChargeType chargeType, PortalTypes portalType = PortalTypes.Mobility)
        {
            var pool = new CrossProviderCustomerRatePool(billingPeriod, chargeType, portalType, isBillInAdvance: true);

            foreach (var device in advanceBillingDevices)
            {
                var skipDevice = false;

                RatePlan customerRatePlan = default;
                if (device.CustomerRatePlanId != null)
                {
                    customerRatePlan = customerRatePlans.FirstOrDefault(x => x.Id == device.CustomerRatePlanId);

                    if (string.IsNullOrWhiteSpace(customerRatePlan.PlanName))
                    {
                        logger.LogInfo("WARN", $"No Customer Rate Plan {device.CustomerRatePlanCode} found for this Device: {device.ICCID}");
                        skipDevice = true;
                    }
                }
                else
                {
                    // WARN and exclude
                    logger.LogInfo("WARN", $"No Customer Rate Plan for this Device: {device.ICCID}");
                    skipDevice = true;
                }

                // can we add this to the pool?
                if (skipDevice)
                {
                    logger.LogInfo("WARN", $"Device skipped: {device.ICCID}");
                }
                else
                {
                    pool.AddSimCard(new CrossProviderCustomerSimCard(device, customerRatePlan, chargeType, true));
                }
            }

            return pool;
        }

        protected DeviceSyncSummary GetSummaryValues(KeySysLambdaContext context, IntegrationType integrationType, int serviceProviderId)
        {
            DeviceSyncSummary summary = new DeviceSyncSummary();

            string connectionString = context.GeneralProviderSettings.JasperDbConnectionString;
            string sqlText = "usp_Jasper_Devices_Get_Sync_Summary";
            switch (integrationType)
            {
                case IntegrationType.ThingSpace:
                    connectionString = context.ConnectionString;
                    sqlText = "usp_ThingSpace_Devices_Get_Sync_Summary";
                    break;
                case IntegrationType.Telegence:
                    connectionString = context.ConnectionString;
                    sqlText = "usp_Telegence_Devices_Get_Sync_Summary";
                    break;
                case IntegrationType.eBonding:
                    connectionString = context.ConnectionString;
                    sqlText = "usp_eBonding_Devices_Get_Sync_Summary";
                    break;
                case IntegrationType.Pond:
                    connectionString = context.ConnectionString;
                    sqlText = SQLConstant.StoredProcedureName.usp_Pond_Devices_Get_Sync_Summary;
                    break;
                case IntegrationType.Teal:
                    connectionString = context.ConnectionString;
                    sqlText = SQLConstant.StoredProcedureName.usp_Teal_Devices_Get_Sync_Summary;
                    break;
            }

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                using (var cn = new SqlConnection(connectionString))
                {
                    using (var cmd = new SqlCommand(sqlText, cn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        if (integrationType == IntegrationType.Jasper
                            || integrationType == IntegrationType.POD19
                            || integrationType == IntegrationType.TMobileJasper
                            || integrationType == IntegrationType.Rogers)
                        {
                            cmd.Parameters.AddWithValue("@ServiceProviderId", serviceProviderId);
                        }

                        cn.Open();

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                if (!reader.IsDBNull(0))
                                {
                                    summary.DetailLastSyncDate = reader.GetDateTime(0);
                                }

                                if (!reader.IsDBNull(1))
                                {
                                    summary.DetailQueueCount = reader.GetInt32(1);
                                }

                                if (!reader.IsDBNull(2))
                                {
                                    summary.DetailUpdatedCount = reader.GetInt32(2);
                                }

                                if (!reader.IsDBNull(3))
                                {
                                    summary.UsageLastSyncDate = reader.GetDateTime(3);
                                }

                                if (!reader.IsDBNull(4))
                                {
                                    summary.UsageQueueCount = reader.GetInt32(4);
                                }

                                if (!reader.IsDBNull(5))
                                {
                                    summary.UsageUpdatedCount = reader.GetInt32(5);
                                }

                                if (!reader.IsDBNull(6))
                                {
                                    summary.DeviceCount = reader.GetInt32(6);
                                }
                            }

                            reader.Close();
                        }

                        cn.Close();
                    }
                }

                return summary;
            });
        }

        public int? GetJasperServiceProviderId(KeySysLambdaContext context)
        {
            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(SQL_RETRY_MAX_COUNT);
            return sqlRetryPolicy.Execute(() =>
            {
                using (var cn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("SELECT TOP 1 id FROM ServiceProvider sp WHERE sp.IntegrationId = 1", cn))
                    {
                        cmd.CommandType = System.Data.CommandType.Text;
                        cn.Open();

                        var providerId = cmd.ExecuteScalar();

                        cn.Close();

                        if (providerId != null)
                        {
                            return (int)providerId;
                        }
                        else
                        {
                            return new int?();
                        }
                    }
                }
            });
        }

        public static int? GetServiceProviderId(SQSEvent.SQSMessage message)
        {
            if (!message.MessageAttributes.ContainsKey("ServiceProviderId") || !int.TryParse(message.MessageAttributes["ServiceProviderId"].StringValue, out var serviceProviderId))
            {
                return null;
            }

            return serviceProviderId;
        }

        public async Task LogAndSendConfigurationIssueEmailAsync(KeySysLambdaContext context, string receiverEmail, long sessionId, long instanceId)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(context.ConnectionString);
            string server = builder.DataSource;

            //format the message here so that it is also available in logs
            var errorMessage = $"Optimization session id: {sessionId}, instance id: {instanceId}. Optimization will continue using the RDS database '{server}'.";
            var additionalInfo = $"Please go to {RedisReconfigureDocumentationURL} in order to update the Redis cache configuration settings so that the optimization can operate efficiently.";
            LogInfo(context, "EXCEPTION", errorMessage);
            LogInfo(context, "INFO", additionalInfo);
            await SendConfigurationIssueEmailNotificationAsync(context, receiverEmail, new List<string>() { errorMessage, additionalInfo });
        }

        public async Task SendConfigurationIssueEmailNotificationAsync(KeySysLambdaContext context, string receiverEmail, List<string> errorMessages)
        {
            LogInfo(context, "SUB", $"({receiverEmail}, {errorMessages.Count})");
            var currentOU = context.OptimizationSettings.ExecutionOU;
            var bodyBuilder = BuildConfigurationIssueEmailBody(context, currentOU, errorMessages);
            string subject = $"{currentOU} - Optimization Configuration Issue";

            await Policy.Handle<Exception>()
                .WaitAndRetryAsync(EMAIL_RETRY_MAX_COUNT,
                    retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    (exception, timeSpan, retryCount, sqlContext) =>
                    {
                        LogInfo(context, "STATUS",
                            $"Encountered transient error when sending error notification. Delaying for {timeSpan.TotalMilliseconds}ms, then making retry {retryCount} of {EMAIL_RETRY_MAX_COUNT}. Message: {exception.Message}");
                    })
                .ExecuteAsync(async () => await SendEmailAsync(context, subject, receiverEmail, bodyBuilder));
        }
        public Amazon.Runtime.BasicAWSCredentials AwsSesCredentials(KeySysLambdaContext context)
        {
            return AwsCredentials(new Base64Service(), context.GeneralProviderSettings.AWSAccesKeyID_SES, context.GeneralProviderSettings.AWSSecretAccessKey_SES);
        }
        public static Amazon.Runtime.BasicAWSCredentials AwsCredentials(IBase64Service base64Service, string awsAccessKey, string encodedSecretAccessKey)
        {
            return new Amazon.Runtime.BasicAWSCredentials(awsAccessKey, base64Service.Base64Decode(encodedSecretAccessKey));
        }


        private async Task SendEmailAsync(KeySysLambdaContext context, string subject, string receiverEmail, BodyBuilder bodyBuilder)
        {
            LogInfo(context, "SUB", "SendEmailAsync()");
            var emailFactory = new SimpleEmailServiceFactory();
            using var client = emailFactory.GetClient(AwsSesCredentials(context), RegionEndpoint.USEast1);
            var message = new MimeMessage();
            //CarrierOptimizationFromEmailAddress in the UI
            message.From.Add(MailboxAddress.Parse(context.OptimizationSettings.FromEmailAddress));
            message.To.Add(MailboxAddress.Parse(receiverEmail));

            message.Subject = subject;
            message.Body = bodyBuilder.ToMessageBody();
            using (var stream = new MemoryStream())
            {
                message.WriteTo(stream);

                var sendRequest = new SendRawEmailRequest
                {
                    RawMessage = new RawMessage(stream)
                };
                try
                {
                    var response = await client.SendRawEmailAsync(sendRequest);
                    LogInfo(context, "RESPONSE STATUS", $"{response.HttpStatusCode:d} {response.HttpStatusCode:g}");
                }
                catch (Exception ex)
                {
                    LogInfo(context, "EXCEPTION", "Error Sending Email: " + ex.Message);
                }
            }
        }

        private BodyBuilder BuildConfigurationIssueEmailBody(KeySysLambdaContext context, string currentOU, ICollection<string> errorMessages)
        {
            LogInfo(context, "SUB", $"(,{currentOU},)");
            return new BodyBuilder
            {
                HtmlBody = @$"<html>
                    <h1>{context.OptimizationSettings.ExecutionOU} - Optimization Configuration Issue</h1>
                    <h3>Optimization for {context.OptimizationSettings.ExecutionOU} OU is not currently configured correctly to use Redis cache. This will cause optimization to experience degraded performance.</h4>
                    <ul>
                        {string.Join("", errorMessages.Select(errorMessage => $"<li>{errorMessage}</li>"))}
                    </ul>
                </html>",
                TextBody = @$"
                    {context.OptimizationSettings.ExecutionOU} - Optimization Configuration Issue
                    Optimization for {context.OptimizationSettings.ExecutionOU} OU is not currently configured correctly to use Redis cache. This will cause optimization to experience degraded performance.
                    {string.Join(Environment.NewLine, errorMessages)}"
            };
        }

        public List<vwOptimizationSimCard> ProcessDevicesWithAutoChangeDisabledRatePlans(KeySysLambdaContext context, int? integrationAuthenticationId, bool usesProration, string revAccountNumber, int? AMOPCustomerId, BillingPeriod billingPeriod, BillingPeriod nextBillingPeriod, long instanceId, List<vwOptimizationSimCard> allSimCards, List<RatePlan> autoChangeDisabledRatePlans, int tenantId, string serviceProviderIds = null)
        {
            LogInfo(context, LogTypeConstant.Sub, $"{integrationAuthenticationId},{usesProration},{revAccountNumber},{billingPeriod?.Id},{instanceId},{allSimCards?.Count},{autoChangeDisabledRatePlans?.Count},{PortalType}");

            // Generate chargeType specifically for optimization without algorithm.
            var useBillInAdvance = autoChangeDisabledRatePlans.Any(x => x.IsBillInAdvanceEligible);
            var chargeType = OptimizationChargeType.RateChargeAndOverage;
            if (useBillInAdvance)
            {
                chargeType = OptimizationChargeType.OverageOnly;
            }

            if (useBillInAdvance && (nextBillingPeriod == null || billingPeriod == null))
            {
                LogInfo(context, CommonConstants.ERROR, string.Format(LogCommonStrings.BILL_IN_ADVANCE_BILL_PERIOD_COULD_NOT_BE_FOUND, billingPeriod?.Id));
                return allSimCards;
            }

            // track number of devices that were process in this step 
            var projectedDeviceIds = new List<int>();

            // devices that have a rate pool and the rate plan have checkbox enabled
            var validDevices = allSimCards
                .Where(x => autoChangeDisabledRatePlans.Any(ratePlan => ratePlan.Id == x.CustomerRatePlanId)).ToList();

            if (validDevices.Count == 0)
            {
                LogInfo(context, LogTypeConstant.Info, $"No device assigned with any of the {autoChangeDisabledRatePlans.Count} rate plans that have 'Auto Change Rate Plan' disabled. No devices were processed in this step.");
                return allSimCards;
            }

            // Step 1 - Pooled Devices
            var pooledDevices = validDevices.Where(x => x.CustomerRatePoolId != null).ToList();
            var remainingSimCards = allSimCards;
            if (pooledDevices.Count > 0)
            {
                remainingSimCards = remainingSimCards.Except(pooledDevices).ToList();
                ProcessPooledDevices(context, integrationAuthenticationId, usesProration, revAccountNumber, AMOPCustomerId, autoChangeDisabledRatePlans, billingPeriod, instanceId, projectedDeviceIds, chargeType, pooledDevices, tenantId, serviceProviderIds);

                LogInfo(context, LogTypeConstant.Info, $"Processed {pooledDevices.Count} devices that have Rate plan with 'Auto Change Rate Plan' disabled.");
            }

            // Step 2 - Independent Devices (no pool but have a customer rate plan)
            var independentDevices = validDevices.Where(x => x.CustomerRatePoolId == null).ToList();
            if (independentDevices.Count > 0)
            {
                remainingSimCards = remainingSimCards.Except(independentDevices).ToList();
                ProcessUnpooledDevices(context, revAccountNumber, AMOPCustomerId, usesProration, autoChangeDisabledRatePlans, billingPeriod, instanceId, projectedDeviceIds, chargeType, independentDevices);
                LogInfo(context, LogTypeConstant.Info, $"Processed {independentDevices.Count} devices that have Rate plan with 'Auto Change Rate Plan' disabled.");
            }

            // Step 3 (Optional) - Calculate Bill in Advance Charges
            if (useBillInAdvance)
            {
                ProcessBillInAdvanceDevices(context, revAccountNumber, AMOPCustomerId, usesProration, autoChangeDisabledRatePlans, nextBillingPeriod, instanceId, validDevices);
                LogInfo(context, LogTypeConstant.Info, $"Processed bill in advance for {pooledDevices.Count + independentDevices.Count} devices that have Rate plan with 'Auto Change Rate Plan' disabled.");
            }

            return remainingSimCards;
        }

        protected void ProcessPooledDevices(KeySysLambdaContext context, int? integrationAuthenticationId, bool usesProration, string revAccountNumber, int? AMOPCustomerId, List<RatePlan> customerRatePlans, BillingPeriod billingPeriod, long instanceId, List<int> projectedDeviceIds, OptimizationChargeType chargeType, List<vwOptimizationSimCard> pooledDevices, int tenantId, string serviceProviderIds = null)
        {
            var poolIds = pooledDevices.GroupBy(x => x.CustomerRatePoolId)
                                                .Select(x => x.Key)
                                                .ToList();

            var customerType = string.IsNullOrEmpty(revAccountNumber) ? SiteTypes.AMOP : SiteTypes.Rev;

            LogInfo(context, LogTypeConstant.Info, $"PoolIds: {string.Join(',', poolIds)}");
            List<vwOptimizationSimCard> additionalSims = GetOptimizationSimCardsByPortalType(context, integrationAuthenticationId, revAccountNumber, AMOPCustomerId, billingPeriod, poolIds, customerType, tenantId, serviceProviderIds);

            // get by ids since query is faster with primary key
            var crossPooledCustomerRatePlanIds = additionalSims.GroupBy(s => s.CustomerRatePlanId).Select(s => s.Key.GetValueOrDefault()).ToList();

            var crossPooledCustomerRatePlans = new List<RatePlan>();
            if (crossPooledCustomerRatePlanIds.Count > 0)
            {
                crossPooledCustomerRatePlans = GetCrossCustomerRatePlans(context, crossPooledCustomerRatePlanIds);
                LogInfo(context, LogTypeConstant.Info, $"Cross Pooled Rate Plans: {string.Join(',', crossPooledCustomerRatePlans)}");
            }
            else
            {
                LogInfo(context, LogTypeConstant.Info, $"Found no additional customer rate plan from additional devices.");
            }

            // create new comm plan group
            var commPlanGroupId = CreateCommPlanGroup(context, instanceId);

            // filter out duplicated rate plans from the list above 
            // not modify customerRatePlans to not affect non-pooling sims
            crossPooledCustomerRatePlans = crossPooledCustomerRatePlans.Union(customerRatePlans).ToList();

            projectedDeviceIds.AddRange(pooledDevices.Select(x => x.Id));

            ProjectDataUsageAndSaveDeviceByPortalType(context, billingPeriod, instanceId, pooledDevices, commGroupId: commPlanGroupId);


            // Pooled device assignment and amount calculation
            CalculatePooledDevices(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId, revAccountNumber,
                crossPooledCustomerRatePlans, pooledDevices, usesProration, billingPeriod, chargeType, AMOPCustomerId, additionalSims);
        }

        public List<vwOptimizationSimCard> GetOptimizationSimCardsByPortalType(KeySysLambdaContext context, int? integrationAuthenticationId, string revAccountNumber, int? AMOPCustomerId, BillingPeriod billingPeriod, List<int?> poolIds, SiteTypes customerType, int tenantId, string serviceProviderIds = null)
        {
            var simCards = new List<vwOptimizationSimCard>();

            if (PortalType == PortalTypes.Mobility)
            {
                simCards = optimizationMobilityDeviceRepository.GetMobilityOptimizationSimCards(context, null, billingPeriod.ServiceProviderId, revAccountNumber, integrationAuthenticationId, billingPeriod.Id, tenantId, customerType, AMOPCustomerId, poolIds);
            }
            else if (PortalType == PortalTypes.M2M)
            {
                simCards = GetOptimizationSimCards(context, null, billingPeriod.ServiceProviderId, revAccountNumber, integrationAuthenticationId, billingPeriod.Id, tenantId, customerType, AMOPCustomerId, poolIds);
            }
            else if (PortalType == PortalTypes.CrossProvider)
            {
                simCards = crossProviderOptimizationRepository.GetCrossProviderCustomerSimCards(ParameterizedLog(context), customerType, AMOPCustomerId, revAccountNumber, integrationAuthenticationId, billingPeriod, serviceProviderIds, poolIds);
            }
            else
            {
                OptimizationErrorHandler.OnPortalTypeError(context, PortalType, true);
            }

            return simCards.Where(s => s.CustomerRatePlanId != null).ToList();
        }

        protected List<SimCard> ProjectDataUsageAndSaveDeviceByPortalType(KeySysLambdaContext context, BillingPeriod billingPeriod, long instanceId, List<vwOptimizationSimCard> devices, bool autoChangeRatePlan = false, long? commGroupId = null)
        {
            var simCards = new List<SimCard>();

            if (PortalType == PortalTypes.Mobility)
            {
                simCards = optimizationMobilityDeviceRepository.ProjectDataUsageAndSaveMobilityDevices(context, instanceId, devices, billingPeriod, autoChangeRatePlan, commGroupId);
            }
            else if (PortalType == PortalTypes.M2M)
            {
                simCards = ProjectDataUsageAndSaveDevices(context, instanceId, devices, billingPeriod, autoChangeRatePlan, commGroupId);
            }
            else if (PortalType == PortalTypes.CrossProvider)
            {
                // Group devices by portal type, then write to both OptimizationDevice & OptimizationMobilityDevice tables
                var simCardsByPortalTypes = devices.GroupBy(x => x.PortalType);
                foreach (var simCardByPortalType in simCardsByPortalTypes)
                {
                    if (simCardByPortalType.Key == PortalTypes.Mobility)
                    {
                        simCards.AddRange(optimizationMobilityDeviceRepository.ProjectDataUsageAndSaveMobilityDevices(context, instanceId, simCardByPortalType.ToList(), billingPeriod, autoChangeRatePlan, commGroupId));
                    }
                    else if (simCardByPortalType.Key == PortalTypes.M2M)
                    {
                        simCards.AddRange(ProjectDataUsageAndSaveDevices(context, instanceId, simCardByPortalType.ToList(), billingPeriod, autoChangeRatePlan, commGroupId));
                    }
                    else
                    {
                        OptimizationErrorHandler.OnPortalTypeError(context, simCardByPortalType.Key, true);
                    }
                }
            }
            else
            {
                OptimizationErrorHandler.OnPortalTypeError(context, PortalType, true);
            }
            return simCards;
        }

        private void CalculatePooledDevices(KeySysLambdaContext context, long instanceId, long commPlanGroupId, int? serviceProviderId,
            string revAccountNumber, List<RatePlan> customerRatePlans, List<vwOptimizationSimCard> pooledDevices, bool usesProration,
            BillingPeriod billingPeriod, OptimizationChargeType chargeType, int? amopCustomerId, List<vwOptimizationSimCard> additionalPooledDevices = null)
        {
            LogInfo(context, LogTypeConstant.Sub, $"(,{instanceId},{commPlanGroupId},{serviceProviderId},{revAccountNumber},customerRatePlans.Count:{customerRatePlans.Count},pooledDevices.Count:{pooledDevices.Count},{usesProration},{billingPeriod.Id},{chargeType},{amopCustomerId})");

            var queueId = CreateQueue(context, instanceId, commPlanGroupId, serviceProviderId, usesProration);

            string customerIdentifier;
            if (amopCustomerId == null)
            {
                customerIdentifier = revAccountNumber;
            }
            else
            {
                customerIdentifier = amopCustomerId.ToString();
            }
            LogInfo(context, LogTypeConstant.Info, $"Customer Identifier value: {customerIdentifier}");

            StartQueue(context, queueId, string.Empty);

            var logger = context.logger;

            var pools = CalculatePoolsForPooledDevices(pooledDevices, customerRatePlans, logger, billingPeriod, chargeType, customerIdentifier, additionalPooledDevices, PortalType);

            RecordResults(context, queueId, customerIdentifier, pools);

            // stop queue
            StopQueue(context, queueId);
        }

        public static List<CrossProviderCustomerRatePool> CalculatePoolsForPooledDevices(IReadOnlyCollection<vwOptimizationSimCard> pooledDevices, IReadOnlyCollection<RatePlan> customerRatePlans,
            IKeysysLogger logger, BillingPeriod billingPeriod, OptimizationChargeType chargeType, string customerIdentifier = null, List<vwOptimizationSimCard> additionalPooledDevices = null, PortalTypes portalType = PortalTypes.Mobility)
        {
            logger.LogInfo(LogTypeConstant.Sub, $"(pooledDevices.Count:{pooledDevices.Count},customerRatePlans.Count:{customerRatePlans.Count},billingPeriod.Id:{billingPeriod.Id},chargeType:{chargeType})");
            var pools = new List<CrossProviderCustomerRatePool>();
            if (pooledDevices != null && pooledDevices.Count > 0)
            {
                var groupedPooledDevices = pooledDevices.GroupBy(x => x.CustomerRatePoolId).ToList();
                var groupedAdditionalDevices = additionalPooledDevices?.GroupBy(x => x.CustomerRatePoolId);
                pools = new List<CrossProviderCustomerRatePool>(groupedPooledDevices.Count);

                foreach (var poolDeviceList in groupedPooledDevices)
                {
                    var poolId = poolDeviceList.Key;
                    var pool = new CrossProviderCustomerRatePool(billingPeriod, chargeType, portalType, customerIdentifier, true);

                    foreach (var device in poolDeviceList)
                    {
                        var skipDevice = false;

                        RatePlan customerRatePlan = default;
                        if (device.CustomerRatePlanId != null)
                        {
                            customerRatePlan = customerRatePlans.FirstOrDefault(x => x.Id == device.CustomerRatePlanId);

                            if (string.IsNullOrWhiteSpace(customerRatePlan.PlanName))
                            {
                                logger.LogInfo(LogTypeConstant.Warning, $"No Customer Rate Plan {device.CustomerRatePlanCode} found for this Device: {device.ICCID}");
                                skipDevice = true;
                            }
                        }
                        else
                        {
                            // WARN and exclude
                            logger.LogInfo(LogTypeConstant.Warning, $"No Customer Rate Plan for this Device: {device.ICCID}");
                            skipDevice = true;
                        }

                        // can we add this to the pool?
                        if (skipDevice)
                        {
                            logger.LogInfo(LogTypeConstant.Warning, $"Device skipped: {device.ICCID}");
                        }
                        else
                        {
                            pool.AddSimCard(new CrossProviderCustomerSimCard(device, customerRatePlan, chargeType));
                        }
                    }

                    if (groupedAdditionalDevices != null)
                    {
                        var additionalDevicePoolList = groupedAdditionalDevices?.FirstOrDefault(group =>
                                                            group.Key == poolId);
                        foreach (var additionalDevice in additionalDevicePoolList ?? Enumerable.Empty<vwOptimizationSimCard>())
                        {
                            var skipDevice = false;

                            RatePlan customerRatePlan = default;
                            if (additionalDevice.CustomerRatePlanId != null)
                            {
                                customerRatePlan = customerRatePlans.FirstOrDefault(x => x.Id == additionalDevice.CustomerRatePlanId);

                                if (string.IsNullOrWhiteSpace(customerRatePlan.PlanName))
                                {
                                    logger.LogInfo(LogTypeConstant.Warning, $"No Customer Rate Plan {additionalDevice.CustomerRatePlanCode} found for this additional device: {additionalDevice.ICCID}");
                                    skipDevice = true;
                                }
                            }
                            else
                            {
                                // WARN and exclude
                                logger.LogInfo(LogTypeConstant.Warning, $"No Customer Rate Plan for this additional device: {additionalDevice.ICCID}");
                                skipDevice = true;
                            }

                            // can we add this to the pool?
                            if (skipDevice)
                            {
                                logger.LogInfo(LogTypeConstant.Warning, $"Additional device skipped: {additionalDevice.ICCID}");
                            }
                            else
                            {
                                pool.AddAdditionalSimCard(new CrossProviderCustomerSimCard(additionalDevice, customerRatePlan, chargeType));
                            }
                        }
                    }

                    pool.CheckPoolLimit();

                    pools.Add(pool);
                }
            }

            return pools;
        }

        protected void ProcessUnpooledDevices(KeySysLambdaContext context, string revAccountNumber, int? amopCustomerId, bool usesProration, List<RatePlan> customerRatePlans, BillingPeriod billingPeriod, long instanceId, List<int> projectedDeviceIds, OptimizationChargeType chargeType, List<vwOptimizationSimCard> independentDevices)
        {
            projectedDeviceIds.AddRange(independentDevices.Select(x => x.Id));
            // 1. Create another comm plan group to have distinct (combined) results
            var commPlanGroupId = CreateCommPlanGroup(context, instanceId);

            ProjectDataUsageAndSaveDeviceByPortalType(context, billingPeriod, instanceId, independentDevices, commGroupId: commPlanGroupId);

            // 2. Independent device assignment and amount calculation
            CalculateUnpooledDevices(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId,
                revAccountNumber, customerRatePlans, independentDevices, usesProration, billingPeriod, chargeType, amopCustomerId);
        }

        private void CalculateUnpooledDevices(KeySysLambdaContext context, long instanceId, long commPlanGroupId, int serviceProviderId,
            string revAccountNumber, List<RatePlan> customerRatePlans, List<vwOptimizationSimCard> unpooledDevices, bool usesProration,
            BillingPeriod billingPeriod, OptimizationChargeType chargeType, int? amopCustomerId)
        {
            LogInfo(context, LogTypeConstant.Sub, $"(,{instanceId},{commPlanGroupId},{serviceProviderId},{revAccountNumber},customerRatePlans.Count:{customerRatePlans.Count},unpooledDevices.Count:{unpooledDevices.Count},{usesProration},{billingPeriod.Id},{chargeType},{amopCustomerId})");

            long queueId = CreateQueue(context, instanceId, commPlanGroupId, serviceProviderId, usesProration);
            StartQueue(context, queueId, string.Empty);
            string customerIdentifier;
            if (amopCustomerId == null)
            {
                customerIdentifier = revAccountNumber;
            }
            else
            {
                customerIdentifier = amopCustomerId.ToString();
            }
            LogInfo(context, LogTypeConstant.Info, $"Customer Identifier value: {customerIdentifier}");

            var logger = context.logger;

            var pool = CalculatePoolForUnpooledDevices(unpooledDevices, customerRatePlans, logger, billingPeriod, chargeType, PortalType);

            RecordResults(context, queueId, customerIdentifier, pool);

            // stop queue
            StopQueue(context, queueId);
        }

        public static CrossProviderCustomerRatePool CalculatePoolForUnpooledDevices(List<vwOptimizationSimCard> unpooledDevices, IReadOnlyCollection<RatePlan> customerRatePlans,
            IKeysysLogger logger, BillingPeriod billingPeriod, OptimizationChargeType chargeType, PortalTypes portalType = PortalTypes.Mobility)
        {
            var pool = new CrossProviderCustomerRatePool(billingPeriod, chargeType, portalType);

            foreach (var device in unpooledDevices)
            {
                bool skipDevice = false;

                RatePlan customerRatePlan = default;
                if (device.CustomerRatePlanId != null)
                {
                    customerRatePlan = customerRatePlans.FirstOrDefault(x => x.Id == device.CustomerRatePlanId);

                    if (string.IsNullOrWhiteSpace(customerRatePlan.PlanName))
                    {
                        logger.LogInfo(LogTypeConstant.Warning, $"No Customer Rate Plan {device.CustomerRatePlanCode} found for this Device ICCID: {device.ICCID}");
                        skipDevice = true;
                    }
                }
                else
                {
                    // WARN and exclude
                    logger.LogInfo(LogTypeConstant.Warning, $"No Customer Rate Plan for this Device ICCID: {device.ICCID}");
                    skipDevice = true;
                }

                // can we add this to the pool?
                if (skipDevice)
                {
                    logger.LogInfo(LogTypeConstant.Warning, $"Device skipped: {device.ICCID}");
                }
                else
                {
                    pool.AddSimCard(new CrossProviderCustomerSimCard(device, customerRatePlan, chargeType, true));
                }
            }

            return pool;
        }

        protected void ProcessBillInAdvanceDevices(KeySysLambdaContext context, string revAccountNumber, int? amopCustomerId, bool usesProration, List<RatePlan> customerRatePlans, BillingPeriod nextBillingPeriod, long instanceId, List<vwOptimizationSimCard> optimizationSimCards)
        {
            var devicesWithCustomerRatePlans = optimizationSimCards.Where(x => x.CustomerRatePlanId != null).ToList();

            var billInAdvanceRatePlans = customerRatePlans.Where(x => x.IsBillInAdvanceEligible).ToList();
            if (billInAdvanceRatePlans.Count == 0)
            {
                LogInfo(context, LogTypeConstant.Warning, "Expected Bill In Advance Rate Plans and SIM Cards, but found no eligible rate plans.");
            }
            else
            {
                if (devicesWithCustomerRatePlans.Count > 0)
                {
                    var advanceCommPlanGroupId = CreateCommPlanGroup(context, instanceId);

                    CalculateAdvanceBillingDevices(context, instanceId, advanceCommPlanGroupId, nextBillingPeriod.ServiceProviderId,
                        revAccountNumber, amopCustomerId, customerRatePlans, devicesWithCustomerRatePlans, usesProration, nextBillingPeriod, OptimizationChargeType.RateChargeOnly);

                }
                else
                {
                    LogInfo(context, LogTypeConstant.Info, "Found no device with a customer rate plan assigned.");
                }
            }
        }

        // For logging in generic function
        public static Action<string, string> ParameterizedLog(KeySysLambdaContext context)
        {
            return (type, message) => LogInfo(context, type, message);
        }

        public static string GetStringValueFromEnvironmentVariable(ILambdaContext context, IEnvironmentRepository environmentRepo, string key)
        {
            var stringValue = environmentRepo.GetEnvironmentVariable(context, key);
            if (string.IsNullOrWhiteSpace(stringValue))
            {
                throw new InvalidOperationException(string.Format(LogCommonStrings.ENVIRONMENT_VARIABLE_NOT_CONFIGURED, key));
            }
            return stringValue;
        }

        public static int GetIntValueFromEnvironmentVariable(KeySysLambdaContext context, IEnvironmentRepository environmentRepo, string variableKey, int defaultValue)
        {
            var stringValue = GetStringValueFromEnvironmentVariable(context.LambdaContext, environmentRepo, variableKey);
            var isParseSuccess = int.TryParse(stringValue, out int valueFromEnvironment);
            if (!isParseSuccess || valueFromEnvironment <= 0)
            {
                LogInfo(context, CommonConstants.WARNING, string.Format(LogCommonStrings.INVALID_CONFIGURED_VALUE_FOR_VARIABLE, stringValue, defaultValue));
                valueFromEnvironment = defaultValue;
            }
            return valueFromEnvironment;
        }

        public void InitializeRepositories(ILambdaContext context, KeySysLambdaContext amopContext)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(amopContext);
            serviceProviderRepository = new ServiceProviderRepository(amopContext.logger, amopContext.EnvironmentRepo, context);
            carrierRatePlanRepository = new CarrierRatePlanRepository(amopContext.ConnectionString, amopContext.logger);
            customerRatePlanRepository = new CustomerRatePlanRepository(amopContext.ConnectionString, amopContext.logger);
            crossProviderOptimizationRepository = new CrossProviderOptimizationRepository(amopContext.ConnectionString, amopContext.logger);
        }

        protected List<RatePlan> MapRatePlansToOptimizationGroup(List<RatePlan> ratePlans, OptimizationGroup optimizationGroup)
        {
            var ratePlanIdList = optimizationGroup.RatePlanIds.Split(',').ToList();
            List<RatePlan> groupRatePlans = new List<RatePlan>();
            foreach (var planId in ratePlanIdList)
            {
                var ratePlan = ratePlans.FirstOrDefault(x => x.Id.ToString() == planId);
                if (ratePlan.Id.ToString() == planId)
                {
                    groupRatePlans.Add(ratePlan);
                }
            }

            return groupRatePlans;
        }

        protected bool CheckZeroValueRatePlans(KeySysLambdaContext context, long instanceId, List<RatePlan> groupRatePlans, bool shouldStopInstance)
        {
            var zeroValueRatePlans = groupRatePlans.Where(groupRatePlan => groupRatePlan.DataPerOverageCharge <= 0 || groupRatePlan.OverageRate <= 0);
            var isInvalid = zeroValueRatePlans?.Count() > 0;
            if (isInvalid)
            {
                var zeroValueRatePlanNames = string.Join(", ", zeroValueRatePlans.Select(x => x.PlanDisplayName));
                LogInfo(context, CommonConstants.ERROR, string.Format(LogCommonStrings.ZERO_VALUES_RATE_PLANS_MESSAGE, zeroValueRatePlanNames));
                if (shouldStopInstance)
                {
                    StopOptimizationInstance(context, instanceId, OptimizationStatus.CompleteWithErrors);
                }
            }
            return isInvalid;
        }

        protected static void LogVariableValue(KeySysLambdaContext context, string variableName, object variableValue)
        {
            LogInfo(context, CommonConstants.INFO, $"{variableName}: {variableValue}");
        }

        protected static OptimizationChargeType GetChargeType(bool useBillInAdvance)
        {
            var chargeType = OptimizationChargeType.RateChargeAndOverage;
            if (useBillInAdvance)
            {
                chargeType = OptimizationChargeType.OverageOnly;
            }

            return chargeType;
        }

        protected async Task SendRunOptimizerMessage(KeySysLambdaContext context, RatePlanSequence[] sequences, int queuesPerInstance)
        {
            // get queues
            var queueIds = sequences.Select(x => x.QueueId).ToList();

            if (queueIds != null && queueIds.Count > 0)
            {
                var sendCount = 0;
                var queueIdsToSend = new List<string>();
                foreach (var queueId in queueIds)
                {
                    sendCount++;
                    queueIdsToSend.Add(queueId.ToString());

                    if (sendCount % queuesPerInstance == 0)
                    {
                        await SendQueueToSqsAsync(context, queueIdsToSend, skipLowerCostCheck: false, OptimizationChargeType.RateChargeAndOverage, false);
                        queueIdsToSend = new List<string>();
                        sendCount = 0;
                    }
                }

                if (sendCount > 0)
                {
                    await SendQueueToSqsAsync(context, queueIdsToSend, skipLowerCostCheck: false, OptimizationChargeType.RateChargeAndOverage, false);
                }
            }
        }

        protected async Task<bool> ProcessRatePoolGroup(KeySysLambdaContext context, int? integrationAuthenticationId, bool usesProration, string revAccountNumber, int? AMOPCustomerId, BillingPeriod billingPeriod, long instanceId, OptimizationChargeType chargeType, IEnumerable<RatePlan> ratePlans, List<vwOptimizationSimCard> optimizationSimCards, int? ratePoolId, int queuesPerInstance)
        {
            foreach (var ratePlanGroup in ratePlans.GroupBy(x => x.AllowsSimPooling))
            {
                LogVariableValue(context, nameof(ratePoolId), ratePoolId);
                LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.ALLOW_SIM_POOLING_MESSAGE, ratePlanGroup.Key));

                // Get rate plans for group
                var groupRatePlans = ratePlanGroup.ToList();
                if (CheckZeroValueRatePlans(context, instanceId, groupRatePlans, shouldStopInstance: true))
                {
                    return true;
                }

                // Filter rate plans that are used for auto change rate plan
                if (optimizationSimCards.Count == 0)
                {
                    // No more devices to process the next steps for this rate plan group
                    // If there are devices but no rate plans, the devices could be unassigned devices so it is expected
                    LogInfo(context, LogTypeConstant.Info, string.Format("No device found for this rate plan group {0}", ratePlanGroup.Key));
                    continue;
                }
                // Create new comm plan group
                var commPlanGroupId = CreateCommPlanGroup(context, instanceId);
                var calculatedPlans = RatePoolCalculator.CalculateMaxAvgUsage(groupRatePlans, null);
                var ratePools = RatePoolFactory.CreateRatePools(calculatedPlans, billingPeriod, usesProration, chargeType);
                var ratePoolCollection = RatePoolCollectionFactory.CreateRatePoolCollection(ratePools, ratePlanGroup.Key, ratePoolId: ratePoolId);

                var baseAssignedSimCardsCount = BaseDeviceAssignment(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId,
                    revAccountNumber, integrationAuthenticationId, null, ratePoolCollection, ratePools, optimizationSimCards, billingPeriod, usesProration, AMOPCustomerId);
                // Add rate plans to comm plan group
                var commGroupRatePlanTable = AddCustomerRatePlansToCommPlanGroup(context, instanceId, commPlanGroupId, calculatedPlans);

                // Add the customer rate pool to comm group
                optimizationRepository.AddCustomerRatePoolToCommGroup(context, instanceId, commPlanGroupId, ratePoolId);

                // Zero sim card => no need to run optimizer
                // One sim card => swapping between rate plans would be the same as base device assignment
                //              => already calculate that => no need to run optimizer
                if (baseAssignedSimCardsCount > OptimizationConstant.BaseAssignedDeviceLimit)
                {
                    // Permute rate plans
                    if (calculatedPlans.Count > OptimizationConstant.CustomerOptimizationPoolRatePlanLimit)
                    {
                        LogInfo(context, CommonConstants.WARNING, string.Format(LogCommonStrings.CUSTOMER_POOL_RATE_PLAN_LIMIT_ERROR, OptimizationConstant.CustomerOptimizationPoolRatePlanLimit, ratePlanGroup.Key));
                        continue;
                    }
                    if (calculatedPlans.Count <= OptimizationConstant.RatePlanMinimumLimit)
                    {

                        LogInfo(context, CommonConstants.INFO, string.Format(LogCommonStrings.AUTO_CHANGE_MINIMUM_RATE_PLAN_LIMIT_REACHED, calculatedPlans.Count, ratePoolId, ratePlanGroup.Key));
                        continue;
                    }
                    GeneratePermutationQueueRatePlans(context, usesProration, billingPeriod, instanceId, commPlanGroupId, ratePoolCollection, commGroupRatePlanTable);

                    // Enqueue rate plan permutations
                    await EnqueueOptimizationRunsAsync(context, instanceId, new List<long>() { commPlanGroupId }, chargeType, queuesPerInstance, skipLowerCostCheck: true, isCustomerOptimization: true);
                }
                else
                {
                    LogInfo(context, LogTypeConstant.Info, string.Format(LogCommonStrings.NOT_ENOUGH_DEVICE_FOR_OPTIMIZATION_USING_PERMUTATION_FOR_RATE_POOL, string.Join(',', ratePlanGroup.Select(plan => plan.Id).ToList()), baseAssignedSimCardsCount));
                }
            }
            return false;
        }


        protected void GeneratePermutationQueueRatePlans(KeySysLambdaContext context, bool usesProration, BillingPeriod billingPeriod, long instanceId, long commPlanGroupId, RatePoolCollection ratePoolCollection, DataTable commGroupRatePlanTable)
        {
            LogInfo(context, CommonConstants.SUB, detail: $"Start permutation for {ratePoolCollection.RatePools.Count} Rate Plans");
            List<RatePlanSequence> ratePoolSequences;
            if (ratePoolCollection.RatePoolId != null && ratePoolCollection.RatePoolId > 0)
            {
                ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequencesByRatePlanCodes(ratePoolCollection.RatePools);
            }
            else
            {
                ratePoolSequences = RatePoolAssigner.GenerateRatePoolSequences(ratePoolCollection.RatePools);
            }

            LogInfo(context, CommonConstants.SUB, "End");
            DataTable dtQueueRatePlan = GenerateQueueRatePlanMappingDataTable(context, usesProration, billingPeriod, instanceId, commPlanGroupId, commGroupRatePlanTable, ratePoolSequences);

            CreateQueueRatePlans(context, dtQueueRatePlan);
        }

        private DataTable GenerateQueueRatePlanMappingDataTable(KeySysLambdaContext context, bool usesProration, BillingPeriod billingPeriod, long instanceId, long commPlanGroupId, DataTable commGroupRatePlanTable, List<RatePlanSequence> ratePoolSequences)
        {
            var dtQueueRatePlan = new DataTable();
            dtQueueRatePlan.Columns.Add(CommonColumnNames.QueueId, typeof(long));
            dtQueueRatePlan.Columns.Add(CommonColumnNames.CommGroupRatePlanId, typeof(long));
            dtQueueRatePlan.Columns.Add(CommonColumnNames.SequenceOrder, typeof(int));
            dtQueueRatePlan.Columns.Add(CommonColumnNames.CreatedBy);
            dtQueueRatePlan.Columns.Add(CommonColumnNames.CreatedDate, typeof(DateTime));

            foreach (var ratePoolSequence in ratePoolSequences)
            {
                // Add queue for rate plan permutation
                var queueId = CreateQueue(context, instanceId, commPlanGroupId, billingPeriod.ServiceProviderId, usesProration);

                // Add rate plans to queue
                var dtQueueRatePlanTemp = AddRatePlansToQueue(queueId, ratePoolSequence, commGroupRatePlanTable);
                if (dtQueueRatePlanTemp?.Rows.Count > 0)
                {
                    foreach (DataRow dr in dtQueueRatePlanTemp.Rows)
                    {
                        dtQueueRatePlan.Rows.Add(dr.ItemArray);
                    }
                }
            }

            return dtQueueRatePlan;
        }
    }
}