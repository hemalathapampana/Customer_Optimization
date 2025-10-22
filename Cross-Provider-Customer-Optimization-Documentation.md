# Cross-Provider Customer Optimization System - Complete Technical Documentation

## Executive Summary

The Cross-Provider Customer Optimization System is an advanced AWS Lambda-based pipeline that minimizes SIM card operational costs for customers across multiple service providers through intelligent rate plan optimization. The system processes customer device populations spanning different carriers, analyzes cross-provider usage patterns, and optimizes rate plan assignments using sophisticated algorithms that consider provider-specific constraints, pricing models, and service capabilities.

## System Architecture & Cross-Provider Flow

### Core Cross-Provider Components

1. **QueueCustomerOptimization** - Orchestrates cross-provider customer optimization processes
2. **SimCardCostOptimizer** - Executes multi-provider optimization algorithms  
3. **SimCardCostOptimizerCleanup** - Finalizes cross-provider results and cleanup

### Cross-Provider Data Flow Overview

```
Customer Request → Cross-Provider Session Management → Multi-Provider Customer Validation → 
Cross-Provider Rate Plan Discovery → Multi-Provider Auto Change Logic → 
Cross-Provider Rate Pool Generation → Provider-Specific Queue Creation → 
Multi-Provider Optimization Execution → Cross-Provider Result Compilation → 
Consolidated Customer Email & Reporting → Cross-Provider Cleanup
```

### Cross-Provider Architecture Characteristics

- **Multi-Provider Processing**: Simultaneous optimization across multiple service providers
- **Provider-Agnostic Logic**: Unified optimization algorithms that adapt to provider-specific constraints
- **Cross-Provider Cost Comparison**: Real-time cost analysis between different providers
- **Consolidated Reporting**: Unified reporting across all providers for customer visibility
- **Provider-Specific Validation**: Separate validation logic for each provider's requirements

## 1. QueueCustomerOptimization Lambda - Cross-Provider Orchestrator

### Purpose
Primary orchestrator that initiates and manages cross-provider customer optimization processes, coordinating optimization across multiple service providers simultaneously while maintaining provider-specific business rules and constraints.

### Cross-Provider Execution Triggers

- **AMOP 2.0 Integration**: Cross-provider customer optimization requests via SQS messages
- **Multi-Provider Portal Support**: M2M Portal with cross-provider capabilities
- **Provider-Agnostic Customer Processing**: Revenue (Rev) customers and AMOP customers across all providers

### Core Cross-Provider Logic Flow

#### 1.1 Cross-Provider Customer Validation & Processing

**Flow**: Customer Request Processing → Multi-Provider Customer Type Detection → Cross-Provider Authentication Validation → Multi-Provider Customer Rate Plan Discovery

**Cross-Provider Validation Rules**:
- Validates Customer ID across all supported providers
- Ensures cross-provider billing period alignment
- Verifies integration authentication credentials for each provider
- Validates service provider associations and capabilities
- Checks cross-provider eligibility and restrictions

**Cross-Provider Customer Processing Logic**:
- **Rev Customers**: GUID-based identification with provider-specific integration authentication
- **AMOP Customers**: Integer-based identification with unified cross-provider authentication
- **Multi-Provider Processing**: Simultaneous processing across Verizon, AT&T, T-Mobile, and other supported providers
- **Provider-Specific Constraints**: Applies individual provider business rules and limitations

#### 1.2 Cross-Provider Rate Plan Discovery & Validation

**Flow**: Load Multi-Provider Customer Rate Plans → Cross-Provider Auto Change Eligibility → Provider-Specific Bill in Advance → Cross-Provider Rate Pool Grouping

**Cross-Provider Rate Plan Processing**:
- Retrieves customer rate plans from all associated providers
- Filters rate plans by cross-provider customer eligibility
- Groups rate plans by provider-specific Auto Change capabilities
- Validates cross-provider rate plan compatibility and overage rates
- Handles provider-specific rate plan structures and limitations

**Cross-Provider Auto Change Logic**:
- **Provider-Specific Auto Change**: Enables optimization within individual providers
- **Cross-Provider Auto Change**: Allows optimization across different providers
- **Provider Rate Pool Collections**: Creates provider-specific rate plan groupings
- **Cross-Provider Compatibility**: Ensures rate plans work across provider boundaries

#### 1.3 Cross-Provider Bill in Advance Processing

**Flow**: Multi-Provider BIA Eligibility → Load Cross-Provider Next Billing Period → Provider-Specific Charge Type Configuration → Cross-Provider Optimization Mode

**Cross-Provider Bill in Advance Features**:
- Identifies rate plans eligible for BIA across all providers
- Loads next billing period for cross-provider advance billing calculations
- Sets provider-specific charge types for advance billing scenarios
- Coordinates BIA processing across multiple providers simultaneously
- Handles provider-specific BIA requirements and limitations

#### 1.4 Cross-Provider Device Processing

**Flow**: Group by Cross-Provider Rate Pool ID → Process Multi-Provider Auto Change Groups → Generate Cross-Provider Permutations → Create Provider-Specific Queues

**Cross-Provider Processing Strategies**:

1. **Cross-Provider Rate Pool Processing**: Groups devices by provider-agnostic rate pool ID for unified optimization
2. **Provider-Specific Auto Change Processing**: Groups devices by provider and rate plan code for targeted optimization
3. **Cross-Provider Permutation Generation**: Creates valid rate plan combinations across providers
4. **Multi-Provider Queue Creation**: Generates optimization queues for parallel cross-provider processing

**Cross-Provider Rate Plan Permutation Logic**:
- Generates sequences of compatible rate plans across providers
- Limits permutations to prevent cross-provider combinatorial explosion
- Orders sequences by cross-provider cost optimization potential
- Filters out invalid cross-provider rate plan combinations
- Considers provider-specific constraints and capabilities

### Cross-Provider Queue Generation & Management

#### Cross-Provider Rate Pool Sequence Generation

**GenerateRatePoolSequences()**: Creates optimized sequences of rate plans for cross-provider customer assignment testing.

**Cross-Provider Process Flow**:
```
Input Multi-Provider Customer Rate Plans → Filter Cross-Provider Compatible Plans → 
Generate Cross-Provider Permutations → Apply Provider-Specific Customer Logic → 
Rank by Cross-Provider Cost Potential → Return Provider-Optimized Sequences
```

**Cross-Provider Key Operations**:
1. **Multi-Provider Compatibility**: Ensures rate plans work across customer billing cycles and providers
2. **Cross-Provider Cost Ranking**: Orders plans by total cross-provider cost-effectiveness
3. **Provider-Specific Auto Change Logic**: Applies individual provider rate plan change rules
4. **Cross-Provider Optimization Priority**: Sequences with highest total savings potential across all providers

**Cross-Provider Sequence Characteristics**:
- **Ordering**: Multi-provider cost optimization (lowest total cost across providers)
- **Filtering**: Eliminates incompatible cross-provider rate plan combinations
- **Limits**: Controlled by RATE_PLAN_SEQUENCES_FIRST_INSTANCE_LIMIT per provider
- **Batching**: Sequences split into provider-specific manageable batches

### Cross-Provider Constraints

- **Minimum Device Limit**: Requires > 1 device per provider for cross-provider optimization
- **Cross-Provider Rate Plan Limits**: Maximum 15 rate plans per provider per rate plan group
- **Multi-Provider Auto Change Requirements**: Minimum 2 rate plans per provider for optimization
- **Cross-Provider Rate Pool**: Uses customer-specific rate pooling across multiple providers

## 2. SimCardCostOptimizer Lambda - Cross-Provider Execution Engine

### Purpose
Executes sophisticated cross-provider optimization algorithms to find the best rate plan assignments for customer devices across multiple service providers, considering provider-specific pricing, capabilities, and constraints.

### Cross-Provider Execution Flow

#### 2.1 Cross-Provider Customer Processing

**Flow**: Receive Multi-Provider Customer Queues → Load Cross-Provider Customer Instance → Validate Multi-Provider Customer Data → Process Cross-Provider Customer Optimization

**Cross-Provider Customer Processing Logic**:
- Loads customer optimization instances across all providers
- Processes cross-provider customer rate pools instead of single-provider plans
- Applies provider-specific optimization settings and constraints
- Uses cross-provider customer rate plan filtering and validation logic

#### 2.2 Cross-Provider Rate Pool Processing

**Flow**: Load Multi-Provider Customer Rate Pools → Filter by Cross-Provider Customer Eligibility → Generate Cross-Provider Customer Collections → Execute Multi-Provider Customer Algorithms

**Cross-Provider Rate Pool Features**:
- **Cross-Provider Rate Pool ID**: Links devices to rate pools spanning multiple providers
- **Multi-Provider Filtering**: Filters devices by provider-specific customer rate plan codes
- **Cross-Provider Pooled Usage**: Allows usage sharing across customer devices and providers
- **Multi-Provider Optimization Groups**: Uses cross-provider grouping logic and constraints

#### 2.3 Cross-Provider Assignment Strategies

The system executes comprehensive cross-provider assignment strategies:

**Strategy 1: Cross-Provider No Grouping + Largest to Smallest**
- Processes customer devices individually across all providers
- Assigns highest usage customer devices first regardless of provider
- Optimizes for maximum cross-provider cost reduction
- Considers provider-specific capabilities for high-usage devices

**Strategy 2: Cross-Provider No Grouping + Smallest to Largest**
- Processes customer devices individually across all providers
- Assigns lowest usage customer devices first across providers
- Optimizes for cross-provider plan utilization efficiency
- Balances provider-specific plan optimization

**Strategy 3: Cross-Provider Communication Plan Grouping (M2M)**
- Groups customer devices by communication plan across providers
- Maintains customer plan consistency while optimizing across providers
- Optimizes for cross-provider bulk assignments and volume discounts

**Strategy 4: Provider-Specific Optimization with Cross-Provider Comparison**
- Optimizes within each provider individually
- Compares results across providers for best overall assignment
- Considers provider migration costs and benefits

#### 2.4 Cross-Provider Cost Calculation Engine

**Flow**: Calculate Multi-Provider Base Cost → Apply Cross-Provider Proration → Calculate Multi-Provider Overage → Add Provider-Specific Fees → Generate Cross-Provider Total

**Cross-Provider Cost Components**:
- **Multi-Provider Base Cost**: Monthly plan cost across providers × (billing days / 30)
- **Cross-Provider Overage**: Excess usage × provider-specific overage rates
- **Provider-Specific Regulatory Fees**: Individual carrier fees and regulations
- **Cross-Provider Taxes**: Location and provider-based tax calculations
- **Provider Migration Costs**: Costs associated with changing providers

#### 2.5 Cross-Provider Result Evaluation

**Flow**: Compare Cross-Provider Strategy Results → Select Best Multi-Provider Assignment → Validate Cross-Provider Savings → Record Provider-Specific Details

**Cross-Provider Evaluation Criteria**:
- Total cost across all providers
- Provider-specific performance metrics
- Cross-provider migration feasibility
- Service quality and coverage considerations
- Long-term cost projections across providers

## 3. SimCardCostOptimizerCleanup Lambda - Cross-Provider Finalization

### Purpose
Finalizes cross-provider optimization results, generates comprehensive multi-provider reports, and handles cross-provider post-optimization tasks including provider coordination and customer communication.

### Cross-Provider Execution Flow

#### 3.1 Cross-Provider Queue Monitoring

**Flow**: Check Multi-Provider Customer Queue Depths → Monitor Cross-Provider Processing → Apply Provider-Specific Customer Backoff → Validate Cross-Provider Completion

**Cross-Provider Monitoring Logic**:
- Polls customer optimization queues across all providers for completion
- Uses provider-specific exponential backoff (30s → 60s → 120s → max 300s)
- Tracks cross-provider queue depths and processing status
- Handles provider-specific retry logic and failure scenarios
- Coordinates completion across multiple provider processing streams

#### 3.2 Cross-Provider Result Compilation

**Flow**: Identify Multi-Provider Winning Queues → Compile Cross-Provider Results → Generate Multi-Provider Statistics → Create Cross-Provider Reports

**Cross-Provider Compilation Process**:
- Selects winning assignments for each customer rate pool group across providers
- Compiles cross-provider cost savings and optimization statistics
- Generates comprehensive multi-provider Excel reports with device assignments
- Creates cross-provider optimization summaries and comparisons
- Handles provider-specific result formatting and data structures

#### 3.3 Cross-Provider Report Generation

**Multi-Provider Customer Reports**:
- **Cross-Provider Device Assignment Spreadsheets**: Detailed assignments across all providers
- **Multi-Provider Cost Savings Summaries**: Consolidated savings across providers
- **Provider-Specific Rate Plan Utilization Statistics**: Individual provider performance
- **Cross-Provider Optimization Group Details**: Multi-provider grouping and assignments
- **Provider Migration Recommendations**: Suggestions for optimal provider distribution

**Report Features**:
- Provider-specific tabs in Excel reports
- Cross-provider comparison charts and graphs
- Migration cost analysis and recommendations
- Historical cross-provider performance tracking

#### 3.4 Cross-Provider Post-Optimization Tasks

**Cross-Provider Email Notifications**:
- Sends consolidated optimization results across all providers
- Includes provider-specific Excel attachments and summaries
- Provides cross-provider cost savings summaries and comparisons
- Uses cross-provider email templates with provider-specific sections

**Cross-Provider Processing Tracking**:
- Updates OptimizationCustomerProcessing table with multi-provider status
- Tracks customer optimization session progress across all providers
- Manages cross-provider processing state and coordination
- Handles provider-specific cleanup logic and requirements

**Cross-Provider Session Management**:
- Waits for all provider instances to complete before final email
- Coordinates customer optimization across multiple service providers
- Manages cross-provider processing delays and retry logic
- Sends consolidated multi-provider optimization results
- Handles provider-specific completion criteria and validation

## Cross-Provider Assignment Strategy Implementation

### Cross-Provider Strategy Selection Logic

**Flow**: Portal Type → Customer Type → Multi-Provider Customer Settings → Cross-Provider Strategy List

**M2M Cross-Provider Portal**:
- Cross-Provider No Grouping + Largest/Smallest across providers
- Cross-Provider Group by Communication Plan + Largest/Smallest across providers
- Provider-Specific Optimization with Cross-Provider Comparison
- Multi-Provider Migration Strategy with cost-benefit analysis

### Cross-Provider Strategy Execution Flow

```
Load Multi-Provider Customer Configuration → Prepare Cross-Provider Device Groups → 
Execute Multi-Provider Assignment Algorithm → Calculate Cross-Provider Costs → 
Evaluate Cross-Provider Results → Select Best Multi-Provider Strategy
```

### Cross-Provider Cost Optimization Logic

```
For each cross-provider strategy:
  For each provider:
    For each customer device group:
      For each provider rate plan sequence:
        Calculate cross-provider assignment cost
        Compare with cross-provider baseline
        Track best cross-provider assignment
        Consider provider migration costs
  Select lowest-cost cross-provider assignment
  Evaluate provider distribution optimization
Select best cross-provider strategy result
```

## Cross-Provider Database Tables and Data Flow

### Cross-Provider Core Tables

**OptimizationSession**: Tracks cross-provider optimization sessions across all service providers
**OptimizationInstance**: Represents individual optimization runs per service provider with cross-provider coordination
**OptimizationQueue**: Manages multi-provider optimization work queues with cross-provider rate pools
**OptimizationCustomerProcessing**: Tracks cross-provider processing state and progress across providers
**CrossProviderMigration**: Tracks potential and actual provider migrations for customers
**ProviderPerformance**: Stores historical performance data for cross-provider comparisons

### Cross-Provider Data Flow Between Components

**Cross-Provider Session Data Flow**:
```
AMOP 2.0 Multi-Provider Customer Request → OptimizationSession → 
Multi-Provider OptimizationInstance → Cross-Provider Rate Pool Processing → 
Multi-Provider Results Storage → Cross-Provider Report Generation
```

**Cross-Provider Device Data Flow**:
```
Multi-Provider Customer Rate Plans → Cross-Provider Device Processing → 
Multi-Provider Rate Pool Assignment → Cross-Provider Cost Calculation → 
Multi-Provider Result Storage → Provider Migration Analysis
```

**Cross-Provider Result Data Flow**:
```
Multi-Provider Optimization Results → Cross-Provider Result Compilation → 
Multi-Provider Report Generation → Cross-Provider Email Delivery → 
Multi-Provider Processing Cleanup → Provider Coordination Finalization
```

## Cross-Provider Specific Features

### Provider Migration Management
- **Migration Cost Analysis**: Calculates costs of moving devices between providers
- **Service Impact Assessment**: Evaluates coverage and performance implications
- **Migration Scheduling**: Plans optimal timing for provider changes
- **Rollback Capabilities**: Provides mechanisms to reverse provider migrations

### Cross-Provider Performance Monitoring
- **Real-Time Provider Comparison**: Continuous monitoring of provider performance
- **Cost Trend Analysis**: Historical cost analysis across providers
- **Service Quality Metrics**: Coverage, speed, and reliability tracking per provider
- **Optimization Effectiveness**: Measures success of cross-provider optimizations

### Provider-Specific Compliance
- **Regulatory Compliance**: Ensures adherence to provider-specific regulations
- **Contract Compliance**: Validates against individual provider contracts
- **Data Privacy**: Maintains provider-specific data handling requirements
- **Audit Trails**: Comprehensive logging for cross-provider optimization decisions

## Conclusion

This Cross-Provider Customer Optimization System provides a comprehensive solution for minimizing SIM card operational costs across multiple service providers. The system's sophisticated architecture enables simultaneous optimization across providers while maintaining provider-specific business rules, compliance requirements, and service quality standards. The three-Lambda architecture ensures scalable, reliable, and efficient processing of cross-provider optimization scenarios, delivering significant cost savings and operational improvements for customers with multi-provider device portfolios.