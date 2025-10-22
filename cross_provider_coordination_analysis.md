# Cross-Provider Coordination & Email System Analysis  
## Altaworx SIM Card Cost Optimization - Detailed Algorithmic Breakdown

### Overview
The cross-provider coordination system manages multi-provider optimization sessions, tracks processing state across providers, handles retry logic, and sends consolidated email notifications with provider-specific attachments and summaries.

---

## 1. Cross-Provider Email Notifications

### What:
Consolidated optimization results across all providers with provider-specific Excel attachments, cost savings summaries, and cross-provider email templates.

### Why:
Provides unified view of optimization results while maintaining provider-specific granularity for operational and strategic decision-making.

### How:

**Algorithm: Consolidated Email Generation**
```
1. Email Context Determination:
   IF instance.portal_type == CrossProvider:
     email_content = OptimizationCustomerEndProcess{
       instance_id, session_id, service_provider_id,
       customer_type, billing_period_end_date, tenant_id,
       detail_sync_date: null, usage_sync_date: null
     }
   ELSE:
     sync_results = GetSummaryValues(integration_type, service_provider_id)
     email_content = OptimizationCustomerEndProcess{
       instance_id, session_id, service_provider_id,
       customer_type, billing_period_end_date, tenant_id,
       detail_sync_date: sync_results.detail_last_sync_date,
       usage_sync_date: sync_results.usage_last_sync_date
     }

2. Cross-Provider Result Compilation:
   opt_customer_processing = GetOptCustomerProcessing(service_provider_id, session_id)
   
   FOR each processing_record in opt_customer_processing:
     customer_results.add({
       service_provider_id: processing_record.service_provider_id,
       service_provider_name: processing_record.service_provider_name,
       customer_name: processing_record.customer_name,
       device_count: processing_record.device_count,
       processing_duration: processing_record.end_time - processing_record.start_time,
       is_successful: processing_record.is_processed
     })

3. Email Body Construction:
   html_body = OptCustomerResultsBody(
     instance, opt_customer_processing,
     run_start_time, run_end_time,
     device_detail_sync_date, device_usage_sync_date, sim_count
   )
   
   // HTML template with provider-specific sections
   html_template = """
   <html>
   <div>Optimization Results for Billing Period Ending on {billing_period_end}
        Optimization started: {run_start_time}
        Optimization completed: {run_end_time}</div>
   <div>Last Device Detail Sync Date: {device_detail_sync_date}
        Last Device Usage Sync Date: {device_usage_sync_date}
        Total SIM Cards: {sim_count}
        Execution OU: {execution_ou}</div>
   <table>
   <tr><th>No.</th><th>Customer Name</th></tr>
   """
   
   FOR each customer_result in customer_results:
     customer_name = (customer_type == AMOP) ? 
       customer_result.amop_customer_name : customer_result.customer_name
     html_template += "<tr><td>{index}</td><td>{customer_name}</td></tr>"

4. Excel Attachment Generation:
   excel_bytes = BuildResultsEmailBody(instance, assignment_xlsx_bytes, billing_timezone, sync_results)
   email_body.attachments.add("device_assignments.xlsx", excel_bytes, excel_content_type)

5. Email Delivery:
   SendOptimizationEmail(subject, email_body, from_address, recipient_addresses, bcc_addresses)
```

**Code Location:** `AltaworxSimCardCostOptimizerCleanup.cs`
- Lines 195-277: `OptCustomerSendEmail()` - Main email orchestration
- Lines 1987-2011: `BuildResultsEmailBody()` - Email body generation with Excel attachment
- Lines 2013-2069: `OptCustomerResultsBody()` - Cross-provider HTML template generation
- Lines 1947-1985: `SendOptimizationEmail()` - AWS SES email delivery

---

## 2. Cross-Provider Processing Tracking

### What:
Updates OptimizationCustomerProcessing table with multi-provider status, tracks session progress, and manages processing state coordination.

### Why:
Maintains comprehensive audit trail of optimization progress across multiple providers and enables coordination logic.

### How:

**Algorithm: Multi-Provider Processing State Management**
```
1. Processing Status Update:
   UpdateOptCustomerProcessing(customer_id, end_time, device_count, service_provider_id, site_type, instance)
   
   update_query = """
   UPDATE [OptimizationCustomerProcessing]
   SET [DeviceCount] = @deviceCount,
       [IsProcessed] = @isProcessing,
       [EndTime] = @endTime,
       [InstanceId] = @instanceId
   WHERE {customer_filter}
   AND [ServiceProviderId] = @serviceProviderId
   AND [SessionId] = @sessionId
   """
   
   IF site_type == Rev:
     customer_filter = "[CustomerId] = @customerId"
   ELSE:
     customer_filter = "[AMOPCustomerId] = @amopCustomerId"

2. Cross-Provider Session Progress Tracking:
   CheckOptCustomerProcessing(service_provider_id, session_id)
   
   check_query = """
   SELECT COUNT(*) FROM [OptimizationCustomerProcessing]
   WHERE [IsProcessed] = false
   AND [SessionId] = @sessionId
   """
   
   IF service_provider_id > 0:
     check_query += " AND [ServiceProviderId] = @serviceProviderId"
   
   unprocessed_count = execute_query(check_query)
   return unprocessed_count > 0

3. Provider-Specific Result Aggregation:
   GetOptCustomerProcessing(service_provider_id, session_id)
   
   aggregation_query = """
   SELECT [ServiceProviderId], [CustomerId], [CustomerName], [DeviceCount], 
          [IsProcessed], [StartTime], [EndTime], s.[Name], 
          o.[AMOPCustomerId], o.[AMOPCustomerName]
   FROM [OptimizationCustomerProcessing] o
   JOIN ServiceProvider s ON s.Id = o.ServiceProviderId
   WHERE [ServiceProviderId] = @serviceProviderId 
   AND [IsProcessed] = true 
   AND [SessionId] = @sessionId
   """

4. Processing State Cleanup:
   DeleteDataFromOptCustomerProcessing(service_provider_id, session_id)
   
   IF service_provider_id > 0:
     delete_query = """
     DELETE FROM [OptimizationCustomerProcessing]
     WHERE [ServiceProviderId] = @serviceProviderId 
     AND [SessionId] = @sessionId
     """
   ELSE:
     delete_query = """
     DELETE FROM [OptimizationCustomerProcessing]
     WHERE [SessionId] = @sessionId
     """
```

**Code Location:** `AltaworxSimCardCostOptimizerCleanup.cs`
- Lines 1777-1827: `UpdateOptCustomerProcessing()` - Multi-provider state updates
- Lines 1861-1901: `CheckOptCustomerProcessing()` - Session progress verification
- Lines 1902-1946: `GetOptCustomerProcessing()` - Provider-specific data retrieval
- Lines 1828-1860: `DeleteDataFromOptCustomerProcessing()` - Cleanup coordination

---

## 3. Cross-Provider Session Management

### What:
Coordinates customer optimization across multiple service providers, waits for all instances to complete, and manages delays and retry logic.

### Why:
Ensures all provider instances complete before sending consolidated results and handles provider-specific completion criteria.

### How:

**Algorithm: Cross-Provider Session Coordination**
```
1. Session Initialization and Instance Tracking:
   ProcessCustomerOptimizationByPortalType(context, message, is_last_instance, tenant_id, customer_type, message_id, optimization_session_id, uses_proration, additional_data)
   
   // Extract session coordination flags
   is_last_instance = message.message_attributes["IsLastInstance"].boolean_value
   optimization_session_id = message.message_attributes["OptimizationSessionId"].long_value
   
2. Instance Completion Detection:
   OptimizationCustomerSendResults(instance, sync_results, is_last_instance, service_provider_id)
   
   // Update processing status for current provider
   UpdateOptCustomerProcessing(customer_id, DateTime.UtcNow, device_count, service_provider_id, site_type, instance)
   
   IF is_last_instance:
     // All provider instances completed - trigger email coordination
     QueueLastStepOptCustomerCleanup(instance.id, instance.session_id, send_email=true, service_provider_id, opt_customer_cleanup_delay_seconds)

3. Cross-Provider Coordination Logic:
   ProcessResultForCrossProvider(is_customer_optimization, is_last_instance, instance, file_result)
   
   IF is_customer_optimization:
     customer = GetRevCustomerById(instance.rev_customer_id)
     crossProviderOptimizationRepository.UpdateProcessingCustomerOptimizationInstance(
       session_id, instance_id, error_message=null, total_device_count, 
       is_error=false, customer_type, customer.rev_customer_id, amop_customer_id
     )
     
     IF is_last_instance:
       // Final step: consolidate all provider results
       QueueLastStepOptCustomerCleanup(instance_id, session_id, send_email=true, service_provider_id=0, delay_seconds)

4. Provider-Specific Completion Criteria Validation:
   WriteResultByPortalType(is_customer_optimization, instance, billing_period, queue_ids, uses_proration)
   
   IF instance.portal_type == Mobility:
     return WriteMobilityResultsByOptimizationType(instance, queue_ids, billing_period, uses_proration, is_customer_optimization)
   ELSE IF instance.portal_type == M2M:
     return WriteM2MResults(instance, queue_ids, billing_period, uses_proration, is_customer_optimization)
   ELSE IF instance.portal_type == CrossProvider:
     // Cross-Provider optimization uses unified customer optimization approach
     return WriteCrossProviderCustomerResults(instance, queue_ids, uses_proration)
```

**Code Location:** `AltaworxSimCardCostQueueCustomerOptimization.cs`
- Lines 95-98: Session coordination flag extraction from SQS messages
- Lines 135, 140: Provider-specific and cross-provider processing dispatch
- Lines 1770-1773: Last instance detection and email triggering

`AltaworxSimCardCostOptimizerCleanup.cs`
- Lines 1741-1775: Instance completion handling and processing updates
- Lines 2346-2361: `ProcessResultForCrossProvider()` - Cross-provider coordination
- Lines 424-444: `WriteResultByPortalType()` - Provider-specific completion criteria

---

## 4. Processing Delays and Retry Logic

### What:
Manages cross-provider processing delays, implements exponential backoff, and handles retry scenarios with configurable limits.

### Why:
Ensures system resilience during high-load periods and provides graceful handling of temporary processing failures.

### How:

**Algorithm: Advanced Retry and Delay Management**
```
1. Queue-Based Delay Calculation:
   DelaySecondsFromQueueLength(optimization_queue_length)
   
   default_delay_seconds = 600  // 10 minutes base delay
   
   IF optimization_queue_length > 50:
     delay_seconds = 900  // 15 minutes max delay (SQS limit)
   ELSE:
     delay_seconds = default_delay_seconds
   
   return delay_seconds

2. Retry Logic with Exponential Backoff:
   OptCustomerSendEmail(instance_id, session_id, service_provider_id, retry_count)
   
   unprocessed_providers = CheckOptCustomerProcessing(service_provider_id, session_id)
   
   IF unprocessed_providers:
     IF retry_count <= clean_up_send_email_retry_count:
       // Retry with delay
       QueueLastStepOptCustomerCleanup(
         instance_id, session_id, send_email=true, 
         service_provider_id, opt_customer_cleanup_delay_seconds, 
         retry_count + 1
       )
     ELSE:
       LogWarning("Customer Optimization process has retried {max_retry_count} times.")
       // Escalate or fail gracefully
   ELSE:
     // All providers completed - proceed with email
     SendConsolidatedOptimizationEmail()

3. SQS-Based Cleanup Coordination:
   QueueLastStepOptCustomerCleanup(instance_id, session_id, is_send_email, service_provider_id, delay_seconds, retry_count)
   
   sqs_message = {
     delay_seconds: delay_seconds,
     message_attributes: {
       "InstanceId": instance_id,
       "SessionId": session_id,
       "IsOptLastStepSendEmail": is_send_email,
       "ServiceProviderId": service_provider_id,
       "RetryCount": retry_count
     },
     message_body: "Optimization Customer Send Email",
     queue_url: cleanup_destination_queue_url
   }
   
   sqs_client.send_message(sqs_message)

4. Cleanup Retry Management:
   RequeueCleanup(instance_id, retry_count, optimization_queue_length, is_customer_optimization)
   
   retry_count += 1
   delay_seconds = DelaySecondsFromQueueLength(optimization_queue_length)
   
   requeue_message = {
     delay_seconds: delay_seconds,
     message_attributes: {
       "InstanceId": instance_id,
       "RetryCount": retry_count,
       "IsCustomerOptimization": is_customer_optimization
     },
     message_body: "Requeue Cleanup for Instance {instance_id}, Retry #{retry_count}",
     queue_url: cleanup_destination_queue_url
   }

5. Queue Length Monitoring:
   GetOptimizationQueueLength()
   
   queue_attributes = sqs_client.get_queue_attributes([
     "ApproximateNumberOfMessages",
     "ApproximateNumberOfMessagesDelayed", 
     "ApproximateNumberOfMessagesNotVisible"
   ])
   
   total_queue_length = (
     queue_attributes.approximate_number_of_messages +
     queue_attributes.approximate_number_of_messages_delayed +
     queue_attributes.approximate_number_of_messages_not_visible
   )
   
   return total_queue_length
```

**Code Location:** `AltaworxSimCardCostOptimizerCleanup.cs`
- Lines 2261-2273: `DelaySecondsFromQueueLength()` - Queue-based delay calculation
- Lines 195-212: Retry logic with configurable limits in `OptCustomerSendEmail()`
- Lines 2170-2218: `QueueLastStepOptCustomerCleanup()` - SQS coordination with delays
- Lines 2219-2259: `RequeueCleanup()` - Cleanup retry management
- Lines 280-297: `GetOptimizationQueueLength()` - Queue monitoring for load-based delays

---

## 5. Consolidated Multi-Provider Results

### What:
Sends consolidated optimization results across all providers with unified reporting and provider-specific breakdowns.

### Why:
Provides comprehensive view of cross-provider optimization effectiveness while maintaining granular provider insights.

### How:

**Algorithm: Multi-Provider Result Consolidation**
```
1. Cross-Provider Result Compilation:
   WriteCrossProviderCustomerResults(instance, queue_ids, uses_proration)
   
   // Use M2M optimization result model for cross-provider consistency
   result = new M2MOptimizationResult()
   cross_customer_result = new M2MOptimizationResult()
   total_device_count = 0
   
   // Get billing period for customer across providers
   customer_billing_period = crossProviderOptimizationRepository.GetBillingPeriod(
     amop_customer_id, customer_billing_period_id, billing_timezone
   )

2. Rate Pool Aggregation Across Providers:
   cross_optimization_result_rate_pools = GetResultRatePools(
     instance, customer_billing_period, uses_proration, queue_ids, is_customer_optimization
   )
   
   customer_specific_rate_pools = GenerateCustomerSpecificRatePools(cross_optimization_result_rate_pools)
   
   AddUnassignedRatePool(instance, customer_billing_period, uses_proration, 
     cross_optimization_result_rate_pools, customer_specific_rate_pools)

3. Device Result Processing by Queue:
   FOR each queue_id in queue_ids:
     device_results = crossProviderOptimizationRepository.GetCrossProviderResults(queue_id, customer_billing_period)
     total_device_count += device_results.count
     
     // Build customer-specific results
     result = BuildM2MOptimizationResult(device_results, customer_specific_rate_pools, result)
     
     // Build cross-customer shared pool results
     shared_pool_device_results = crossProviderOptimizationRepository.GetCrossProviderSharedPoolResults(queue_id, customer_billing_period)
     shared_pool_device_results.add_range(device_results)
     cross_customer_result = BuildM2MOptimizationResult(shared_pool_device_results, cross_optimization_result_rate_pools, cross_customer_result, skip_auto_change_rate_plan=true)

4. Multi-Tab Excel Generation:
   stat_file_bytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, result)
   assignment_file_bytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(result)
   
   IF cross_customer_result.combined_rate_pools.total_sim_card_count > result.combined_rate_pools.total_sim_card_count:
     shared_pool_stat_bytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, cross_customer_result)
     shared_pool_assignment_bytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(cross_customer_result)
   
   // Generate consolidated Excel with multiple provider tabs
   assignment_xlsx_bytes = RatePoolAssignmentWriter.GenerateExcelFileFromByteArrays(
     stat_file_bytes, assignment_file_bytes, 
     shared_pool_stat_bytes, shared_pool_assignment_bytes
   )

5. Provider-Specific Mapping Consolidation:
   GetRatePlanToRatePoolMappingByPortalType(queue_ids, CrossProvider)
   
   IF portal_type == CrossProvider:
     m2m_mappings = GetRatePlanToRatePoolMapping(queue_ids, M2M)
     mobility_mappings = GetRatePlanToRatePoolMapping(queue_ids, Mobility)
     consolidated_mappings = m2m_mappings + mobility_mappings
     return consolidated_mappings
   ELSE:
     return GetRatePlanToRatePoolMapping(queue_ids, portal_type)
```

**Code Location:** `AltaworxSimCardCostOptimizerCleanup.cs`
- Lines 2276-2331: `WriteCrossProviderCustomerResults()` - Main consolidation logic
- Lines 821-833: `GenerateCustomerSpecificRatePools()` - Cross-provider rate pool generation
- Lines 2325: Multi-tab Excel generation for cross-provider results
- Lines 2332-2344: `GetRatePlanToRatePoolMappingByPortalType()` - Provider mapping consolidation

---

## 6. Provider-Specific Completion Criteria and Validation

### What:
Handles provider-specific completion criteria, validates results across different provider types, and ensures data consistency.

### Why:
Different providers (M2M, Mobility, CrossProvider) have unique validation requirements and completion workflows.

### How:

**Algorithm: Provider-Specific Validation and Completion**
```
1. Provider Type Dispatch and Validation:
   WriteResultByPortalType(is_customer_optimization, instance, billing_period, queue_ids, uses_proration)
   
   IF instance.portal_type == Mobility:
     IF is_customer_optimization:
       return WriteMobilityResults(instance, queue_ids, billing_period, uses_proration, is_customer_optimization)
     ELSE:
       return WriteMobilityCarrierResults(instance, queue_ids, billing_period, uses_proration)
       
   ELSE IF instance.portal_type == M2M:
     return WriteM2MResults(instance, queue_ids, billing_period, uses_proration, is_customer_optimization)
     
   ELSE IF instance.portal_type == CrossProvider:
     // Cross-Provider optimization currently only supports Customer Optimization
     return WriteCrossProviderCustomerResults(instance, queue_ids, uses_proration)

2. Mobility Provider Validation:
   WriteMobilityCarrierResults(instance, queue_ids, billing_period, uses_proration)
   
   // Validate optimization groups and rate plan assignments
   optimization_groups = carrierRatePlanRepository.GetValidOptimizationGroupsWithRatePlanIds(service_provider_id)
   device_results = optimizationMobilityDeviceRepository.GetMobilityDeviceResults(queue_ids, billing_period)
   
   // Validation criteria: rate_plan_type_id and optimization_group_id must not be null
   invalid_devices = device_results.filter(x => x.rate_plan_type_id == null OR x.optimization_group_id == null)
   IF invalid_devices.any():
     LogError("NULL_RATE_PLAN_TYPE_ID_OPTIMIZATION_GROUP_ID", invalid_devices.iccid_list)
   
   valid_device_results = device_results.filter(x => x.rate_plan_type_id != null AND x.optimization_group_id != null)
   device_results_by_optimization_groups = valid_device_results.group_by(x => x.optimization_group_id)

3. Cross-Provider Completion Coordination:
   ProcessResultForCrossProvider(is_customer_optimization, is_last_instance, instance, file_result)
   
   IF is_customer_optimization:
     customer = GetRevCustomerById(instance.rev_customer_id)
     
     // Update cross-provider processing instance with completion status
     crossProviderOptimizationRepository.UpdateProcessingCustomerOptimizationInstance(
       session_id: instance.session_id,
       instance_id: instance.id,
       error_message: null,
       total_device_count: file_result.total_device_count,
       is_error: false,
       customer_type: instance.customer_type,
       rev_customer_id: customer.rev_customer_id,
       amop_customer_id: instance.amop_customer_id
     )
     
     IF is_last_instance:
       // All cross-provider instances completed - trigger final email
       QueueLastStepOptCustomerCleanup(
         instance.id, instance.session_id, 
         send_email=true, service_provider_id=0, 
         opt_customer_cleanup_delay_seconds
       )

4. Rate Plan Update Feasibility Validation:
   DoesHaveTimeToProcessRatePlanUpdates(instance, rate_plans_to_update_count, connection_string, logger, current_time, timezone)
   
   // Get historical rate plan update performance
   rate_plan_update_summaries = GetPreviousRatePlanUpdateSummary(instance.id, connection_string)
   
   // Calculate time requirements
   minutes_remaining_in_bill_cycle = MinutesRemainingInBillCycle(instance.billing_period_end_date, current_time, timezone)
   minutes_to_update_rate_plans = MinutesToUpdateRatePlans(rate_plans_to_update_count, rate_plan_update_summaries)
   
   // Validation criteria: leave 10-minute buffer
   can_complete_updates = (minutes_remaining_in_bill_cycle > 0 AND 
                          minutes_remaining_in_bill_cycle - minutes_to_update_rate_plans >= 10)
   
   IF can_complete_updates:
     QueueRatePlanUpdates(instance.id, tenant_id)
     SendGoForRatePlanUpdatesEmail(instance, billing_timezone)
   ELSE:
     SendNoGoForRatePlanUpdatesEmail(instance, billing_timezone)

5. Instance Status Validation:
   CleanupInstance(instance_id, is_customer_optimization, is_last_instance, service_provider_id)
   
   // Validation: Check instance status before processing
   IF INSTANCE_FINISHED_STATUSES.contains(instance.run_status_id):
     LogWarning("Duplicated instance cleanup request for instance {instance_id}")
     return
   
   // Validation: Ensure instance exists
   IF instance.id <= 0:
     LogError("Could not find instance with id {instance_id}")
     return
```

**Code Location:** `AltaworxSimCardCostOptimizerCleanup.cs`
- Lines 424-444: `WriteResultByPortalType()` - Provider dispatch and validation
- Lines 659-661: Mobility provider validation for null rate plan types and optimization groups
- Lines 2346-2361: `ProcessResultForCrossProvider()` - Cross-provider completion coordination
- Lines 446-468: `DoesHaveTimeToProcessRatePlanUpdates()` - Rate plan update feasibility validation
- Lines 313-322: Instance status validation in `CleanupInstance()`

---

## Summary

This cross-provider coordination system provides comprehensive management of multi-provider telecommunications optimization through:

1. **Consolidated Email Notifications** - Unified provider results with HTML templates and Excel attachments
2. **Multi-Provider Processing Tracking** - OptimizationCustomerProcessing table coordination across providers
3. **Session Management** - Cross-provider instance coordination with last-instance detection  
4. **Advanced Retry Logic** - Queue-based delays, exponential backoff, and configurable retry limits
5. **Result Consolidation** - Multi-tab Excel generation with provider-specific breakdowns
6. **Provider-Specific Validation** - Completion criteria tailored to M2M, Mobility, and CrossProvider types

The system ensures all provider instances complete successfully before sending consolidated results while maintaining individual provider insights and handling various failure scenarios through sophisticated retry and delay mechanisms.