# Customer Optimization Process - Step-by-Step Guide

This document provides a comprehensive overview of the customer optimization process implemented through three AWS Lambda functions in the Altaworx SimCard Cost Optimizer system.

## Overview

The customer optimization process is designed to optimize SIM card rate plans for specific customers to minimize costs while maintaining service quality. The process involves three main Lambda functions that work sequentially:

1. **QueueCustomerOptimization** - Initiates and queues customer optimization requests
2. **SimCost Optimizer** - Performs the actual optimization calculations
3. **SimCost Optimizer Cleanup** - Finalizes results and sends notifications

## Lambda Function 1: QueueCustomerOptimization

### Purpose
This Lambda function serves as the entry point for customer optimization. It processes SQS messages containing customer optimization requests and sets up the optimization environment.

### Key Responsibilities
- Validates incoming SQS messages
- Extracts customer information and billing period details
- Determines portal type (M2M, Mobility, or Cross-Provider)
- Sets up optimization instances
- Queues optimization jobs for processing

### Step-by-Step Process

#### 1. Message Validation and Extraction
```
Input: SQS Event with message attributes
- CustomerType (SiteTypes)
- TenantId
- OptimizationSessionId
- CustomerId or AMOPCustomerId
- BillPeriodId or BillYear/BillMonth
- ServiceProviderId
- IntegrationAuthenticationId (for Rev customers)
- PortalType (optional, defaults to M2M)
```

#### 2. Customer Type Processing
The function branches based on customer type:

**For Rev Customers (SiteTypes.Rev):**
- Requires CustomerId (GUID)
- Requires IntegrationAuthenticationId
- Uses `ProcessCustomerId()` method

**For AMOP Customers (SiteTypes.AMOP):**
- Uses AMOPCustomerId (integer)
- Uses `ProcessAMOPCustomerId()` method

**For Cross-Provider Customers:**
- Uses different portal type logic
- Processes multiple service providers
- Uses `ProcessCrossProviderCustomerOptimization()` method

#### 3. Rate Plan Retrieval
- Fetches customer-specific rate plans for the billing period
- Determines if Bill-in-Advance is eligible
- Validates rate plan availability

#### 4. Optimization Instance Creation
- Creates optimization instance in database
- Sets billing period information
- Configures charge type (RateChargeAndOverage or OverageOnly)
- Initializes Redis cache if available

#### 5. Device Processing by Rate Plans
The function processes devices grouped by rate plans:

**Auto-Change Disabled Rate Plans:**
- Processes devices with fixed rate pool assignments
- Uses existing rate plan pooling logic

**Auto-Change Enabled Rate Plans:**
- Groups devices by rate plan code
- Creates permutation queues for optimization
- Generates multiple optimization scenarios

#### 6. Queue Generation
- Creates communication plan groups
- Generates rate pool sequences
- Creates optimization queues for each permutation
- Enqueues cleanup tasks with appropriate delays

### Error Handling
- Validates zero-value rate plans
- Handles missing billing periods
- Manages Redis cache connectivity issues
- Sends error notifications to AMOP 2.0

## Lambda Function 2: SimCost Optimizer

### Purpose
This Lambda function performs the core optimization calculations using advanced algorithms to determine the most cost-effective rate plan assignments for devices.

### Key Responsibilities
- Processes optimization queue items
- Performs rate pool calculations
- Executes optimization algorithms
- Records optimization results
- Manages partial processing for large datasets

### Step-by-Step Process

#### 1. Queue Processing Setup
```
Input: SQS Event with QueueIds
- QueueIds (comma-separated list)
- SkipLowerCostCheck (optional)
- ChargeType (optimization charge type)
- IsChainingProcess (for large datasets)
```

#### 2. Instance and Device Retrieval
- Validates queue status to prevent duplicate processing
- Retrieves optimization instance details
- Loads billing period information
- Fetches device data based on portal type:
  - M2M: Uses communication plans
  - Mobility: Uses optimization groups
  - Cross-Provider: Uses cross-provider device repository

#### 3. Rate Pool Creation
- Calculates maximum average usage from rate plans
- Creates rate pools using RatePoolFactory
- Determines pooling strategy based on portal type
- Sets up rate pool collection for optimization

#### 4. Optimization Algorithm Execution
The system uses RatePoolAssigner with multiple strategies:

**Grouping Strategies:**
- No Grouping
- Group by Communication Plan (M2M only)

**Assignment Orders:**
- Largest to Smallest
- Smallest to Largest

**Pooling Options:**
- Individual device assignment
- Shared pooling between rate plans
- Customer rate pool optimization

#### 5. Time Management and Chaining
- Monitors Lambda execution time
- Implements Redis caching for partial results
- Chains to new Lambda instances for large datasets
- Manages sanity check time limits

#### 6. Result Recording
- Records best optimization result
- Updates queue status
- Saves device assignments
- Calculates cost savings

### Optimization Features
- **Proration Support**: Handles mid-cycle activations
- **Bill-in-Advance**: Supports advance billing scenarios
- **Cross-Customer Pooling**: Optimizes across customer boundaries
- **Rate Plan Filtering**: Filters by rate plan types for Mobility

## Lambda Function 3: SimCost Optimizer Cleanup

### Purpose
This Lambda function finalizes the optimization process by cleaning up temporary data, generating reports, and sending results to stakeholders.

### Key Responsibilities
- Waits for all optimization queues to complete
- Determines winning optimization scenarios
- Generates Excel reports
- Sends email notifications
- Manages rate plan updates
- Cleans up temporary data

### Step-by-Step Process

#### 1. Queue Completion Monitoring
- Monitors SQS queue length
- Implements retry logic with exponential backoff
- Ensures all optimization instances are complete
- Prevents premature cleanup execution

#### 2. Result Aggregation and Winner Selection
- Identifies winning queue per communication group
- Selects lowest cost optimization scenario
- Aggregates results across all communication groups
- Handles different portal types appropriately

#### 3. Report Generation
The system generates comprehensive Excel reports:

**For M2M Optimization:**
- Device assignment details
- Rate pool statistics
- Cost comparison analysis
- Cross-pooling results (if applicable)

**For Mobility Optimization:**
- Optimization group summaries
- Device assignment exports
- Rate plan change recommendations

**For Cross-Provider Optimization:**
- Multi-provider cost analysis
- Consolidated device assignments
- Service provider comparisons

#### 4. Email Notification System
**Carrier Optimization Results:**
- Sent to operations team
- Includes optimization statistics
- Contains Excel attachment with results
- Shows sync status and device counts

**Customer Optimization Results:**
- Sent to customer contacts
- Personalized with customer information
- Includes billing period details
- Contains optimization recommendations

#### 5. Rate Plan Update Management
For eligible carrier optimizations:
- Calculates rate plan update requirements
- Estimates time needed for updates
- Determines if updates can complete before billing cycle
- Queues automatic rate plan updates or sends manual notifications

#### 6. Cross-Provider Processing
- Handles multi-service provider scenarios
- Consolidates results across providers
- Manages complex customer billing relationships
- Coordinates with AMOP 2.0 for notifications

#### 7. Data Cleanup
- Removes temporary optimization results
- Keeps only winning scenario data
- Cleans up queue rate plan assignments
- Maintains audit trail for completed optimizations

### Error Handling and Retry Logic
- Implements configurable retry counts
- Manages timeout scenarios
- Handles email delivery failures
- Provides comprehensive error logging

## Process Flow Summary

```
1. Customer Optimization Request (SQS Message)
   ↓
2. QueueCustomerOptimization Lambda
   - Validates request
   - Creates optimization instance
   - Processes customer rate plans
   - Generates optimization queues
   ↓
3. SimCost Optimizer Lambda (Multiple Instances)
   - Processes individual queues
   - Executes optimization algorithms
   - Records results
   - Handles large dataset chaining
   ↓
4. SimCost Optimizer Cleanup Lambda
   - Waits for completion
   - Selects winning scenarios
   - Generates reports
   - Sends notifications
   - Manages rate plan updates
   ↓
5. Process Complete
   - Customer receives optimization results
   - Rate plans updated (if applicable)
   - Audit trail maintained
```

## Key Features and Capabilities

### Multi-Portal Support
- **M2M Portal**: Traditional machine-to-machine optimization
- **Mobility Portal**: Mobile device optimization with group management
- **Cross-Provider Portal**: Multi-carrier optimization scenarios

### Advanced Optimization Features
- **Auto-Change Rate Plans**: Dynamic rate plan switching during optimization
- **Customer Rate Pools**: Shared data pooling across customer devices
- **Bill-in-Advance**: Future billing period optimization
- **Proration**: Mid-cycle activation cost adjustments

### Scalability and Performance
- **Redis Caching**: Improves performance for large datasets
- **Lambda Chaining**: Handles datasets too large for single execution
- **Parallel Processing**: Multiple optimization scenarios run concurrently
- **Queue Management**: Prevents resource contention

### Integration Points
- **AMOP 2.0**: External system notifications
- **Email System**: AWS SES for result delivery
- **Database**: SQL Server for data persistence
- **SQS**: Message queuing for process coordination

## Monitoring and Troubleshooting

### Key Metrics to Monitor
- Queue processing times
- Optimization completion rates
- Email delivery success
- Redis cache hit rates
- Lambda execution durations

### Common Issues and Solutions
- **Redis Connectivity**: System continues without cache if unavailable
- **Queue Timeouts**: Automatic retry with exponential backoff
- **Large Datasets**: Automatic chaining to prevent timeouts
- **Email Failures**: Retry logic with configurable attempts

This comprehensive process ensures optimal SIM card rate plan assignments while maintaining system reliability and performance across different customer types and optimization scenarios.