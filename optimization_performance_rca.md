# Root Cause Analysis: Optimization Performance Issues (v1.0 to v2.0)

## Executive Summary

This document provides a comprehensive Root Cause Analysis (RCA) for performance degradation observed in the Altaworx SimCard Cost Optimizer when transitioning from version 1.0 to version 2.0. The analysis focuses on identifying database tables and optimization processes that may be contributing to the performance issues.

## Problem Statement

Performance degradation has been observed in the optimization process between version 1.0 and version 2.0, with certain optimizations missing or performing slower in the newer version. This RCA aims to identify the specific database tables and processes involved to facilitate targeted investigation and remediation.

## System Overview

The Altaworx SimCard Cost Optimizer is a distributed system that processes optimization requests through AWS Lambda functions, utilizing:
- **Primary Function**: `AltaworxSimCardCostOptimizer.cs` - Main optimization processing
- **Cleanup Function**: `AltaworxSimCardCostOptimizerCleanup.cs` - Post-optimization cleanup and reporting
- **Queue Function**: `AltaworxSimCardCostQueueCustomerOptimization.cs` - Customer optimization queuing

## Database Tables Analysis

### 1. Primary Optimization Result Tables

#### **OptimizationDeviceResult**
- **Purpose**: Stores optimization results for M2M devices
- **Key Columns**: 
  - `QueueId` - Links to optimization queue
  - `AmopDeviceId` - Device identifier
  - `AssignedCarrierRatePlanId` - Assigned carrier rate plan
  - `AssignedCustomerRatePlanId` - Assigned customer rate plan
  - `CustomerRatePoolId` - Rate pool assignment
  - `BaseRateAmt`, `RateChargeAmt`, `OverageChargeAmt` - Cost calculations
- **Performance Impact**: Heavily queried during result generation and cleanup processes
- **RCA Priority**: **HIGH** - Core result storage table

#### **OptimizationMobilityDeviceResult**
- **Purpose**: Stores optimization results for Mobility portal devices
- **Key Columns**: Similar structure to OptimizationDeviceResult
- **Performance Impact**: Frequent JOINs with MobilityDevice table
- **RCA Priority**: **HIGH** - Mobility-specific optimizations

#### **OptimizationSharedPoolResult**
- **Purpose**: M2M shared pool optimization results
- **Performance Impact**: Used in pooled optimization scenarios with complex aggregations
- **RCA Priority**: **MEDIUM** - Pooling logic may have changed between versions

#### **OptimizationMobilitySharedPoolResult**
- **Purpose**: Mobility shared pool optimization results
- **Performance Impact**: Mobility pooled scenarios with cross-device calculations
- **RCA Priority**: **MEDIUM** - Mobility pooling optimizations

### 2. Core Processing Tables

#### **OptimizationQueue**
- **Purpose**: Manages optimization queue items and processing status
- **Key Columns**:
  - `Id` - Queue identifier
  - `InstanceId` - Links to optimization instance
  - `CommPlanGroupId` - Communication plan group
  - `RunStatusId` - Processing status
  - `TotalCost` - Calculated optimization cost
  - `RunEndTime` - Completion timestamp
- **Performance Impact**: Frequently updated during processing, uses `WITH (HOLDLOCK)` for concurrency
- **RCA Priority**: **HIGH** - Queue management and status tracking

#### **OptimizationInstance**
- **Purpose**: Tracks optimization instances and configuration
- **Key Columns**:
  - `Id` - Instance identifier
  - `BillingPeriodId` - Billing period reference
  - `ServiceProviderId` - Service provider
  - `PortalType` - M2M, Mobility, or CrossProvider
  - `IsCustomerOptimization` - Optimization type flag
  - `AMOPCustomerId`, `RevCustomerId` - Customer identifiers
- **Performance Impact**: Referenced throughout optimization lifecycle
- **RCA Priority**: **HIGH** - Core instance management

### 3. Device Tables

#### **Device** (M2M Portal)
- **Purpose**: Core device information for M2M portal
- **Key Columns**: `Id`, `ICCID`, `MSISDN`, `CommunicationPlan`, `ProviderDateActivated`
- **Performance Impact**: Large table with frequent JOINs during optimization
- **RCA Priority**: **HIGH** - Data volume and JOIN performance

#### **MobilityDevice** (Mobility Portal)
- **Purpose**: Core device information for Mobility portal
- **Performance Impact**: Similar to Device table but portal-specific
- **RCA Priority**: **HIGH** - Mobility optimization performance

### 4. Rate Plan and Pool Tables

#### **JasperCarrierRatePlan**
- **Purpose**: Carrier rate plan definitions and configurations
- **Performance Impact**: Referenced during rate plan assignments and calculations
- **RCA Priority**: **MEDIUM** - Rate plan lookup performance

#### **JasperCustomerRatePlan**
- **Purpose**: Customer-specific rate plan definitions
- **Performance Impact**: Customer optimization scenarios
- **RCA Priority**: **MEDIUM** - Customer-specific optimizations

#### **CustomerRatePool**
- **Purpose**: Customer rate pool configurations for pooled optimizations
- **Performance Impact**: Pooling logic and assignments
- **RCA Priority**: **MEDIUM** - Pooling optimization changes

### 5. Supporting Tables

#### **OptimizationInstanceResultFile**
- **Purpose**: Stores Excel result files as binary data
- **Key Columns**: `InstanceId`, `AssignmentXlsxBytes`, `CreatedDate`
- **Performance Impact**: Large binary data storage and retrieval
- **RCA Priority**: **LOW** - File generation performance

#### **BillingPeriod**
- **Purpose**: Billing period definitions and date ranges
- **Performance Impact**: Referenced for billing validation
- **RCA Priority**: **LOW** - Configuration lookup

## Key Performance Areas for Investigation

### 1. Query Performance Issues

#### **Complex JOIN Operations**
```sql
-- Example from cleanup process
FROM OptimizationDeviceResult deviceResult 
INNER JOIN Device device ON deviceResult.[AmopDeviceId] = device.[Id] 
LEFT JOIN JasperCommunicationPlan commPlan ON commPlan.[CommunicationPlanName] = device.[CommunicationPlan] 
LEFT JOIN JasperCarrierRatePlan carrierPlan ON deviceResult.[AssignedCarrierRatePlanId] = carrierPlan.[Id] 
LEFT JOIN JasperCustomerRatePlan customerPlan ON deviceResult.[AssignedCustomerRatePlanId] = customerPlan.[Id] 
LEFT JOIN CustomerRatePool customerPool ON deviceResult.[CustomerRatePoolId] = customerPool.[Id]
```
**Investigation Points**:
- Index coverage on JOIN columns
- Query execution plan changes
- Data volume growth between versions

#### **Batch Processing with Array Parameters**
```csharp
cmd.AddArrayParameters("@QueueIds", queueIds);
```
**Investigation Points**:
- Batch size configuration (`QueuesPerInstance`)
- Parameter sniffing issues
- IN clause performance with large lists

### 2. Locking and Concurrency Issues

#### **Queue Status Updates**
```sql
UPDATE OptimizationQueue WITH (HOLDLOCK) 
SET RunEndTime = GETUTCDATE(), RunStatusId = @runStatusId, TotalCost = NULL 
WHERE CommPlanGroupId = @commGroupId AND RunEndTime IS NULL
```
**Investigation Points**:
- Lock escalation and blocking
- Concurrent queue processing
- Deadlock scenarios

### 3. Caching and Memory Management

#### **Redis Cache Implementation**
```csharp
if (IsUsingRedisCache)
{
    simCards = RedisCacheHelper.GetSimCardsFromCache(context, instance.Id, commPlans, commPlanGroupId,
        () => GetSimCardsByPortalType(context, instance, queue.ServiceProviderId, billingPeriod, instance.PortalType, commPlanGroupId, commPlans, optimizationGroups));
}
```
**Investigation Points**:
- Cache hit/miss ratios
- Cache invalidation strategies
- Memory pressure and garbage collection

### 4. Data Volume and Growth

**Investigation Points**:
- Result table growth between versions
- Historical data retention policies
- Archive and cleanup processes

## Recommended Investigation Steps

### Phase 1: Database Performance Analysis

1. **Index Analysis**
   - Review index usage on result tables
   - Check for missing indexes on JOIN columns
   - Analyze index fragmentation levels

2. **Query Performance**
   - Capture execution plans for key queries
   - Identify long-running queries
   - Check for parameter sniffing issues

3. **Lock Analysis**
   - Monitor blocking and deadlocks
   - Review lock escalation events
   - Analyze wait statistics

### Phase 2: Application Performance Analysis

1. **Batch Size Optimization**
   - Test different `QueuesPerInstance` values
   - Monitor Lambda execution times
   - Analyze SQS message processing rates

2. **Cache Performance**
   - Monitor Redis cache metrics
   - Analyze cache hit ratios
   - Review cache eviction patterns

3. **Memory Analysis**
   - Monitor Lambda memory usage
   - Analyze garbage collection metrics
   - Review object allocation patterns

### Phase 3: Data Volume Analysis

1. **Table Growth Analysis**
   - Compare table sizes between versions
   - Analyze data distribution patterns
   - Review historical growth trends

2. **Cleanup Process Review**
   - Verify cleanup procedures are running
   - Check data retention policies
   - Analyze archive processes

## Monitoring and Metrics

### Key Performance Indicators (KPIs)

1. **Processing Time Metrics**
   - Average optimization completion time
   - Queue processing throughput
   - Lambda execution duration

2. **Database Metrics**
   - Query execution times
   - Lock wait times
   - Index seek vs scan ratios

3. **Cache Metrics**
   - Cache hit/miss ratios
   - Cache response times
   - Memory utilization

### Alerting Thresholds

- Queue processing time > 5 minutes
- Database query time > 30 seconds
- Cache miss ratio > 20%
- Lock wait time > 10 seconds

## Next Steps and Recommendations

### Immediate Actions

1. **Performance Monitoring**
   - Implement comprehensive monitoring for all identified tables
   - Set up alerting for performance thresholds
   - Create performance dashboards

2. **Database Optimization**
   - Review and optimize indexes on result tables
   - Analyze query execution plans
   - Consider table partitioning for large result tables

3. **Configuration Tuning**
   - Optimize batch sizes (`QueuesPerInstance`)
   - Review timeout settings
   - Tune cache configurations

### Long-term Improvements

1. **Architecture Review**
   - Consider result table archiving strategies
   - Evaluate query optimization opportunities
   - Review caching strategies

2. **Code Optimization**
   - Optimize JOIN operations
   - Implement query result caching
   - Review batch processing logic

## Conclusion

The performance degradation between versions 1.0 and 2.0 likely stems from changes in:
- Database query patterns and JOIN complexity
- Data volume growth in result tables
- Caching implementation changes
- Batch processing configuration

Focus investigation efforts on the **HIGH priority** tables identified in this analysis, particularly the optimization result tables and their relationships with device tables, as these form the core of the optimization processing pipeline.

## Appendix

### Table Relationship Diagram
```
OptimizationInstance
    ├── OptimizationQueue
    │   ├── OptimizationDeviceResult ──→ Device
    │   ├── OptimizationMobilityDeviceResult ──→ MobilityDevice
    │   ├── OptimizationSharedPoolResult ──→ Device
    │   └── OptimizationMobilitySharedPoolResult ──→ MobilityDevice
    └── OptimizationInstanceResultFile

Rate Plans & Pools
    ├── JasperCarrierRatePlan
    ├── JasperCustomerRatePlan
    └── CustomerRatePool
```

### Performance Testing Queries

**Monitor Result Table Growth:**
```sql
SELECT 
    OBJECT_NAME(OBJECT_ID) AS TableName,
    rows AS RowCount,
    (SUM(reserved) * 8) / 1024 AS SizeMB
FROM sys.dm_db_partition_stats 
WHERE OBJECT_NAME(OBJECT_ID) LIKE '%Optimization%Result%'
GROUP BY OBJECT_ID
ORDER BY SizeMB DESC;
```

**Monitor Queue Processing Performance:**
```sql
SELECT 
    AVG(DATEDIFF(second, RunStartTime, RunEndTime)) AS AvgProcessingSeconds,
    COUNT(*) AS QueueCount,
    RunStatusId
FROM OptimizationQueue 
WHERE RunEndTime > DATEADD(day, -7, GETDATE())
GROUP BY RunStatusId;
```