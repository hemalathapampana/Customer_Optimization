using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Altaworx.SimCard.Cost.Optimizer.Core.Helpers;
using Altaworx.SimCard.Cost.Optimizer.Core.Models;
using Altaworx.SimCard.Cost.Optimizer.Core.Repositories.CarrierRatePlan;
using Amop.Core.Constants;
using Amop.Core.Helpers;
using Amop.Core.Models;
using Amop.Core.Resilience;
using Microsoft.Data.SqlClient;
using LogTypeConstant = Altaworx.AWS.Core.Helpers.Constants.LogTypeConstant;

namespace Altaworx.SimCard.Cost.Optimizer.Core.Repositories.Optimization
{
    public class OptimizationMobilityDeviceRepository : IOptimizationMobilityDeviceRepository
    {
        #region public methods
        public List<vwOptimizationSimCard> GetMobilityOptimizationSimCardsWithRetry(KeySysLambdaContext context, List<int> optimizationGroupIds, int? serviceProviderId, string revAccountNumber, int? integrationAuthenticationId, int billingPeriodId, int tenantId, SiteTypes siteType = SiteTypes.Rev, int? amopCustomerId = null, List<int?> poolIds = null, bool isCarrierOptimization = false)
        {

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(CommonConstants.NUMBER_OF_RETRIES);
            var simCards = new List<vwOptimizationSimCard>();
            sqlRetryPolicy.Execute(() =>
            {
                simCards = GetMobilityOptimizationSimCards(context, optimizationGroupIds, serviceProviderId, revAccountNumber, integrationAuthenticationId, billingPeriodId, tenantId, siteType, amopCustomerId, poolIds, isCarrierOptimization);
            });
            return simCards;
        }

        public List<vwOptimizationSimCard> GetMobilityOptimizationSimCards(KeySysLambdaContext context, List<int> optimizationGroupIds, int? serviceProviderId, string revAccountNumber, int? integrationAuthenticationId, int billingPeriodId, int tenantId, SiteTypes siteType = SiteTypes.Rev, int? amopCustomerId = null, List<int?> poolIds = null, bool isCarrierOptimization = false)
        {
            AwsFunctionBase.LogInfo(context, LogTypeConstant.Sub, $"({serviceProviderId},{revAccountNumber},{integrationAuthenticationId},{billingPeriodId},{nameof(tenantId)}: {tenantId},{siteType}, {nameof(amopCustomerId)}: {amopCustomerId}, {nameof(poolIds)}: {(poolIds != null ? string.Join(',', poolIds) : string.Empty)}, {nameof(isCarrierOptimization)}: {isCarrierOptimization})");
            try
            {
                var simCards = new List<vwOptimizationSimCard>();
                using (var conn = new SqlConnection(context.ConnectionString))
                {
                    using (var cmd = new SqlCommand("usp_Optimization_Mobility_SimCardsGet", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@BillingPeriodId", billingPeriodId);
                        cmd.Parameters.AddWithValue("@SiteType", (int)siteType);
                        cmd.Parameters.AddWithValue(CommonSQLParameterNames.TENANT_ID, tenantId);

                        if (string.IsNullOrWhiteSpace(revAccountNumber))
                        {
                            cmd.Parameters.AddWithValue("@RevAccountNumber", DBNull.Value);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@RevAccountNumber", revAccountNumber);
                        }

                        if (amopCustomerId == null)
                        {
                            cmd.Parameters.AddWithValue("@AMOPCustomerId", DBNull.Value);
                        }
                        else
                        {
                            cmd.Parameters.AddWithValue("@AMOPCustomerId", amopCustomerId.Value);
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
                        cmd.Parameters.AddWithValue(CommonSQLParameterNames.IS_CARRIER_OPTIMIZATION, isCarrierOptimization);
                        cmd.CommandTimeout = SQLConstant.ShortTimeoutSeconds;
                        conn.Open();

                        var rdr = cmd.ExecuteReader();
                        while (rdr.Read())
                        {
                            var simCard = MobilityOptimizationSimCardFromReader(rdr);
                            if (optimizationGroupIds == null || optimizationGroupIds.Count == 0 || optimizationGroupIds.Contains(simCard.OptimizationGroupId.GetValueOrDefault()))
                            {
                                simCards.Add(simCard);
                            }
                        }
                    }
                }

                return simCards;
            }
            catch (SqlException ex)
            {
                AwsFunctionBase.LogInfo(context, LogTypeConstant.Exception, $"Exception when executing stored procedure: {ex.Message}, ErrorCode:{ex.ErrorCode}-{ex.Number}, Stack Trace: {ex.StackTrace}");
                throw ex;
            }
            catch (InvalidOperationException ex)
            {
                AwsFunctionBase.LogInfo(context, LogTypeConstant.Exception, $"Exception when connecting to database: {ex.Message}, Stack Trace: {ex.StackTrace}");
                throw ex;
            }
            catch (Exception ex)
            {
                AwsFunctionBase.LogInfo(context, LogTypeConstant.Exception, $"Exception when getting mobility SIM cards for customer {(siteType == SiteTypes.Rev ? revAccountNumber : amopCustomerId.ToString())} : {ex.Message}, Stack Trace: {ex.StackTrace}");
                throw ex;
            }
        }

        protected vwOptimizationSimCard MobilityOptimizationSimCardFromReader(SqlDataReader dataReader)
        {
            var columns = dataReader.GetColumnsFromReader();
            return new vwOptimizationSimCard
            {
                Id = int.Parse(dataReader[vwOptimizationSimCardColumnNames.DeviceId].ToString()),
                ICCID = dataReader[vwOptimizationSimCardColumnNames.ICCID].ToString(),
                CommunicationPlan = dataReader[vwOptimizationSimCardColumnNames.CommunicationPlan].ToString(),
                // usage is really in bytes, must convert to MB
                CycleDataUsageMB = dataReader.GetInt32(CommonColumnNames.IntegrationId) == (int)IntegrationType.Teal
                    ? Math.Round(long.Parse(dataReader[vwOptimizationSimCardColumnNames.CtdDataUsage].ToString()) / CommonConstants.TEAL_BYTE_CONVERSION_VALUE / CommonConstants.TEAL_BYTE_CONVERSION_VALUE, 3)
                    : Math.Round(long.Parse(dataReader[vwOptimizationSimCardColumnNames.CtdDataUsage].ToString()) / CommonConstants.DEFAULT_BYTE_CONVERSION_VALUE / CommonConstants.DEFAULT_BYTE_CONVERSION_VALUE, 3),
                MSISDN = dataReader[vwOptimizationSimCardColumnNames.MSISDN].ToString(),
                Status = dataReader[vwOptimizationSimCardColumnNames.Status].ToString(),
                UsageDate = !dataReader.IsDBNull(vwOptimizationSimCardColumnNames.UsageDate) ?
                        DateTime.Parse(dataReader[vwOptimizationSimCardColumnNames.UsageDate].ToString()) : new DateTime?(),
                CustomerRatePlanCode = dataReader[vwOptimizationSimCardColumnNames.CustomerRatePlanCode].ToString(),
                ServiceProviderId = int.Parse(dataReader[vwOptimizationSimCardColumnNames.ServiceProviderId].ToString()),
                IntegrationAuthenticationId = !dataReader.IsDBNull(vwOptimizationSimCardColumnNames.AccountNumberIntegrationAuthenticationId) ?
                                            int.Parse(dataReader[vwOptimizationSimCardColumnNames.AccountNumberIntegrationAuthenticationId].ToString()) : 0,
                FoundationAccountNumber = dataReader[vwOptimizationSimCardColumnNames.FoundationAccountNumber].ToString(),
                BillingAccountNumber = dataReader[vwOptimizationSimCardColumnNames.BillingAccountNumber].ToString(),
                RevAccountNumber = dataReader[vwOptimizationSimCardColumnNames.RevAccountNumber].ToString(),
                CarrierRatePlanCode = !dataReader.IsDBNull(vwOptimizationSimCardColumnNames.CarrierRatePlanCode) ?
                                    dataReader[vwOptimizationSimCardColumnNames.CarrierRatePlanCode].ToString() : "",
                CustomerDataAllocationMB = !dataReader.IsDBNull(vwOptimizationSimCardColumnNames.CustomerDataAllocationMB) ?
                                        decimal.Parse(dataReader[vwOptimizationSimCardColumnNames.CustomerDataAllocationMB].ToString()) : (decimal?)null,
                CustomerRatePlanId = !dataReader.IsDBNull(vwOptimizationSimCardColumnNames.CustomerRatePlanId) ?
                                int.Parse(dataReader[vwOptimizationSimCardColumnNames.CustomerRatePlanId].ToString()) : (int?)null,
                CustomerRatePoolId = !dataReader.IsDBNull(vwOptimizationSimCardColumnNames.CustomerRatePoolId) ?
                                int.Parse(dataReader[vwOptimizationSimCardColumnNames.CustomerRatePoolId].ToString()) : (int?)null,
                CustomerRatePlanMB = !dataReader.IsDBNull(vwOptimizationSimCardColumnNames.CustomerRatePlanMB) ?
                                decimal.Parse(dataReader[vwOptimizationSimCardColumnNames.CustomerRatePlanMB].ToString()) : (decimal?)null,
                ProviderDateActivated = !dataReader.IsDBNull(vwOptimizationSimCardColumnNames.ProviderDateActivated) ?
                                DateTime.Parse(dataReader[vwOptimizationSimCardColumnNames.ProviderDateActivated].ToString()) : (DateTime?)null,
                SmsUsage = !dataReader.IsDBNull(vwOptimizationSimCardColumnNames.SmsUsage) ? dataReader.GetInt64(vwOptimizationSimCardColumnNames.SmsUsage) : 0,
                AmopCustomerId = int.Parse(dataReader[vwOptimizationSimCardColumnNames.SiteId].ToString()),
                OptimizationGroupId = dataReader.NullableIntFromReader(columns, CommonColumnNames.OptimizationGroupId),
                RatePlanTypeId = dataReader.NullableIntFromReader(columns, CommonColumnNames.OptimizationRatePlanTypeId)
            };
        }

        public List<SimCard> ProjectDataUsageAndSaveMobilityDevices(KeySysLambdaContext context, long instanceId, List<vwOptimizationSimCard> optimizationSimCards, BillingPeriod billingPeriod, bool autoChangeRatePlan, long? commGroupId = null)
        {
            AwsFunctionBase.LogInfo(context, CommonConstants.SUB, $"{instanceId},{optimizationSimCards?.Count},{billingPeriod?.Id}");
            List<SimCard> simCards = new List<SimCard>();

            try
            {
                ArgumentNullException.ThrowIfNull(context);
                ArgumentNullException.ThrowIfNull(optimizationSimCards);
                ArgumentNullException.ThrowIfNull(billingPeriod);

                DataTable optimizationMobilityDeviceTable = CreateOptimizationMobilityDeviceDataTable();

                for (int deviceIndex = 0; deviceIndex < optimizationSimCards?.Count; deviceIndex++)
                {
                    var deviceRow = optimizationMobilityDeviceTable.NewRow();

                    var optSimCard = optimizationSimCards[deviceIndex];
                    decimal projectedDataUsage = AwsFunctionBase.ProjectDataUsage(optSimCard.CycleDataUsageMB, optSimCard.Status, optSimCard.UsageDate, billingPeriod.BillingPeriodStart, billingPeriod.BillingPeriodEnd, billingPeriod.BillingTimeZone);
                    deviceRow[OptimizationMobilityDeviceColumnNames.InstanceId] = instanceId;
                    deviceRow[OptimizationMobilityDeviceColumnNames.DeviceId] = DBNull.Value;
                    deviceRow[OptimizationMobilityDeviceColumnNames.CycleDataUsageMB] = optSimCard.CycleDataUsageMB;
                    deviceRow[OptimizationMobilityDeviceColumnNames.ProjectedDataUsageMB] = projectedDataUsage;
                    deviceRow[OptimizationMobilityDeviceColumnNames.CommunicationPlan] = optSimCard.CommunicationPlan;
                    deviceRow[OptimizationMobilityDeviceColumnNames.MSISDN] = optSimCard.MSISDN;
                    deviceRow[OptimizationMobilityDeviceColumnNames.ICCID] = optSimCard.ICCID;
                    deviceRow[OptimizationMobilityDeviceColumnNames.UsageDate] = optSimCard.UsageDate;
                    deviceRow[OptimizationMobilityDeviceColumnNames.CreatedBy] = OptimizationConstant.DefaultMobilityCreatedByName;
                    deviceRow[OptimizationMobilityDeviceColumnNames.CreatedDate] = DateTime.UtcNow;
                    deviceRow[OptimizationMobilityDeviceColumnNames.AmopDeviceId] = optSimCard.Id;
                    deviceRow[OptimizationMobilityDeviceColumnNames.ServiceProviderId] = optSimCard.ServiceProviderId;
                    deviceRow[CommonColumnNames.OptimizationRatePlanTypeId] = (object)optSimCard.RatePlanTypeId ?? DBNull.Value;
                    deviceRow[CommonColumnNames.OptimizationGroupId] = (object)optSimCard.OptimizationGroupId ?? DBNull.Value;
                    deviceRow[CommonColumnNames.AutoChangeRatePlan] = autoChangeRatePlan;
                    deviceRow[CommonColumnNames.OptimizationCommGroupId] = (object)commGroupId ?? DBNull.Value;
                    deviceRow[CommonColumnNames.RatePlanCode] = (object)optSimCard.CustomerRatePlanCode ?? DBNull.Value;
                    if (optSimCard.ProviderDateActivated != null)
                    {
                        deviceRow[OptimizationMobilityDeviceColumnNames.DateActivated] = optSimCard.ProviderDateActivated;
                    }
                    else
                    {
                        deviceRow[OptimizationMobilityDeviceColumnNames.DateActivated] = DBNull.Value;
                    }

                    optSimCard.CycleDataUsageMB = projectedDataUsage;
                    var simCard = AwsFunctionBase.SimCardFromOptimizationSimCard(optSimCard, billingPeriod);
                    simCards.Add(simCard);

                    deviceRow[OptimizationMobilityDeviceColumnNames.WasActivatedInThisBillingPeriod] = simCard.WasActivatedInThisBillingPeriod;
                    deviceRow[OptimizationMobilityDeviceColumnNames.DaysActivatedInBillingPeriod] = simCard.DaysActivatedInBillingPeriod;
                    deviceRow[OptimizationMobilityDeviceColumnNames.SmsUsage] = simCard.SmsUsage;

                    optimizationMobilityDeviceTable.Rows.Add(deviceRow);
                    optimizationSimCards[deviceIndex] = optSimCard;
                }

                List<SqlBulkCopyColumnMapping> columnMappings = SQLBulkCopyHelper.AutoMapColumns(optimizationMobilityDeviceTable);

                var logMessage = SqlHelper.SqlBulkCopy(context.ConnectionString, optimizationMobilityDeviceTable, "OptimizationMobilityDevice", columnMappings);

                if (logMessage != null)
                {
                    AwsFunctionBase.LogInfo(context, LogTypeConstant.Error, logMessage);
                }
                else
                {
                    AwsFunctionBase.LogInfo(context, LogTypeConstant.Info, $"Complete recording {simCards.Count} devices to database");
                }
            }
            catch (SqlException ex)
            {
                AwsFunctionBase.LogInfo(context, CommonConstants.EXCEPTION, string.Format(LogCommonStrings.EXCEPTION_WHEN_EXECUTING_SQL_COMMAND, string.Join(". ", ex.Message, ex.ErrorCode, ex.Number, ex.StackTrace)));
            }
            catch (InvalidOperationException ex)
            {
                AwsFunctionBase.LogInfo(context, CommonConstants.EXCEPTION, string.Format(LogCommonStrings.EXCEPTION_WHEN_CONNECTING_DATABASE, string.Join(". ", ex.Message, ex.StackTrace)));
            }
            catch (Exception ex)
            {
                AwsFunctionBase.LogInfo(context, CommonConstants.EXCEPTION, string.Join(". ", ex.Message, ex.StackTrace));
            }

            return simCards;
        }

        public int GetExpectedOptimizationSimCardCount(KeySysLambdaContext context, int serviceProviderId, string revAccountNumber, int billingPeriodId, bool isCarrierOptimization, int? integrationAuthenticationId, int tenantId)
        {
            AwsFunctionBase.LogInfo(context, CommonConstants.SUB, $"({serviceProviderId},{revAccountNumber},{billingPeriodId},{integrationAuthenticationId})");

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(CommonConstants.NUMBER_OF_RETRIES);
            var result = 0;
            var parameters = new List<SqlParameter>()
            {
                new SqlParameter(CommonSQLParameterNames.SERVICE_PROVIDER_ID_PASCAL_CASE, serviceProviderId),
                new SqlParameter(CommonSQLParameterNames.REV_ACCOUNT_NUMBER, (object)revAccountNumber ?? DBNull.Value),
                new SqlParameter(CommonSQLParameterNames.BILLING_PERIOD_ID, billingPeriodId),
                new SqlParameter(CommonSQLParameterNames.IS_CARRIER_OPTIMIZATION, isCarrierOptimization),
                new SqlParameter(CommonSQLParameterNames.INTEGRATION_AUTHENTICATION_ID, (object)integrationAuthenticationId ?? DBNull.Value),
                new SqlParameter(CommonSQLParameterNames.TENANT_ID, tenantId),
            };
            sqlRetryPolicy.Execute(() =>
            {
                result = SqlQueryHelper.ExecuteStoredProcedureWithIntResult(AwsFunctionBase.ParameterizedLog(context), context.ConnectionString,
                    SQLConstant.StoredProcedureName.GET_OPTIMIZATION_MOBILITY_SIM_CARDS_COUNT,
                    parameters, SQLConstant.ShortTimeoutSeconds,
                    shouldThrowOnException: true);
            });
            return result;
        }

        // To match existing implementation of carrier optimization where each queue is mapped to a comm group
        public void AddOptimizationGroupToCollection(KeySysLambdaContext context, long instanceId, long commGroupId, OptimizationGroup optimizationGroup)
        {
            AwsFunctionBase.LogInfo(context, CommonConstants.SUB, $"{nameof(instanceId)},{instanceId}");

            DataTable table = new DataTable();
            table.Columns.Add(CommonColumnNames.InstanceId, typeof(long));
            table.Columns.Add(CommonColumnNames.CommGroupId, typeof(long));
            table.Columns.Add(CommonColumnNames.OptimizationGroupId, typeof(int));
            table.Columns.Add(CommonColumnNames.OptimizationGroupName, typeof(string));
            table.Columns.Add(CommonColumnNames.CreatedBy);
            table.Columns.Add(CommonColumnNames.CreatedDate, typeof(DateTime));


            var dataRow = table.NewRow();

            dataRow[CommonColumnNames.InstanceId] = instanceId;
            dataRow[CommonColumnNames.CommGroupId] = commGroupId;
            dataRow[CommonColumnNames.OptimizationGroupId] = optimizationGroup.Id;
            dataRow[CommonColumnNames.OptimizationGroupName] = optimizationGroup.Name;
            dataRow[CommonColumnNames.CreatedBy] = OptimizationConstant.DefaultMobilityCreatedByName;
            dataRow[CommonColumnNames.CreatedDate] = DateTime.UtcNow;

            table.Rows.Add(dataRow);

            List<SqlBulkCopyColumnMapping> columnMappings = SQLBulkCopyHelper.AutoMapColumns(table);

            SqlHelper.SqlBulkCopy(context.ConnectionString, table, DatabaseTableNames.OPTIMIZATION_COMM_GROUP_OPTIMIZATION_GROUP, columnMappings);
        }

        public void RecordResults(KeySysLambdaContext context, long queueId, string revAccountNumber, OptimizationResult result)
        {
            AwsFunctionBase.LogInfo(context, CommonConstants.SUB, $"({queueId},{revAccountNumber})");
            var logMessages = OptimizationResultDbWriter.RecordResults(context, context.ConnectionString, queueId, revAccountNumber, result);
            if (logMessages != null && logMessages.Count > 0)
            {
                foreach (var message in logMessages)
                {
                    AwsFunctionBase.LogInfo(context, CommonConstants.INFO, message);
                }
            }
        }
        /// <summary>
        ///     Get data from OptimizationMobilityDevice table with an sql retry logic
        /// </summary>
        /// <param name="context"></param>
        /// <param name="optimizationGroupIds"></param>
        /// <param name="serviceProviderId"></param>
        /// <param name="revAccountNumber"></param>
        /// <param name="integrationAuthenticationId"></param>
        /// <param name="billingPeriodId"></param>
        /// <param name="siteType"></param>
        /// <param name="amopCustomerId"></param>
        /// <param name="poolIds"></param>
        /// <param name="isCarrierOptimization"></param>
        /// <returns></returns>
        public List<SimCard> GetOptimizationMobilityDevices(KeySysLambdaContext context, long instanceId, int? serviceProviderId, List<int> optimizationGroupIds, BillingPeriod billingPeriod, long commGroupId, bool isCustomerOptimization, bool autoChangeRatePlan = true)
        {

            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(CommonConstants.NUMBER_OF_RETRIES);
            var simCards = new List<SimCard>();
            var parameters = new List<SqlParameter>()
            {
                new SqlParameter(CommonSQLParameterNames.INSTANCE_ID, instanceId),
                new SqlParameter(CommonSQLParameterNames.SERVICE_PROVIDER_ID_PASCAL_CASE, (object)serviceProviderId ?? DBNull.Value),
                new SqlParameter(CommonSQLParameterNames.AUTO_CHANGE_RATE_PLAN, autoChangeRatePlan)
            };
            if (optimizationGroupIds == null || optimizationGroupIds.Count == 0)
            {
                parameters.Add(
                new SqlParameter(CommonSQLParameterNames.OPTIMIZATION_GROUP_IDS, DBNull.Value));
            }
            else
            {
                parameters.Add(
                new SqlParameter(CommonSQLParameterNames.OPTIMIZATION_GROUP_IDS, string.Join(',', optimizationGroupIds)));

            }

            if (commGroupId == 0)
            {
                parameters.Add(new SqlParameter(CommonSQLParameterNames.COMM_GROUP_ID, DBNull.Value));
            }
            else
            {
                parameters.Add(new SqlParameter(CommonSQLParameterNames.COMM_GROUP_ID, commGroupId));

            }

            sqlRetryPolicy.Execute(() =>
            {
                simCards = SqlQueryHelper.ExecuteStoredProcedureWithListResult(AwsFunctionBase.ParameterizedLog(context), context.ConnectionString, SQLConstant.StoredProcedureName.GET_OPTIMIZATION_MOBILITY_DEVICES_BY_INSTANCE_ID,
                    (dataReader) => SimCard.SimCardFromReader(dataReader, billingPeriod, isCustomerOptimization),
                    parameters,
                    SQLConstant.ShortTimeoutSeconds);
            });
            return simCards;
        }

        public List<SimCardResult> GetMobilityDeviceResults(KeySysLambdaContext context, List<long> queueIds, BillingPeriod billingPeriod)
        {
            var policyFactory = new PolicyFactory(context.logger);
            var sqlRetryPolicy = policyFactory.GetSqlRetryPolicy(CommonConstants.NUMBER_OF_RETRIES);
            var simCards = new List<SimCardResult>();
            var parameters = new List<SqlParameter>();
            if (queueIds == null || queueIds.Count == 0)
            {
                parameters.Add(
                new SqlParameter(CommonSQLParameterNames.QUEUE_IDS, DBNull.Value));
            }
            else
            {
                parameters.Add(
                new SqlParameter(CommonSQLParameterNames.QUEUE_IDS, string.Join(',', queueIds)));

            }

            sqlRetryPolicy.Execute(() =>
            {
                simCards = SqlQueryHelper.ExecuteStoredProcedureWithListResult(AwsFunctionBase.ParameterizedLog(context), context.ConnectionString, SQLConstant.StoredProcedureName.GET_OPTIMIZATION_MOBILITY_DEVICE_RESULTS_BY_QUEUE_IDS,
                    (dataReader) => SimCardResult.SimCardResultFromReader(dataReader, billingPeriod),
                    parameters,
                    SQLConstant.ShortTimeoutSeconds);
            });
            return simCards;
        }
        #endregion

        #region private methods

        private static DataTable CreateOptimizationMobilityDeviceDataTable()
        {
            DataTable optimizationMobilityDeviceTable = new DataTable();
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.InstanceId, typeof(long));
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.DeviceId, typeof(int));
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.CycleDataUsageMB, typeof(decimal));
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.ProjectedDataUsageMB, typeof(decimal));
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.CommunicationPlan);
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.MSISDN);
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.ICCID);
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.UsageDate, typeof(DateTime));
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.CreatedBy);
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.CreatedDate, typeof(DateTime));
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.AmopDeviceId, typeof(int));
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.ServiceProviderId, typeof(int));
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.DateActivated, typeof(DateTime));
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.WasActivatedInThisBillingPeriod, typeof(bool));
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.DaysActivatedInBillingPeriod, typeof(int));
            optimizationMobilityDeviceTable.Columns.Add(OptimizationMobilityDeviceColumnNames.SmsUsage, typeof(long));
            optimizationMobilityDeviceTable.Columns.Add(CommonColumnNames.OptimizationRatePlanTypeId, typeof(int));
            optimizationMobilityDeviceTable.Columns.Add(CommonColumnNames.OptimizationGroupId, typeof(int));
            optimizationMobilityDeviceTable.Columns.Add(CommonColumnNames.AutoChangeRatePlan, typeof(bool));
            optimizationMobilityDeviceTable.Columns.Add(CommonColumnNames.OptimizationCommGroupId, typeof(long));
            optimizationMobilityDeviceTable.Columns.Add(CommonColumnNames.RatePlanCode);
            return optimizationMobilityDeviceTable;
        }
        #endregion
    }
}
