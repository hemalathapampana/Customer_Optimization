using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Altaworx.AWS.Core.Helpers.Constants;
using Altaworx.SimCard.Cost.Optimizer.Core.Models;
using Amop.Core.Constants;
using Amop.Core.Helpers;
using Amop.Core.Models;
using Microsoft.Data.SqlClient;

namespace Altaworx.SimCard.Cost.Optimizer.Core.Helpers
{
    public static class OptimizationResultDbWriter
    {
        public static List<LogMessage> RecordResults(KeySysLambdaContext context, string connectionString, long queueId, string revAccountNumber, OptimizationResult optimizationResult)
        {
            context.LogInfo("SUB", $"RecordResults(,,{queueId},{revAccountNumber},[OptimizationResult])");

            // record assignments
            List<LogMessage> results = RecordRatePoolAssignments(context, connectionString, queueId, revAccountNumber, optimizationResult);

            // record summary cost
            RecordTotalCost(context, connectionString, queueId, optimizationResult);

            return results;
        }

        public static List<LogMessage> RecordResults(KeySysLambdaContext context, string connectionString, long queueId, int amopCustomerId, OptimizationResult optimizationResult)
        {
            context.LogInfo("SUB", $"RecordResults(,,{queueId},amopCustomerId:{amopCustomerId},[OptimizationResult])");

            // record assignments
            List<LogMessage> results = RecordRatePoolAssignments(context, connectionString, queueId, amopCustomerId, optimizationResult);

            // record summary cost
            RecordTotalCost(context, connectionString, queueId, optimizationResult);

            return results;
        }

        public static List<LogMessage> RecordResults(KeySysLambdaContext context, string connectionString, long queueId, string revAccountNumber, List<CrossProviderCustomerRatePool> pools, PortalTypes portalType)
        {
            context.LogInfo("SUB", $"RecordResults(,,{queueId},{revAccountNumber},[MobilityRatePools])");

            // record assignments
            List<LogMessage> results = RecordRatePoolAssignments(context, connectionString, queueId, revAccountNumber, pools, portalType);

            // record summary cost
            RecordTotalCost(context, connectionString, queueId, pools);

            return results;
        }

        public static List<LogMessage> RecordResults(KeySysLambdaContext context, string connectionString, long queueId, int amopCustomerId, List<CrossProviderCustomerRatePool> pools, PortalTypes portalType)
        {
            context.LogInfo("SUB", $"RecordResults(,,{queueId},{amopCustomerId},[MobilityRatePools])");

            // record assignments
            List<LogMessage> results = RecordRatePoolAssignments(context, connectionString, queueId, amopCustomerId, pools, portalType);

            // record summary cost
            RecordTotalCost(context, connectionString, queueId, pools);

            return results;
        }

        private static void RecordTotalCost(KeySysLambdaContext context, string connectionString, long queueId, List<CrossProviderCustomerRatePool> pools)
        {
            context.LogInfo("SUB", $"RecordTotalCost(,,{queueId},[List<MobilityRatePool>])");
            var usageChargeAmount = pools.Sum(pool => pool.TotalCost);// base rate + rate charge + overage
            var totalBaseRateAmount = pools.Sum(pool => pool.TotalBaseRate);
            var totalRateChargeAmount = pools.Sum(pool => pool.TotalRateCharge);
            var totalOverageAmount = pools.Sum(pool => pool.TotalOverageCost);
            var smsChargeAmount = pools.Sum(p => p.SimCards.Sum(c => c.Value.SmsChargeAmount));
            var totalChargeAmount = usageChargeAmount + smsChargeAmount;
            RecordTotalCost(context, connectionString, queueId, totalChargeAmount, totalBaseRateAmount, totalRateChargeAmount, totalOverageAmount);
        }

        private static void RecordTotalCost(KeySysLambdaContext context, string connectionString, long queueId, OptimizationResult optimizationResult)
        {
            context.LogInfo(CommonConstants.SUB, $"(,,{queueId},)");
            var usageChargeAmount = optimizationResult.TotalDataCost;
            var totalBaseRateAmount = optimizationResult.TotalBaseRate;
            var totalRateChargeAmount = optimizationResult.TotalRateCharge;
            var totalOverageAmount = optimizationResult.TotalOverageCost;
            var smsChargeAmount = optimizationResult.TotalSmsCost;
            var totalChargeAmount = usageChargeAmount + smsChargeAmount;
            RecordTotalCost(context, connectionString, queueId, totalChargeAmount, totalBaseRateAmount, totalRateChargeAmount, totalOverageAmount);
        }

        public static void RecordTotalCost(KeySysLambdaContext context, string connectionString, long queueId, decimal totalCost, decimal totalBaseRate = 0, decimal totalRateCharge = 0, decimal totalOverageCharge = 0)
        {
            context.LogInfo("SUB", $"RecordTotalCost(,,{queueId},{totalCost})");

            using (var Conn = new SqlConnection(context.ConnectionStringWithoutMARS))
            {
                using (var Cmd = new SqlCommand(@"UPDATE OptimizationQueue
                                                    SET TotalCost = @totalCost,
                                                        [TotalBaseRateAmt] = @totalBaseRate,
                                                        [TotalRateChargeAmt] = @totalRateCharge,
                                                        [TotalOverageChargeAmt] = @totalOverageCharge
                                                    WHERE Id = @id", Conn))
                {
                    Cmd.CommandType = CommandType.Text;
                    Cmd.Parameters.AddWithValue("@id", queueId);
                    Cmd.Parameters.AddWithValue("@totalCost", totalCost);
                    Cmd.Parameters.AddWithValue("@totalBaseRate", totalBaseRate);
                    Cmd.Parameters.AddWithValue("@totalRateCharge", totalRateCharge);
                    Cmd.Parameters.AddWithValue("@totalOverageCharge", totalOverageCharge);
                    Conn.Open();

                    Cmd.ExecuteNonQuery();

                    Conn.Close();
                }
            }
            context.LogInfo("INFO", $"Successfully wrote the total cost of {totalCost} to queue {queueId}");
        }

        public static List<LogMessage> RecordRatePoolAssignments(KeySysLambdaContext context, string connectionString, long queueId, string revAccountNumber, OptimizationResult optimizationResult)
        {
            context.LogInfo("SUB", $"RecordRatePoolCollection(,,{queueId},{revAccountNumber},OptimizationResult)");
            List<LogMessage> results = new List<LogMessage>();

            foreach (var collection in optimizationResult.RawRatePools)
            {
                var tempResults = RecordRatePoolCollection(context, connectionString, queueId, revAccountNumber, collection, optimizationResult.PortalType);
                if (tempResults != null && tempResults.Count > 0)
                {
                    results.AddRange(tempResults);
                }
            }

            return results;
        }

        public static List<LogMessage> RecordRatePoolAssignments(KeySysLambdaContext context, string connectionString, long queueId, int amopCustomerId, OptimizationResult optimizationResult)
        {
            context.LogWithMoreDetails(CommonConstants.SUB, $"(,,{queueId},{nameof(amopCustomerId)}:{amopCustomerId})");
            List<LogMessage> results = new List<LogMessage>();

            foreach (var collection in optimizationResult.RawRatePools)
            {
                var tempResults = RecordRatePoolCollection(context, connectionString, queueId, amopCustomerId, collection, optimizationResult.PortalType);
                if (tempResults != null && tempResults.Count > 0)
                {
                    results.AddRange(tempResults);
                }
            }

            return results;
        }

        public static List<LogMessage> RecordRatePoolAssignments(KeySysLambdaContext context, string connectionString, long queueId, string revAccountNumber, List<CrossProviderCustomerRatePool> pools, PortalTypes portalType)
        {
            List<LogMessage> results = new List<LogMessage>();

            foreach (var pool in pools)
            {
                var tempResults = RecordRatePoolByPortalType(context, queueId, revAccountNumber, pool, portalType);
                if (tempResults != null)
                {
                    results.AddRange(tempResults);
                }

                if (pool.IsPooled)
                {
                    var tempSharedPoolResults = RecordRatePoolByPortalType(context, queueId, revAccountNumber, pool, portalType, isSharedPoolDevices: true);
                    if (tempSharedPoolResults != null)
                    {
                        results.AddRange(tempSharedPoolResults);
                    }
                }
            }

            return results;
        }

        public static List<LogMessage> RecordRatePoolAssignments(KeySysLambdaContext context, string connectionString, long queueId, int amopCustomerId, List<CrossProviderCustomerRatePool> pools, PortalTypes portalType)
        {
            context.LogInfo("SUB", $"RecordRatePoolAssignments(,,{queueId},{amopCustomerId},[List<MobilityRatePool>])");
            List<LogMessage> results = new List<LogMessage>();

            foreach (var pool in pools)
            {
                var tempResults = RecordRatePoolByPortalType(context, queueId, null, pool, portalType, amopCustomerId);
                if (tempResults != null)
                {
                    results.AddRange(tempResults);
                }

                if (pool.IsPooled)
                {
                    var tempSharedPoolResults = RecordRatePoolByPortalType(context, queueId, null, pool, portalType, amopCustomerId, isSharedPoolDevices: true);
                    if (tempSharedPoolResults != null)
                    {
                        results.AddRange(tempSharedPoolResults);
                    }
                }
            }

            return results;
        }

        private static List<LogMessage> RecordRatePoolCollection(KeySysLambdaContext context, string connectionString, long queueId, string revAccountNumber, RatePoolCollection collection, PortalTypes portalType)
        {
            context.LogInfo("SUB", $"RecordRatePoolCollection(,,{queueId},{revAccountNumber},[M2MRatePools])");
            List<LogMessage> results = new List<LogMessage>();

            foreach (var ratePool in collection.RatePools)
            {
                if (ratePool.SimCards.Count <= 0)
                {
                    context.LogInfo("SUB", $"No Sim card for rate pool {ratePool.RatePlan.PlanName} - {ratePool.RatePlan.PlanDisplayName}");
                    continue;
                }

                var tempResult = RecordRatePoolByPortalType(context, queueId, revAccountNumber, ratePool, portalType, collection);
                if (tempResult != null)
                {
                    results.AddRange(tempResult);
                }
            }

            return results;
        }

        private static List<LogMessage> RecordRatePoolCollection(KeySysLambdaContext context, string connectionString, long queueId, int amopCustomerId, RatePoolCollection collection, PortalTypes portalType)
        {
            context.LogWithMoreDetails(CommonConstants.SUB, $"(,,{queueId},{nameof(amopCustomerId)}:{amopCustomerId})");
            List<LogMessage> results = new List<LogMessage>();

            foreach (var ratePool in collection.RatePools)
            {
                if (ratePool.SimCards.Count <= 0)
                {
                    context.LogInfo("SUB", $"No Sim card for rate pool {ratePool.RatePlan.PlanName} - {ratePool.RatePlan.PlanDisplayName}");
                    continue;
                }

                var tempResult = RecordRatePoolByPortalType(context, queueId, null, ratePool, portalType, collection, amopCustomerId);
                if (tempResult != null)
                {
                    results.AddRange(tempResult);
                }
            }

            return results;
        }

        //Record the result for every Cross provider devices 
        private static LogMessage RecordRatePool(KeySysLambdaContext context, string connectionString, long queueId, string revAccountNumber, CrossProviderCustomerRatePool ratePool, PortalTypes portalType, bool isSharedPoolDevices, int? amopCustomerId = null)
        {

            AwsFunctionBase.LogInfo(context, LogTypeConstant.Sub, $"RecordRatePool(,,{queueId},{revAccountNumber},)");
            AwsFunctionBase.LogInfo(context, LogTypeConstant.Info, $"IsSharedPoolDevices: {isSharedPoolDevices}");
            AwsFunctionBase.LogInfo(context, LogTypeConstant.Info, $"MobilityRatePool SIMs: {ratePool.SimCardCount}");
            AwsFunctionBase.LogInfo(context, LogTypeConstant.Info, $"MobilityRatePool Overage: {ratePool.AverageOverageUsage}");
            AwsFunctionBase.LogInfo(context, LogTypeConstant.Info, $"MobilityRatePool RatePlan: {ratePool.RatePlan.PlanName},{ratePool.RatePlan.AllowsSimPooling}");
            AwsFunctionBase.LogInfo(context, LogTypeConstant.Info, $"AMOP Customer Id: {amopCustomerId}");

            DataTable deviceResultTable = BuildOptimizationResultTable();

            Dictionary<string, CrossProviderCustomerSimCard> simCardList;
            if (isSharedPoolDevices)
            {
                simCardList = ratePool.AdditionalSimCards;
            }
            else
            {
                simCardList = ratePool.SimCards;
            }

            foreach (var simCard in simCardList)
            {
                AddSimCardToDeviceResultDataTable(context, queueId, ratePool, portalType, deviceResultTable, simCard);
            }

            List<SqlBulkCopyColumnMapping> columnMappings = SQLBulkCopyHelper.AutoMapColumns(deviceResultTable);

            return SqlHelper.SqlBulkCopy(connectionString, deviceResultTable, GetCrossProviderOptimizationResultTableName(context, portalType, isSharedPoolDevices), columnMappings);
        }

        //Record the result for every carrier optimization SIMs
        private static LogMessage RecordRatePool(KeySysLambdaContext context, string connectionString, long queueId, string revAccountNumber, M2MRatePool ratePool, PortalTypes portalType, RatePoolCollection collection, int? amopCustomerId = null)
        {
            context.LogWithMoreDetails(CommonConstants.SUB, $"(,,{queueId},{revAccountNumber},[M2MRatePool])");
            context.LogWithMoreDetails(CommonConstants.INFO, $"M2MRatePool SIMs: {ratePool.SimCardCount}");
            context.LogWithMoreDetails(CommonConstants.INFO, $"M2MRatePool Overage: {ratePool.AverageOverageUsage}");
            context.LogWithMoreDetails(CommonConstants.INFO, $"M2MRatePool RatePlan: {ratePool.RatePlan.PlanName},{ratePool.RatePlan.AllowsSimPooling}");
            context.LogWithMoreDetails(CommonConstants.INFO, $"AMOP Customer Id: {amopCustomerId}");
            DataTable table = BuildOptimizationResultTable();

            foreach (var simCard in ratePool.SimCards)
            {
                AddSimToDeviceResultTable(queueId, revAccountNumber, ratePool, portalType, collection, amopCustomerId, table, simCard);
            }


            return SqlHelper.SqlBulkCopyWithRetry(context.logger, context.ConnectionStringWithoutMARS, table, GetOptimizationResultStagingTableName(context, portalType), SQLBulkCopyHelper.AutoMapColumns(table));
        }

        private static string GetOptimizationResultStagingTableName(KeySysLambdaContext context, PortalTypes portalType)
        {
            if (portalType == PortalTypes.Mobility)
            {
                return DatabaseTableNames.OptimizationMobilityDeviceResultStaging;
            }
            else if (portalType == PortalTypes.M2M)
            {
                return DatabaseTableNames.OptimizationDeviceResultStaging;
            }
            else
            {
                OptimizationErrorHandler.OnPortalTypeError(context, portalType, true);
                //should already throw exception from handler
                return string.Empty;
            }
        }

        //Record the result for every Sims that do not have customer rate plan assigned
        public static LogMessage RecordRatePool(KeySysLambdaContext context, string connectionString, long queueId, int? billingPeriodId, List<SimCard> sims, PortalTypes portalType = PortalTypes.M2M, int? customerBillingPeriodId = null)
        {
            context.LogInfo("SUB", $"RecordRatePool(,,{queueId},{billingPeriodId},[sims],{portalType})");
            context.LogInfo("INFO", $"M2MRatePool SIMs: {sims.Count}");

            DataTable table = BuildOptimizationResultTable();

            foreach (var simCard in sims)
            {
                AddNoCustomerRatePlanSimToDeviceResultTable(queueId, billingPeriodId, table, simCard);
            }

            return SqlHelper.SqlBulkCopyWithRetry(context.logger, context.ConnectionStringWithoutMARS, table, GetOptimizationResultStagingTableName(context, portalType), SQLBulkCopyHelper.AutoMapColumns(table));
        }

        private static void AddNoCustomerRatePlanSimToDeviceResultTable(long queueId, int? billingPeriodId, DataTable table, SimCard simCard)
        {
            var deviceResultRow = table.NewRow();

            deviceResultRow[CommonColumnNames.QueueId] = queueId;
            deviceResultRow[CommonColumnNames.DeviceId] = DBNull.Value;
            deviceResultRow[CommonColumnNames.UsageMB] = simCard.CycleDataUsageMB;
            deviceResultRow[CommonColumnNames.AssignedCarrierRatePlanId] = DBNull.Value;
            deviceResultRow[CommonColumnNames.AssignedCustomerRatePlanId] = DBNull.Value;
            deviceResultRow[CommonColumnNames.CreatedBy] = OptimizationConstant.DefaultCreatedByName;
            deviceResultRow[CommonColumnNames.CreatedDate] = DateTime.UtcNow;
            deviceResultRow[CommonColumnNames.AmopDeviceId] = simCard.Id;
            deviceResultRow[CommonColumnNames.ChargeAmt] = 0.0M;
            deviceResultRow[CommonColumnNames.BillingPeriodId] = billingPeriodId.HasValue ? billingPeriodId : DBNull.Value;
            deviceResultRow[CommonColumnNames.SmsUsage] = simCard.SmsUsage;
            deviceResultRow[CommonColumnNames.SmsChargeAmount] = 0.0M;
            deviceResultRow[CommonColumnNames.BaseRateAmt] = 0.0M;
            deviceResultRow[CommonColumnNames.RateChargeAmt] = 0.0M;
            deviceResultRow[CommonColumnNames.OverageChargeAmt] = 0.0M;
            deviceResultRow[CommonColumnNames.CustomerRatePoolId] = simCard.CustomerRatePoolId.HasValue ? simCard.CustomerRatePoolId : DBNull.Value;

            table.Rows.Add(deviceResultRow);
        }

        private static void AddSimToDeviceResultTable(long queueId, string revAccountNumber, M2MRatePool ratePool, PortalTypes portalType, RatePoolCollection collection, int? amopCustomerId, DataTable table, KeyValuePair<string, SimCard> simCard)
        {
            var deviceResultRow = table.NewRow();

            deviceResultRow[CommonColumnNames.QueueId] = queueId;
            deviceResultRow[CommonColumnNames.DeviceId] = DBNull.Value;
            deviceResultRow[CommonColumnNames.UsageMB] = simCard.Value.CycleDataUsageMB;
            if (string.IsNullOrWhiteSpace(revAccountNumber) && amopCustomerId == null)
            {
                //record carrier optimization result
                deviceResultRow[CommonColumnNames.AssignedCarrierRatePlanId] = ratePool.RatePlan.Id;
                deviceResultRow[CommonColumnNames.AssignedCustomerRatePlanId] = DBNull.Value;
            }
            else
            {
                //record customer optimization result
                deviceResultRow[CommonColumnNames.AssignedCarrierRatePlanId] = DBNull.Value;
                deviceResultRow[CommonColumnNames.AssignedCustomerRatePlanId] = ratePool.RatePlan.Id;
                deviceResultRow[CommonColumnNames.CustomerRatePoolId] = (object)collection.RatePoolId ?? DBNull.Value;
            }

            deviceResultRow[CommonColumnNames.CreatedBy] = OptimizationConstant.DefaultCreatedByName;
            deviceResultRow[CommonColumnNames.CreatedDate] = DateTime.UtcNow;
            deviceResultRow[CommonColumnNames.AmopDeviceId] = simCard.Value.Id;
            decimal chargeAmount;
            if (collection.ShouldPoolByOptimizationGroup)
            {
                chargeAmount = collection.DataChargeBySim(ratePool, simCard.Value);
            }
            else
            {
                chargeAmount = ratePool.DataChargeBySim(simCard.Value);
            }
            deviceResultRow[CommonColumnNames.ChargeAmt] = chargeAmount;
            deviceResultRow[CommonColumnNames.BillingPeriodId] = ratePool.BillingPeriod.Id;
            deviceResultRow[CommonColumnNames.SmsUsage] = simCard.Value.SmsUsage;
            deviceResultRow[CommonColumnNames.SmsChargeAmount] = ratePool.SmsChargeBySim(simCard.Value);
            deviceResultRow[CommonColumnNames.BaseRateAmt] = ratePool.BaseRateBySim(simCard.Value);
            deviceResultRow[CommonColumnNames.RateChargeAmt] = ratePool.RateChargeBySim(simCard.Value);
            decimal overageDataChargeAmount;
            if (collection.ShouldPoolByOptimizationGroup)
            {
                overageDataChargeAmount = collection.AverageOverageCost;
            }
            else
            {
                overageDataChargeAmount = ratePool.OverageDataChargeBySim(simCard.Value);
            }
            deviceResultRow[CommonColumnNames.OverageChargeAmt] = overageDataChargeAmount;
            table.Rows.Add(deviceResultRow);
        }

        private static void AddSimCardToDeviceResultDataTable(KeySysLambdaContext context, long queueId, CrossProviderCustomerRatePool ratePool, PortalTypes portalType, DataTable deviceResultTable, KeyValuePair<string, CrossProviderCustomerSimCard> simCard)
        {
            var overageCharge = simCard.Value.CustomerRatePoolId != null ? ratePool.AverageOverageCost : simCard.Value.OverageChargeAmt;
            var totalCharge = overageCharge + simCard.Value.BasePlanCost;
            var baseRate = simCard.Value.BaseRate;
            var rateCharge = simCard.Value.RateCharge;
            var smsCount = !ratePool.IsBillInAdvance ? simCard.Value.SmsUsage : 0;
            var smsCharge = !ratePool.IsBillInAdvance ? simCard.Value.SmsChargeAmount : 0;

            if (ratePool.ChargeType == OptimizationChargeType.OverageOnly)
            {
                totalCharge = overageCharge;
                baseRate = OptimizationConstant.OverageOnlyBaseRate;
                rateCharge = OptimizationConstant.OverageOnlyRateCharge;
            }
            else if (ratePool.ChargeType == OptimizationChargeType.RateChargeOnly)
            {
                totalCharge = simCard.Value.BasePlanCost;
            }

            var deviceResultRow = deviceResultTable.NewRow();

            deviceResultRow[OptimizationDeviceResultColumnNames.QueueId] = queueId;
            deviceResultRow[OptimizationDeviceResultColumnNames.DeviceId] = DBNull.Value;
            deviceResultRow[OptimizationDeviceResultColumnNames.UsageMB] = simCard.Value.CycleDataUsageMB;
            deviceResultRow[OptimizationDeviceResultColumnNames.AssignedCarrierRatePlanId] = DBNull.Value;
            deviceResultRow[OptimizationDeviceResultColumnNames.AssignedCustomerRatePlanId] = simCard.Value.CustomerRatePlanId;
            deviceResultRow[OptimizationDeviceResultColumnNames.CreatedBy] = GetCreateByFromPortalType(context, portalType);
            deviceResultRow[OptimizationDeviceResultColumnNames.CreatedDate] = DateTime.UtcNow;
            deviceResultRow[OptimizationDeviceResultColumnNames.AmopDeviceId] = simCard.Value.Id;
            deviceResultRow[OptimizationDeviceResultColumnNames.ChargeAmt] = totalCharge;
            deviceResultRow[OptimizationDeviceResultColumnNames.BillingPeriodId] = ratePool.BillingPeriod.Id;
            deviceResultRow[OptimizationDeviceResultColumnNames.SmsUsage] = smsCount;
            deviceResultRow[OptimizationDeviceResultColumnNames.SmsChargeAmount] = smsCharge;
            deviceResultRow[OptimizationDeviceResultColumnNames.BaseRateAmt] = baseRate;
            deviceResultRow[OptimizationDeviceResultColumnNames.RateChargeAmt] = rateCharge;
            deviceResultRow[OptimizationDeviceResultColumnNames.OverageChargeAmt] = overageCharge;
            deviceResultRow[OptimizationDeviceResultColumnNames.CustomerRatePoolId] = simCard.Value.CustomerRatePoolId ?? (object)DBNull.Value;

            deviceResultTable.Rows.Add(deviceResultRow);
        }

        private static DataTable BuildOptimizationResultTable()
        {
            DataTable table = new DataTable();
            table.Columns.Add(OptimizationDeviceResultColumnNames.QueueId, typeof(long));
            table.Columns.Add(OptimizationDeviceResultColumnNames.DeviceId, typeof(int));
            table.Columns.Add(OptimizationDeviceResultColumnNames.UsageMB, typeof(decimal));
            table.Columns.Add(OptimizationDeviceResultColumnNames.AssignedCarrierRatePlanId, typeof(int));
            table.Columns.Add(OptimizationDeviceResultColumnNames.AssignedCustomerRatePlanId, typeof(int));
            table.Columns.Add(OptimizationDeviceResultColumnNames.CreatedBy);
            table.Columns.Add(OptimizationDeviceResultColumnNames.CreatedDate, typeof(DateTime));
            table.Columns.Add(OptimizationDeviceResultColumnNames.AmopDeviceId, typeof(int));
            table.Columns.Add(OptimizationDeviceResultColumnNames.ChargeAmt, typeof(decimal));
            table.Columns.Add(OptimizationDeviceResultColumnNames.BillingPeriodId, typeof(int));
            table.Columns.Add(OptimizationDeviceResultColumnNames.SmsUsage, typeof(long));
            table.Columns.Add(OptimizationDeviceResultColumnNames.SmsChargeAmount, typeof(decimal));
            table.Columns.Add(OptimizationDeviceResultColumnNames.BaseRateAmt, typeof(decimal));
            table.Columns.Add(OptimizationDeviceResultColumnNames.RateChargeAmt, typeof(decimal));
            table.Columns.Add(OptimizationDeviceResultColumnNames.OverageChargeAmt, typeof(decimal));
            table.Columns.Add(OptimizationDeviceResultColumnNames.CustomerRatePoolId, typeof(int));
            return table;
        }

        private static string GetCrossProviderOptimizationResultTableName(KeySysLambdaContext context, PortalTypes portalType, bool isSharedPoolDevices)
        {
            if (portalType == PortalTypes.Mobility)
            {
                if (isSharedPoolDevices)
                {
                    return DatabaseTableNames.OptimizationMobilitySharedPoolResult;
                }
                else
                {
                    return DatabaseTableNames.OptimizationMobilityDeviceResultStaging;
                }
            }
            else if (portalType == PortalTypes.M2M)
            {
                if (isSharedPoolDevices)
                {
                    return DatabaseTableNames.OptimizationSharedPoolResult;
                }
                else
                {
                    return DatabaseTableNames.OptimizationDeviceResultStaging;
                }
            }
            else
            {
                OptimizationErrorHandler.OnPortalTypeError(context, portalType, true);
                //should already throw exception from handler
                return string.Empty;
            }
        }

        private static string GetCreateByFromPortalType(KeySysLambdaContext context, PortalTypes portalType)
        {
            if (portalType == PortalTypes.Mobility)
            {
                return OptimizationConstant.DefaultMobilityCreatedByName;
            }
            else if (portalType == PortalTypes.M2M)
            {
                return OptimizationConstant.DefaultM2MCreatedByName;
            }
            else
            {
                OptimizationErrorHandler.OnPortalTypeError(context, portalType, true);
                //should already throw exception from handler
                return string.Empty;
            }
        }

        public static List<LogMessage> RecordCrossProviderRatePool(KeySysLambdaContext context, string connectionString, long queueId, List<SimCard> sims, int customerBillingPeriodId)
        {
            var logMessages = new List<LogMessage>();
            var simsByPortalTypes = sims.GroupBy(x => x.PortalType);
            foreach (var simsByPortalType in simsByPortalTypes)
            {
                var logMessage = RecordRatePool(context, connectionString, queueId, null, sims, simsByPortalType.Key, customerBillingPeriodId);
                if (logMessage != null)
                {
                    logMessages.Add(logMessage);
                }
            }
            return logMessages;
        }

        private static List<LogMessage> RecordRatePoolByPortalType(KeySysLambdaContext context, long queueId, string revAccountNumber, M2MRatePool ratePool, PortalTypes portalType, RatePoolCollection collection, int? amopCustomerId = null)
        {
            if (portalType == PortalTypes.CrossProvider)
            {
                return RecordCrossProviderRatePool(context, queueId, revAccountNumber, ratePool, portalType, collection, amopCustomerId);
            }
            else
            {
                return new List<LogMessage>
                {
                    RecordRatePool(context, context.ConnectionString, queueId, revAccountNumber, ratePool, portalType, collection, amopCustomerId)
                };
            }
        }

        private static List<LogMessage> RecordRatePoolByPortalType(KeySysLambdaContext context, long queueId, string revAccountNumber, CrossProviderCustomerRatePool ratePool, PortalTypes portalType, int? amopCustomerId = null, bool isSharedPoolDevices = false)
        {
            if (portalType == PortalTypes.CrossProvider)
            {
                return RecordCrossProviderRatePool(context, queueId, revAccountNumber, ratePool, portalType, amopCustomerId, isSharedPoolDevices);
            }
            else
            {
                return new List<LogMessage>
                {
                    RecordRatePool(context, context.ConnectionString, queueId, revAccountNumber, ratePool, portalType, isSharedPoolDevices, amopCustomerId)
                };
            }
        }

        private static List<LogMessage> RecordCrossProviderRatePool(KeySysLambdaContext context, long queueId, string revAccountNumber, M2MRatePool ratePool, PortalTypes portalType, RatePoolCollection collection, int? amopCustomerId = null)
        {
            context.LogWithMoreDetails(CommonConstants.SUB, $"(,,{queueId},{revAccountNumber},{portalType})");
            context.LogWithMoreDetails(CommonConstants.INFO, $"RatePool SIMs: {ratePool.SimCardCount}");
            context.LogWithMoreDetails(CommonConstants.INFO, $"RatePool Overage: {ratePool.AverageOverageUsage}");
            context.LogWithMoreDetails(CommonConstants.INFO, $"RatePool RatePlan: {ratePool.RatePlan.PlanName},{ratePool.RatePlan.AllowsSimPooling}");
            context.LogWithMoreDetails(CommonConstants.INFO, $"AMOP Customer Id: {amopCustomerId}");

            var logMessages = new List<LogMessage>();
            DataTable table = BuildOptimizationResultTable();
            List<SqlBulkCopyColumnMapping> columnMappings = SQLBulkCopyHelper.AutoMapColumns(table);
            var simCardsByPortalTypes = ratePool.SimCards.GroupBy(x => x.Value.PortalType);
            foreach (var simCardsByPortalType in simCardsByPortalTypes)
            {
                foreach (var simCard in simCardsByPortalType)
                {
                    AddSimToDeviceResultTable(queueId, revAccountNumber, ratePool, simCardsByPortalType.Key, collection, amopCustomerId, table, simCard);
                }

                logMessages.Add(SqlHelper.SqlBulkCopyWithRetry(context.logger, context.ConnectionStringWithoutMARS, table, GetOptimizationResultStagingTableName(context, simCardsByPortalType.Key), columnMappings));
                table.Clear();
            }
            return logMessages;
        }

        // Record the result for every Cross provider customer optimization device by portal type 
        private static List<LogMessage> RecordCrossProviderRatePool(KeySysLambdaContext context, long queueId, string revAccountNumber, CrossProviderCustomerRatePool ratePool, PortalTypes portalType, int? amopCustomerId = null, bool isSharedPoolDevices = false)
        {
            context.LogWithMoreDetails(CommonConstants.SUB, $"(,,{queueId},{revAccountNumber},{portalType})");
            context.LogWithMoreDetails(CommonConstants.INFO, $"RatePool SIMs: {ratePool.SimCardCount}");
            context.LogWithMoreDetails(CommonConstants.INFO, $"RatePool Overage: {ratePool.AverageOverageUsage}");
            context.LogWithMoreDetails(CommonConstants.INFO, $"RatePool RatePlan: {ratePool.RatePlan.PlanName},{ratePool.RatePlan.AllowsSimPooling}");
            context.LogWithMoreDetails(CommonConstants.INFO, $"AMOP Customer Id: {amopCustomerId}");
            context.LogWithMoreDetails(CommonConstants.INFO, $"{nameof(isSharedPoolDevices)}: {isSharedPoolDevices}");

            var logMessages = new List<LogMessage>();
            DataTable deviceResultTable = BuildOptimizationResultTable();
            List<SqlBulkCopyColumnMapping> columnMappings = SQLBulkCopyHelper.AutoMapColumns(deviceResultTable);

            Dictionary<string, CrossProviderCustomerSimCard> simCardList;
            if (isSharedPoolDevices)
            {
                simCardList = ratePool.AdditionalSimCards;
            }
            else
            {
                simCardList = ratePool.SimCards;
            }

            var simCardsByPortalTypes = simCardList.GroupBy(x => x.Value.PortalType);
            foreach (var simCardsByPortalType in simCardsByPortalTypes)
            {
                foreach (var simCard in simCardsByPortalType)
                {
                    AddSimCardToDeviceResultDataTable(context, queueId, ratePool, simCardsByPortalType.Key, deviceResultTable, simCard);
                }
                logMessages.Add(SqlHelper.SqlBulkCopy(context.ConnectionStringWithoutMARS, deviceResultTable, GetCrossProviderOptimizationResultTableName(context, simCardsByPortalType.Key, isSharedPoolDevices), columnMappings));
                deviceResultTable.Clear();
            }

            return logMessages;
        }
    }
}
