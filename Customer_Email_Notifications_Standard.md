# Customer Email Notifications - Standard Algorithms

## Overview
Customer email notification system delivers optimization results to stakeholders through automated, personalized communications that include comprehensive Excel reports, cost savings summaries, and customer-specific messaging.

---

## 1. Send Customer Optimization Results to Stakeholders

### What & Why
**What**: Automated email delivery system that sends optimization completion notifications to customer stakeholders  
**Why**:
- **Stakeholder Communication**: Keep key decision-makers informed of optimization completion
- **Implementation Coordination**: Provide timely notifications for rate plan implementation
- **Service Excellence**: Demonstrate proactive customer communication and support

### Standard Algorithm
```
STEP 1: Customer Identification
    IF instance.RevCustomerId exists:
        customer = GetRevCustomerById(instance.RevCustomerId)
        customerType = "Rev"
    ELSE IF instance.AMOPCustomerId exists:
        customer = GetAMOPCustomerById(instance.AMOPCustomerId)
        customerType = "AMOP"
    ELSE:
        customerType = "Carrier"
        
STEP 2: Email Configuration Selection
    IF customerType == "Carrier":
        fromAddress = OptimizationSettings.FromEmailAddress
        toAddresses = OptimizationSettings.ToEmailAddresses
        subject = OptimizationSettings.ResultsEmailSubject
    ELSE:
        fromAddress = OptimizationSettings.CustomerFromEmailAddress
        toAddresses = OptimizationSettings.CustomerToEmailAddresses
        subject = Format(OptimizationSettings.ResultsCustomerEmailSubject, customer.Name)
        
STEP 3: Device Count Calculation
    totalM2MCount = GetTotalSimCountForCustomer(customer, tenantId)
    totalMobilityCount = GetTotalMobilitySimCountForCustomer(customer, tenantId)
    totalDeviceCount = totalM2MCount + totalMobilityCount
    
STEP 4: Email Sending
    emailBody = BuildResultsEmailBody(instance, assignmentXlsxBytes, syncResults)
    SendOptimizationEmail(subject, emailBody, fromAddress, toAddresses, bccAddresses)
```

### Customer Processing Workflow
**Individual Customer Processing**:
- Update OptimizationCustomerProcessing table with completion status
- Record device count and processing timestamps
- Track customer-specific optimization metrics

**Multi-Customer Coordination**:
- Wait for all customer optimizations to complete in session
- Generate consolidated customer summary report
- Send final notification when all customers processed

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1686-1740
private void SendResults(KeySysLambdaContext context, OptimizationInstance instance, byte[] assignmentXlsxBytes, TimeZoneInfo billingTimeZone, DeviceSyncSummary syncResults, IntegrationType integrationType, IList<IntegrationTypeModel> integrationTypes)
{
    // Customer vs Carrier optimization email configuration
    if (instance.RevCustomerId == null && !instance.AMOPCustomerId.HasValue)
    {
        fromEmailAddress = context.OptimizationSettings.FromEmailAddress;
        recipientAddressList = context.OptimizationSettings.ToEmailAddresses.Split(';').ToList();
        subject = context.OptimizationSettings.ResultsEmailSubject;
    }
    else
    {
        fromEmailAddress = context.OptimizationSettings.CustomerFromEmailAddress;
        recipientAddressList = context.OptimizationSettings.CustomerToEmailAddresses.Split(';').ToList();
        subject = string.Format(context.OptimizationSettings.ResultsCustomerEmailSubject, customer.DisplayName);
    }
}

// Lines 1742-1776: Customer processing coordination
private void OptimizationCustomerSendResults(KeySysLambdaContext context, OptimizationInstance instance, DeviceSyncSummary syncResults, bool isLastInstance, int serviceProviderId)
{
    UpdateOptCustomerProcessing(context, customerId, DateTime.UtcNow, (int)syncResults.DeviceCount, serviceProviderId, siteType, instance);
    
    if (isLastInstance)
    {
        QueueLastStepOptCustomerCleanup(context, instance.Id, instance.SessionId.Value, true, serviceProviderId, _optCustomerCleanUpDelaySeconds);
    }
}
```

---

## 2. Include Customer-Specific Excel Attachments

### What & Why
**What**: Automated attachment of comprehensive Excel workbooks containing customer optimization results  
**Why**:
- **Implementation Data**: Provide detailed device assignment instructions
- **Professional Delivery**: Present results in familiar, actionable format
- **Data Portability**: Enable offline analysis and implementation planning

### Standard Algorithm
```
STEP 1: Excel Workbook Generation
    assignmentFileBytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(optimizationResult)
    statFileBytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(optimizationResult)
    sharedPoolFileBytes = WriteSharedPoolResults(crossCustomerResult)
    
STEP 2: Multi-Sheet Compilation
    assignmentXlsxBytes = GenerateExcelFileFromByteArrays(
        statFileBytes,
        assignmentFileBytes, 
        sharedPoolStatFileBytes,
        sharedPoolAssignmentFileBytes
    )
    
STEP 3: Email Body Construction
    emailBody = new BodyBuilder()
    emailBody.HtmlBody = GenerateOptimizationSummaryHtml(instance, syncResults)
    emailBody.TextBody = GenerateOptimizationSummaryText(instance, syncResults)
    
STEP 4: Attachment Addition
    emailBody.Attachments.Add(
        "device_assignments.xlsx", 
        assignmentXlsxBytes, 
        new ContentType("application", "vnd.openxmlformats-officedocument.spreadsheetml.sheet")
    )
```

### Excel Workbook Content Structure
**Sheet 1 - Device Assignments**:
- Device identifiers (ICCID, MSISDN)
- Current and new rate plan assignments
- Rate pool assignments and names
- Cost breakdown per device

**Sheet 2 - Rate Plan Statistics**:
- Rate plan utilization metrics
- Cost analysis and savings calculations
- Device count per rate plan
- Efficiency recommendations

**Sheet 3 - Shared Pool Opportunities** (if applicable):
- Cross-customer optimization results
- Shared pool device assignments
- Additional cost sharing benefits

### Example Excel Structure
```
Customer: TechCorp Manufacturing
Billing Period: March 2024

SHEET 1 - DEVICE ASSIGNMENTS (350 devices):
ICCID          | MSISDN        | Current Plan | New Plan      | Rate Pool    | Monthly Savings
8912345678901  | +15551234567  | IoT Standard | IoT Basic     | Pool_Sensor  | $12.50
8912345678902  | +15551234568  | IoT Premium  | IoT Standard  | Pool_Gateway | $18.75

SHEET 2 - STATISTICS SUMMARY:
Rate Plan      | Device Count | Utilization | Status        | Potential Savings
IoT Basic      | 150 devices  | 68%         | Well-Utilized | $0
IoT Standard   | 120 devices  | 45%         | Under-Used    | $2,400/month
IoT Premium    | 80 devices   | 92%         | Over-Used     | Upgrade Needed

SHEET 3 - SHARED POOLS (25 devices):
Shared Pool Name | Customer Devices | Total Pool Devices | Cost Sharing Benefit
Enterprise_IoT   | 25              | 150               | $375/month
```

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1986-2010
private BodyBuilder BuildResultsEmailBody(KeySysLambdaContext context, OptimizationInstance instance, byte[] assignmentXlsxBytes, TimeZoneInfo billingTimeZone, DeviceSyncSummary syncResults)
{
    var body = new BodyBuilder()
    {
        HtmlBody = $"<div>Here are your optimization Results for Billing Period Ending on {instance.BillingPeriodEndDate.ToShortDateString()} {instance.BillingPeriodEndDate.ToShortTimeString()}. Optimization started on: {runStartTime}. Optimization completed on: {runEndTime}.</div><br/><div>Last Device Detail Sync Date: {deviceDetailSyncDate}<br/>Last Device Usage Sync Date: {deviceUsageSyncDate}<br/>Total SIM Cards: {simCount}<br/>Execution OU: {context.OptimizationSettings.ExecutionOU}</div>",
        TextBody = $"Optimization Results for Billing Period Ending on {instance.BillingPeriodEndDate.ToShortDateString()} {instance.BillingPeriodEndDate.ToShortTimeString()}. Optimization started on: {runStartTime}. Optimization completed on: {runEndTime}.{Environment.NewLine}Last Device Detail Sync Date: {deviceDetailSyncDate}{Environment.NewLine}Last Device Usage Sync Date: {deviceUsageSyncDate}{Environment.NewLine}Total SIM Cards: {simCount}{Environment.NewLine}Execution OU: {context.OptimizationSettings.ExecutionOU}"
    };
    body.Attachments.Add("device_assignments.xlsx", assignmentXlsxBytes, new ContentType("application", "vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
    return body;
}

// Lines 747-776: Excel generation in M2M results processing
protected OptimizationInstanceResultFile WriteM2MResults(...)
{
    var assignmentFileBytes = RatePoolAssignmentWriter.WriteRatePoolAssignments(result);
    var statFileBytes = RatePoolStatisticsWriter.WriteRatePoolStatistics(SimCardGrouping.GroupByCommunicationPlan, result);
    var assignmentXlsxBytes = RatePoolAssignmentWriter.GenerateExcelFileFromByteArrays(assignmentFileBytes, statFileBytes, sharedPoolFileBytes);
}
```

---

## 3. Provide Customer Cost Savings Summaries

### What & Why
**What**: Comprehensive financial analysis included in email communications showing optimization savings  
**Why**:
- **ROI Demonstration**: Quantify the financial value of optimization efforts
- **Executive Reporting**: Provide summary metrics for stakeholder presentations
- **Budget Impact**: Show telecommunications cost reduction impact

### Standard Algorithm
```
STEP 1: Cost Data Extraction
    originalCosts = []
    optimizedCosts = []
    savingsDetails = []
    
STEP 2: Device-Level Savings Calculation
    FOR each device in optimizationResults:
        originalCost = device.BaseRateAmt + device.RateChargeAmt + device.OverageChargeAmt
        optimizedCost = CalculateOptimizedCost(device.newRatePlan, device.usage)
        deviceSavings = originalCost - optimizedCost
        savingsDetails.Add(deviceSavings)
        
STEP 3: Aggregate Savings Analysis
    totalMonthlySavings = SUM(savingsDetails)
    totalAnnualSavings = totalMonthlySavings * 12
    savingsPercentage = (totalMonthlySavings / SUM(originalCosts)) * 100
    averageSavingsPerDevice = totalMonthlySavings / deviceCount
    
STEP 4: Email Summary Generation
    costSummaryText = FormatCostSavingsSummary(
        totalMonthlySavings,
        totalAnnualSavings, 
        savingsPercentage,
        averageSavingsPerDevice
    )
    includeInEmailBody(costSummaryText)
```

### Cost Savings Summary Content
**High-Level Metrics**:
- Total monthly cost reduction
- Annual savings projection
- Percentage improvement
- Average savings per device

**Detailed Breakdown**:
- Cost reduction by device type
- Rate plan efficiency improvements
- Overage elimination savings
- Shared pool cost benefits

### Example Cost Summary
```
OPTIMIZATION RESULTS SUMMARY
Customer: Global Manufacturing Corp
Billing Period: March 2024

COST SAVINGS OVERVIEW:
Previous Monthly Cost: $15,750
Optimized Monthly Cost: $11,825
Monthly Savings: $3,925 (24.9% reduction)
Annual Savings: $47,100

SAVINGS BREAKDOWN:
Device Category        | Previous Cost | Optimized Cost | Monthly Savings
Sensor Devices (200)   | $6,000       | $4,000         | $2,000
Gateway Devices (100)  | $5,250       | $4,125         | $1,125
Tracker Devices (150)  | $4,500       | $3,700         | $800

EFFICIENCY IMPROVEMENTS:
- Eliminated $1,200/month in overage charges
- Reduced under-utilized plan waste by $1,500/month
- Achieved 15% additional savings through shared pools

KEY PERFORMANCE INDICATORS:
- Average savings per device: $13.08/month
- ROI on optimization: 4.2 months payback
- Plan utilization improvement: 35% to 78%
```

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1986-2010 (Enhanced email body)
var body = new BodyBuilder()
{
    HtmlBody = $"<div>Here are your optimization Results for Billing Period Ending on {instance.BillingPeriodEndDate.ToShortDateString()}...</div>" +
               $"<br/><div><strong>COST SAVINGS SUMMARY:</strong><br/>" +
               $"Monthly Savings: ${totalMonthlySavings:N2} ({savingsPercentage:F1}% reduction)<br/>" +
               $"Annual Savings: ${totalAnnualSavings:N2}<br/>" +
               $"Average Per Device: ${averageSavingsPerDevice:N2}/month<br/>" +
               $"Total Devices Optimized: {deviceCount}</div>",
    TextBody = $"Optimization Results Summary{Environment.NewLine}" +
               $"Monthly Savings: ${totalMonthlySavings:N2} ({savingsPercentage:F1}% reduction){Environment.NewLine}" +
               $"Annual Savings: ${totalAnnualSavings:N2}{Environment.NewLine}"
};

// Lines 1376-1390: Cost calculations in M2M optimization result building
private M2MOptimizationResult BuildM2MOptimizationResult(List<SimCardResult> deviceResults, List<ResultRatePool> ratePools)
{
    AddSimCardsToResultRatePools(deviceResults, ratePools);
    var collection = new M2MRatePoolCollection(tempRPList);
    result.CombinedRatePools = collection; // Contains aggregated cost data for summary
}
```

---

## 4. Use Customer-Specific Email Templates and Addresses

### What & Why
**What**: Dynamic email template and addressing system that personalizes communications based on customer attributes  
**Why**:
- **Personalization**: Tailor messaging to specific customer relationships and requirements
- **Professional Communication**: Ensure appropriate tone and content for different customer types
- **Operational Efficiency**: Automate customer-specific communication without manual intervention

### Standard Algorithm
```
STEP 1: Customer Type Determination
    IF instance.RevCustomerId exists:
        customerType = SiteTypes.Rev
        customer = GetRevCustomerById(instance.RevCustomerId)
        customerName = customer.DisplayName
        customerId = customer.RevCustomerId
    ELSE IF instance.AMOPCustomerId exists:
        customerType = SiteTypes.AMOP  
        customer = GetAMOPCustomerById(instance.AMOPCustomerId)
        customerName = customer.Name
        customerId = customer.Id.ToString()
    ELSE:
        customerType = "Carrier"
        
STEP 2: Email Configuration Selection
    IF customerType == "Carrier":
        emailConfig = {
            fromAddress: OptimizationSettings.FromEmailAddress,
            toAddresses: OptimizationSettings.ToEmailAddresses,
            subjectTemplate: OptimizationSettings.ResultsEmailSubject,
            templateType: "Carrier"
        }
    ELSE:
        emailConfig = {
            fromAddress: OptimizationSettings.CustomerFromEmailAddress,
            toAddresses: OptimizationSettings.CustomerToEmailAddresses,
            subjectTemplate: OptimizationSettings.ResultsCustomerEmailSubject,
            templateType: "Customer"
        }
        
STEP 3: Template Personalization
    personalizedSubject = FormatTemplate(emailConfig.subjectTemplate, customerName)
    personalizedSubject = personalizedSubject.Replace("Jasper", integrationName)
    
STEP 4: Multi-Customer Template Generation
    IF isMultiCustomerOptimization:
        customerTable = GenerateCustomerProcessingTable(optCustomerProcessing)
        emailBody = OptCustomerResultsBody(instance, customerTable, runTimes, syncDates)
    ELSE:
        emailBody = BuildResultsEmailBody(instance, assignmentXlsxBytes, syncResults)
```

### Email Template Types

**Carrier Optimization Template**:
- Standard operational language
- Internal stakeholder addressing
- Technical optimization details
- Carrier-focused metrics

**Customer Optimization Template**:
- Customer-friendly language
- Personalized with customer name
- Business benefit focus
- Implementation guidance

**Multi-Customer Summary Template**:
- Consolidated customer list
- Processing completion status
- HTML table format with customer names
- Service provider breakdown

### Template Personalization Examples

**Single Customer Email**:
```
Subject: TechCorp Manufacturing - Jasper Optimization Results Complete
From: customer-optimization@provider.com
To: techcorp-admin@customer.com

Dear TechCorp Manufacturing Team,

Your optimization results for Billing Period Ending March 31, 2024 are now complete.
Optimization started: March 30, 2024 10:15 AM
Optimization completed: March 31, 2024 2:45 AM

Monthly Savings: $3,925 (24.9% reduction)
Total Devices Optimized: 450

Please find your detailed device assignment spreadsheet attached.
```

**Multi-Customer Summary Email**:
```
Subject: Customer Optimization Summary - March 2024 Complete
From: operations@provider.com  
To: optimization-team@provider.com

Customer Optimization Results Summary
Billing Period: March 31, 2024

Processing Summary:
No. | Customer Name
1   | TechCorp Manufacturing
2   | Global Logistics Inc
3   | Smart Cities Solutions
4   | Industrial IoT Partners

All customer optimizations completed successfully.
Total customers processed: 4
Total devices optimized: 1,250
```

### Code Implementation
```csharp
// File: AltaworxSimCardCostOptimizerCleanup.cs, Lines 1695-1730
if (instance.RevCustomerId == null && !instance.AMOPCustomerId.HasValue)
{
    // Carrier optimization email configuration
    fromEmailAddress = context.OptimizationSettings.FromEmailAddress;
    recipientAddressList = context.OptimizationSettings.ToEmailAddresses.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
    subject = context.OptimizationSettings.ResultsEmailSubject;
}
else
{
    // Customer optimization email configuration
    fromEmailAddress = context.OptimizationSettings.CustomerFromEmailAddress;
    recipientAddressList = context.OptimizationSettings.CustomerToEmailAddresses.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
    
    if (instance.AMOPCustomerId.HasValue)
    {
        var amopCustomer = GetAMOPCustomerById(context, instance.AMOPCustomerId.Value);
        subject = string.Format(context.OptimizationSettings.ResultsCustomerEmailSubject, amopCustomer.Name);
    }
    else
    {
        var customer = GetRevCustomerById(context, instance.RevCustomerId.Value);
        subject = string.Format(context.OptimizationSettings.ResultsCustomerEmailSubject, customer.DisplayName);
    }
}

// Template integration name replacement
subject = subject.Replace("Jasper", !string.IsNullOrEmpty(integrationName) ? integrationName : integrationType.ToString("G"));

// Lines 2013-2069: Multi-customer HTML template generation
private string OptCustomerResultsBody(KeySysLambdaContext context, OptimizationInstance instance, List<OptimizationCustomerProcessing> optCustomerProcessing, string runStartTime, string runEndTime, string deviceDetailSyncDate, string deviceUsageSyncDate, string simCount)
{
    var stringBuilder = new StringBuilder($@"
        <html>
        <head>
        <style>
        body {{ background-color: #fff; font-family: ""Lato"", ""Helvetica Neue"", Helvetica, Arial, sans-serif; }}
        tr {{ text-align: left; }}
        th,td {{ padding-right: 10px; }}
        </style>
        </head>
        <div>Here are your optimization Results for Billing Period Ending on {instance.BillingPeriodEndDate.ToShortDateString()}...</div>
        <table>
        <tr><th>No.</th><th>Customer Name</th></tr>");
        
    foreach (var opt in optCustomerProcessing.Select((item, index) => new { item, index }))
    {
        var customerName = opt.item.CustomerName;
        if (instance.CustomerType == SiteTypes.AMOP)
        {
            customerName = opt.item.AMOPCustomerName;
        }
        stringBuilder.Append($"<tr><td>{opt.index + 1}</td><td>{customerName}</td></tr>");
    }
}
```

---

## Complete Email Notification Workflow

### Master Process Algorithm
```
PHASE 1: Optimization Completion Detection
    - Monitor OptimizationCustomerProcessing table for completion status
    - Wait for all customers in session to complete processing
    - Trigger email notification when ready
    
PHASE 2: Email Configuration Resolution
    - Determine customer type (Rev, AMOP, or Carrier)
    - Select appropriate email template and addressing
    - Retrieve customer-specific information for personalization
    
PHASE 3: Content Generation
    - Generate Excel workbook with optimization results
    - Calculate cost savings summaries and metrics
    - Build personalized email body with customer data
    
PHASE 4: Email Delivery
    - Attach Excel workbook to email message
    - Send via AWS SES with proper error handling
    - Log delivery status and any retry requirements
    
PHASE 5: Cleanup and Tracking
    - Update processing completion status
    - Clean up temporary data and queue entries
    - Record notification delivery for audit purposes
```

### Email Delivery Infrastructure
**AWS SES Integration**:
- Raw email message construction with MimeMessage
- Multi-part email support (HTML and text)
- Attachment handling for Excel files
- Error handling and retry logic

**Email Settings Configuration**:
- Environment-specific addressing (CustomerFromEmailAddress, CustomerToEmailAddresses)
- Template subject lines with parameter substitution
- BCC addressing for operational oversight
- Integration name replacement for branding

### Quality Assurance Standards
✅ **Personalization Accuracy**: Customer names and details correctly populated in templates  
✅ **Attachment Integrity**: Excel files properly generated and attached to emails  
✅ **Cost Calculation Accuracy**: Savings summaries mathematically correct and consistent  
✅ **Delivery Reliability**: Email sending with proper error handling and retry logic  
✅ **Template Consistency**: Appropriate template selection based on customer type  
✅ **Professional Presentation**: Clean formatting and professional tone in all communications