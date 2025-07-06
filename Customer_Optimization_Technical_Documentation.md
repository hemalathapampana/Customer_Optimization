# Customer Optimization System - Complete Technical Documentation

## Executive Summary
The Customer Optimization System is a sophisticated AWS Lambda-based pipeline that minimizes SIM card operational costs for individual customers through intelligent rate plan optimization. The system processes customer device populations, analyzes usage patterns, and optimizes rate plan assignments using advanced algorithms across three main Lambda functions, with support for both M2M and Cross-Provider optimization scenarios.

## System Architecture & Flow

### Core Components
1. **QueueCustomerOptimization** - Orchestrates the entire customer optimization process
2. **SimCardCostOptimizer** - Executes customer-specific optimization algorithms  
3. **SimCardCostOptimizerCleanup** - Finalizes results and handles customer-specific cleanup

### Data Flow Overview
Customer Request → Session Management → Customer Validation → Rate Plan Discovery → 
Auto Change Logic → Rate Pool Generation → Queue Creation → Optimization Execution → 
Result Compilation → Customer Email & Reporting → Cleanup

## DATA FLOW DIAGRAM
```
AMOP 2.0 Request
    ↓
QueueCustomerOptimization
    ├── Customer Validation
    ├── Rate Plan Discovery
    ├── Auto Change Logic
    └── Queue Generation
    ↓
SimCardCostOptimizer
    ├── Customer Rate Pool Processing
    ├── Cost Calculation Engine
    └── Assignment Strategies
    ↓
SimCardCostOptimizerCleanup
    ├── Result Compilation
    ├── Customer Email Generation
    └── Processing Cleanup
```

## 1. QueueCustomerOptimization Lambda

### Purpose
Primary orchestrator that initiates and manages customer-specific optimization processes with support for both M2M and Cross-Provider scenarios.

### Execution Triggers
- **AMOP 2.0 Integration**: Direct customer optimization requests via SQS messages
- **Portal Type Support**: M2M Portal and Cross-Provider optimizations
- **Customer Type Processing**: Revenue (Rev) customers and AMOP customers

### Core Logic Flow

#### 1.1 Customer Validation & Processing
**Customer Request Processing → Customer Type Detection → Authentication Validation → Customer Rate Plan Discovery**

Key Validation Rules:
- Validates Customer ID or AMOP Customer ID presence
- Ensures billing period information is available
- Verifies integration authentication credentials
- Validates service provider associations

Customer Processing Logic:
- **Rev Customers**: Uses GUID-based customer identification with integration authentication
- **AMOP Customers**: Uses integer-based customer identification with simplified authentication
- **Cross-Provider**: Processes customers across multiple service providers simultaneously

#### 1.2 Rate Plan Discovery & Validation
**Load Customer Rate Plans → Validate Auto Change Eligibility → Check Bill in Advance → Group by Rate Pool**

Rate Plan Processing:
- Retrieves customer-specific rate plans from billing period
- Filters rate plans by customer eligibility and service provider
- Groups rate plans by Auto Change Rate Plan capability
- Validates rate plan overage rates and data charges (must be > 0)

Auto Change Logic:
- **Auto Change Enabled**: Allows optimization algorithm to change rate plans dynamically
- **Auto Change Disabled**: Uses customer rate pools for fixed rate plan groupings
- **Rate Pool Collections**: Creates collections of compatible rate plans for optimization

#### 1.3 Bill in Advance Processing
**Check BIA Eligibility → Load Next Billing Period → Configure Charge Type → Set Optimization Mode**

Bill in Advance Features:
- Identifies rate plans eligible for Bill in Advance processing
- Loads next billing period for advance billing calculations
- Sets charge type to OverageOnly for advance billing scenarios
- Currently disabled pending new logic implementation (PORT-166)

#### 1.4 Device Processing by Customer Rate Plans
**Group by Rate Pool ID → Process Auto Change Groups → Generate Permutations → Create Queues**

Processing Strategies:
1. **Customer Rate Pool Processing**: Groups devices by customer rate pool ID for pooled optimization
2. **Auto Change Processing**: Groups devices by rate plan code for dynamic rate plan changes
3. **Permutation Generation**: Creates all valid rate plan combinations for testing
4. **Queue Creation**: Generates optimization queues for parallel processing

Rate Plan Permutation Logic:
- Generates sequences of compatible rate plans for testing
- Limits permutations to prevent combinatorial explosion (max 15 rate plans per group)
- Orders sequences by cost optimization potential
- Filters out invalid rate plan combinations

#### 1.5 Cross-Provider Customer Optimization
**Multi-Provider Discovery → Service Provider Filtering → Cross-Provider Rate Plans → Unified Processing**

Cross-Provider Features:
- Processes customers across multiple service providers simultaneously
- Filters rate plans by service provider compatibility
- Uses unified billing periods across providers
- Generates cross-provider optimization queues

Cross-Provider Logic:
```
Load Customer → Get Service Provider IDs → Filter Rate Plans → 
Process Devices → Generate Cross-Provider Queues → Execute Optimization
```

### Queue Generation & Management

#### Rate Pool Sequence Generation
**GenerateRatePoolSequences()**: Creates optimized sequences of rate plans for customer assignment testing.

Process Flow:
```
Input Customer Rate Plans → Filter Compatible Plans → Generate Permutations → 
Apply Customer Logic → Rank by Cost Potential → Return Sequences
```

Key Operations:
1. **Customer Compatibility**: Ensures rate plans work with customer billing cycles
2. **Cost Ranking**: Orders plans by customer cost-effectiveness
3. **Auto Change Logic**: Applies customer-specific rate plan change rules
4. **Optimization Priority**: Sequences with highest customer savings potential first

Sequence Characteristics:
- **Ordering**: Customer-focused cost optimization (lowest customer cost first)
- **Filtering**: Eliminates incompatible customer rate plan combinations
- **Limits**: Controlled by RATE_PLAN_SEQUENCES_FIRST_INSTANCE_LIMIT
- **Batching**: Sequences split into manageable batches for processing

#### Customer-Specific Constraints
- **Minimum Device Limit**: Requires > 1 device for optimization algorithm execution
- **Rate Plan Limits**: Maximum 15 rate plans per customer rate plan group
- **Auto Change Requirements**: Minimum 2 rate plans for auto change optimization
- **Customer Rate Pool**: Uses customer-specific rate pooling when available

## 2. SimCardCostOptimizer Lambda

### Purpose
Executes customer-specific optimization algorithms to find the best rate plan assignments for customer devices.

### Execution Flow

#### 2.1 Customer-Specific Processing
**Receive Customer Queues → Load Customer Instance → Validate Customer Data → Process Customer Optimization**

Customer Processing Logic:
- Loads customer-specific optimization instances
- Processes customer rate pools instead of communication plans
- Applies customer-specific optimization settings
- Uses customer rate plan filtering logic

#### 2.2 Customer Rate Pool Processing
**Load Customer Rate Pools → Filter by Customer Eligibility → Generate Customer Collections → Execute Customer Algorithms**

Customer Rate Pool Features:
- **Customer Rate Pool ID**: Links devices to specific customer rate pools
- **Customer Filtering**: Filters devices by customer rate plan codes
- **Pooled Usage**: Allows usage sharing across customer devices in same pool
- **Customer Optimization Groups**: Uses customer-specific grouping logic

#### 2.3 Customer Assignment Strategies
The system executes customer-focused assignment strategies:

**Strategy 1: Customer No Grouping + Largest to Smallest**
- Processes customer devices individually
- Assigns highest usage customer devices first
- Optimizes for maximum customer cost reduction

**Strategy 2: Customer No Grouping + Smallest to Largest**  
- Processes customer devices individually
- Assigns lowest usage customer devices first
- Optimizes for customer plan utilization

**Strategy 3: Customer Communication Plan Grouping (M2M only)**
- Groups customer devices by communication plan
- Maintains customer plan consistency
- Optimizes for customer bulk assignments

#### 2.4 Customer Cost Calculation Engine
**Calculate Customer Base Cost → Apply Customer Proration → Calculate Customer Overage → Add Customer Fees → Generate Customer Total**

Customer Cost Components:
- **Customer Base Cost**: Monthly plan cost × (customer billing days / 30)
- **Customer Overage**: Excess customer usage × customer overage rate
- **Customer Regulatory Fees**: Customer-specific carrier fees
- **Customer Taxes**: Customer location-based tax calculations

#### 2.5 Customer Result Evaluation
**Compare Customer Strategy Results → Select Best Customer Assignment → Validate Customer Savings → Record Customer Details**

## 3. SimCardCostOptimizerCleanup Lambda

### Purpose
Finalizes customer optimization results, generates customer-specific reports, and handles customer post-optimization tasks.

### Execution Flow

#### 3.1 Customer Queue Monitoring
**Check Customer Queue Depths → Monitor Customer Processing → Apply Customer Backoff → Validate Customer Completion**

Customer Monitoring Logic:
- Polls customer optimization queues for completion
- Uses exponential backoff for customer processing (30s → 60s → 120s → max 300s)
- Tracks customer queue depths and processing status
- Handles customer-specific retry logic

#### 3.2 Customer Result Compilation
**Identify Customer Winning Queues → Compile Customer Results → Generate Customer Statistics → Create Customer Reports**

Customer Compilation Process:
- Selects winning customer assignment for each customer rate pool group
- Compiles customer cost savings and optimization statistics
- Generates customer-specific Excel reports with device assignments
- Creates customer optimization summaries

#### 3.3 Customer Report Generation
**M2M Customer Reports:**
- Customer device assignment spreadsheets
- Customer cost savings summaries
- Customer rate plan utilization statistics
- Customer optimization group details

**Cross-Provider Customer Reports:**
- Customer cross-provider optimization summaries
- Customer device assignment by service provider
- Customer cost analysis across carriers
- Customer unified billing reports

#### 3.4 Customer Post-Optimization Tasks

**Customer Email Notifications:**
- Sends customer optimization results to stakeholders
- Includes customer-specific Excel attachments
- Provides customer cost savings summaries
- Uses customer-specific email templates and addresses

**Customer Processing Tracking:**
- Updates OptimizationCustomerProcessing table
- Tracks customer optimization session progress
- Manages customer processing state across service providers
- Handles customer-specific cleanup logic

**Customer Session Management:**
- Waits for all customer instances to complete before final email
- Coordinates customer optimization across multiple service providers
- Manages customer processing delays and retry logic
- Sends consolidated customer optimization results

## Customer Assignment Strategy Implementation

### Customer Strategy Selection Logic
**Portal Type → Customer Type → Customer Settings → Customer Strategy List**

**M2M Customer Portal:**
- Customer No Grouping + Largest/Smallest
- Customer Group by Communication Plan + Largest/Smallest

**Cross-Provider Customer Portal:**
- Customer No Grouping only (due to cross-provider complexity)

### Customer Strategy Execution Flow
```
Load Customer Configuration → Prepare Customer Device Groups → 
Execute Customer Assignment Algorithm → Calculate Customer Costs → 
Evaluate Customer Results → Select Best Customer Strategy
```

### Customer Cost Optimization Logic
```
For each customer strategy:
  For each customer device group:
    For each customer rate plan sequence:
      Calculate customer assignment cost
      Compare with customer baseline
      Track best customer assignment
  Select lowest-cost customer assignment
Select best customer strategy result
```

## Customer Database Tables and Data Flow

### Customer Core Tables
**OptimizationSession**: Tracks customer optimization sessions across service providers
**OptimizationInstance**: Represents individual customer optimization runs per service provider  
**OptimizationQueue**: Manages customer optimization work queues with customer rate pools
**OptimizationCustomerProcessing**: Tracks customer processing state and progress

### Customer Data Flow Between Components

**Customer Session Data Flow:**
```
AMOP 2.0 Customer Request → OptimizationSession → OptimizationInstance → 
Customer Rate Pool Processing → Customer Results Storage → Customer Report Generation
```

**Customer Device Data Flow:**
```
Customer Rate Plans → Customer Device Processing → Customer Rate Pool Assignment → 
Customer Cost Calculation → Customer Result Storage
```

**Customer Result Data Flow:**
```
Customer Optimization Results → Customer Result Compilation → Customer Report Generation → 
Customer Email Delivery → Customer Processing Cleanup
```

### Customer-Specific Features

#### Auto Change Rate Plan Logic
- **Customer Rate Plan Discovery**: Identifies customer-eligible rate plans for auto change
- **Customer Permutation Generation**: Creates customer rate plan sequences for testing
- **Customer Cost Comparison**: Compares customer costs across rate plan options
- **Customer Assignment Selection**: Selects optimal customer rate plan assignments

#### Customer Rate Pool Processing  
- **Customer Rate Pool Grouping**: Groups customer devices by rate pool assignments
- **Customer Pooled Usage**: Allows usage sharing across customer devices in same pool
- **Customer Pool Optimization**: Optimizes customer costs within rate pool constraints
- **Customer Pool Reporting**: Reports customer savings by rate pool

#### Customer Bill in Advance
- **Customer BIA Eligibility**: Checks customer rate plans for bill in advance capability
- **Customer BIA Calculation**: Calculates customer advance billing charges (not yet implemented)
- **Customer BIA Logic**: Reserved for future customer bill in advance features
- **Customer BIA Reporting**: Planned customer advance billing reports

#### Cross-Provider Customer Support
- **Multi-Provider Processing**: Handles customer optimization across multiple carriers
- **Cross-Provider Rate Plans**: Uses rate plans compatible across multiple service providers
- **Cross-Provider Billing**: Manages customer billing across different provider billing cycles
- **Cross-Provider Reporting**: Generates unified customer reports across providers

This documentation provides a comprehensive technical overview of the Customer Optimization process, covering all aspects from customer request initialization through customer-specific cleanup, with detailed explanations of customer rate pool processing, customer assignment strategies, and customer-focused system architecture.