# Customer Optimization System Analysis

## Overview

The customer optimization system consists of three main AWS Lambda functions that work together to optimize SIM card costs for customers:

1. **Altaworx.SimCard.Cost.Optimizer** - Main optimization engine
2. **Altaworx.SimCard.Cost.Optimizer.Cleanup** - Post-optimization cleanup and reporting
3. **Altaworx.SimCard.Cost.QueueCustomerOptimization** - Customer optimization queue processor

## System Architecture

### 1. Altaworx.SimCard.Cost.Optimizer (AltaworxSimCardCostOptimizer.cs)

**Purpose**: Main optimization engine that processes SIM card assignments to optimal rate plans

**Key Features**:
- Processes SQS messages containing optimization queue items
- Supports multiple portal types: M2M, Mobility, CrossProvider
- Implements Redis caching for performance optimization
- Uses RatePoolAssigner for optimal SIM card assignments
- Supports both carrier and customer optimization modes

**Core Functionality**:
- **ProcessQueues**: Main processing logic for optimization queues
- **ProcessQueuesContinue**: Continuation logic for chained optimization processes
- **WrapUpCurrentInstance**: Handles completion/continuation of optimization instances
- **GetCustomerRatePoolsByCommGroupId**: Retrieves customer rate pools for optimization

**Portal Type Support**:
- **M2M**: Uses communication plans for optimization
- **Mobility**: Uses optimization groups for carrier optimization
- **CrossProvider**: Cross-provider customer optimization

**Assignment Strategies**:
- No Grouping + Largest to Smallest
- No Grouping + Smallest to Largest  
- Group by Communication Plan + Largest to Smallest
- Group by Communication Plan + Smallest to Largest

### 2. Altaworx.SimCard.Cost.Optimizer.Cleanup (AltaworxSimCardCostOptimizerCleanup.cs)

**Purpose**: Post-optimization cleanup, result processing, and email notifications

**Key Features**:
- Monitors optimization queue completion
- Generates Excel reports with optimization results
- Sends email notifications to customers
- Handles rate plan update queuing
- Processes both single and cross-provider optimizations

**Core Functionality**:
- **CleanupInstance**: Main cleanup orchestration
- **WriteResultByPortalType**: Generates results based on portal type
- **SendResults**: Email result delivery
- **ProcessResultForSingleServiceProvider**: Single provider result processing
- **ProcessResultForCrossProvider**: Cross-provider result processing

**Result Generation**:
- **M2M Results**: Processes M2M device assignments
- **Mobility Results**: Handles mobility device optimization
- **CrossProvider Results**: Cross-provider optimization results

**Email Integration**:
- Sends optimization completion emails
- Includes Excel attachments with detailed results
- Handles retry logic for email delivery
- Supports both Rev and AMOP customer types

### 3. Altaworx.SimCard.Cost.QueueCustomerOptimization (AltaworxSimCardCostQueueCustomerOptimization.cs)

**Purpose**: Customer-specific optimization queue processor

**Key Features**:
- Processes customer optimization requests
- Supports M2M and Cross-Provider optimizations
- Handles customer rate plan assignments
- Manages bill-in-advance scenarios
- Implements auto-change rate plan logic

**Core Functionality**:
- **ProcessCustomerOptimizationByPortalType**: Routes optimization by portal type
- **ProcessCustomerId**: Handles Rev customer optimization
- **ProcessAMOPCustomerId**: Handles AMOP customer optimization
- **ProcessCrossProviderCustomerOptimization**: Cross-provider optimization
- **ProcessDevicesByCustomerRatePlans**: Device assignment logic

**Customer Types Supported**:
- **Rev Customers**: Revenue customers with GUID identifiers
- **AMOP Customers**: AMOP customers with integer identifiers
- **Cross-Provider**: Multi-provider customer optimization

## Key Algorithms and Logic

### Rate Pool Assignment Algorithm

The system uses a sophisticated rate pool assignment algorithm that:
1. Groups SIM cards by various criteria (communication plans, rate pools)
2. Calculates optimal assignments using multiple strategies
3. Considers usage patterns, cost optimization, and pooling benefits
4. Supports both carrier and customer optimization modes

### Customer Rate Pool Processing

**Auto-Change Rate Plans**:
- Rate plans with `AutoChangeRatePlan = true` participate in algorithmic optimization
- System generates permutations of rate plan assignments
- Uses RatePoolAssigner to find optimal combinations

**Fixed Rate Pool Assignments**:
- Rate plans with `AutoChangeRatePlan = false` are processed separately
- Devices are assigned to their designated rate pools without optimization
- System validates zero-value rate plans and handles errors

### Optimization Strategies

1. **Communication Plan Grouping**: Groups devices by communication plan for optimized assignments
2. **Rate Pool Pooling**: Leverages shared rate pools for cost optimization
3. **Bill-in-Advance**: Supports future billing period optimization
4. **Cross-Customer Pooling**: Allows pooling across different customers for better rates

## Data Flow

```
SQS Message → Queue Processor → Optimization Engine → Results Processing → Cleanup → Email Notification
```

1. **Message Reception**: SQS messages trigger optimization processes
2. **Queue Processing**: Messages are parsed and validated
3. **Optimization Execution**: Core optimization algorithms run
4. **Result Storage**: Results are saved to database
5. **Report Generation**: Excel reports are created
6. **Email Delivery**: Results are emailed to customers
7. **Cleanup**: Temporary data is cleaned up

## Integration Points

### External Systems
- **SQS**: Message queuing for optimization requests
- **Redis**: Caching layer for performance optimization
- **SQL Server**: Data persistence and retrieval
- **Amazon SES**: Email delivery service
- **AMOP API**: Integration with AMOP 2.0 system

### Database Tables
- `OptimizationInstance`: Tracks optimization instances
- `OptimizationQueue`: Manages optimization queues
- `OptimizationDeviceResult`: Stores device optimization results
- `CustomerRatePool`: Customer-specific rate pool definitions
- `JasperCustomerRatePlan`: Customer rate plan definitions

## Error Handling and Monitoring

### Error Scenarios
- Missing rate plans or invalid configurations
- Zero-value rate plans detection
- Redis cache connectivity issues
- Database connection failures
- Email delivery failures

### Monitoring Features
- Comprehensive logging throughout the process
- Email notifications for configuration issues
- Retry logic for transient failures
- Status tracking for long-running processes

## Performance Optimizations

### Caching Strategy
- Redis caching for SIM card data
- Partial assignment caching for long-running optimizations
- Cache invalidation on process completion

### Parallel Processing
- Multiple optimization strategies run in parallel
- Asynchronous processing for large datasets
- Queue-based processing for scalability

### Resource Management
- Lambda timeout management
- Memory optimization for large datasets
- Connection pooling for database operations

## Configuration Management

### Environment Variables
- `QueuesPerInstance`: Number of queues per optimization instance
- `SanityCheckTimeLimit`: Time limit for optimization sanity checks
- `OptCustomerCleanUpDelaySeconds`: Delay for customer cleanup operations
- `ErrorNotificationEmailReceiver`: Email for error notifications

### Message Attributes
- `CustomerType`: Rev or AMOP customer type
- `PortalType`: M2M, Mobility, or CrossProvider
- `BillingPeriodId`: Billing period for optimization
- `IsCustomerOptimization`: Flag for customer vs carrier optimization
- `ServiceProviderIds`: Service provider identifiers

## Security Considerations

- AWS IAM roles for service permissions
- Encrypted data transmission
- Secure database connections
- Email content sanitization
- Input validation and sanitization

## Scalability Features

- Horizontal scaling through Lambda concurrency
- Queue-based processing for load distribution
- Batch processing for large customer bases
- Asynchronous processing patterns
- Resource pooling and reuse

This system provides a comprehensive solution for optimizing SIM card costs across multiple customer types and service providers, with robust error handling, monitoring, and reporting capabilities.