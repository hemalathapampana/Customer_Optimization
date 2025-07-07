# Customer Optimization System - Complete Documentation

## Executive Summary

The Customer Optimization System is an intelligent AWS Lambda-based pipeline that automatically reduces SIM card operational costs for business customers. The system analyzes how customers use their devices, finds the most cost-effective rate plans, and automatically assigns them to minimize monthly bills. It's specifically designed for M2M (Machine-to-Machine) customers who have multiple connected devices.

**Key Benefits:**
- Automatic cost reduction for customers with multiple devices
- Real-time rate plan optimization based on actual usage patterns
- Support for both individual device optimization and pooled rate plans
- Comprehensive reporting and audit trails

## How It Works - Simple Overview

1. **Request Initiation**: A customer optimization request is triggered (manually or automatically)
2. **Data Collection**: System gathers customer device information and available rate plans
3. **Analysis**: Algorithms analyze usage patterns and calculate optimal assignments
4. **Optimization**: System tests different rate plan combinations to find lowest costs
5. **Results**: Best assignments are compiled and reports are generated
6. **Cleanup**: System finalizes results and sends notifications

## System Architecture & Core Components

### Three Main Lambda Functions

#### 1. QueueCustomerOptimization (The Coordinator)
- **Role**: Project manager that sets up and coordinates the entire optimization process
- **What it does**: Validates customer data, discovers available rate plans, and creates optimization jobs
- **Key responsibilities**: Data validation, rate plan discovery, queue management

#### 2. SimCardCostOptimizer (The Calculator)
- **Role**: The mathematical engine that performs cost calculations and optimizations
- **What it does**: Tests different rate plan combinations and calculates costs for each scenario
- **Key responsibilities**: Cost calculations, algorithm execution, strategy comparison

#### 3. SimCardCostOptimizerCleanup (The Finalizer)
- **Role**: The coordinator that compiles results and handles post-processing
- **What it does**: Waits for all calculations to complete, compiles final results, generates reports
- **Key responsibilities**: Result compilation, report generation, email notifications

## Detailed Process Flow

### Phase 1: QueueCustomerOptimization - Setup & Coordination

#### Step 1: Customer Request Processing
```
Customer Request → Validate Customer ID → Check Portal Type → Authenticate
```
- **Input**: Customer optimization request from AMOP 2.0 system
- **Validation**: Ensures customer exists, has proper permissions, and is M2M portal type
- **Authentication**: Verifies integration credentials and customer access rights

#### Step 2: Rate Plan Discovery
```
Load Customer Data → Discover Available Rate Plans → Validate Eligibility → Group by Features
```
- **Discovery**: Finds all rate plans available to the customer
- **Filtering**: Removes plans that customer isn't eligible for
- **Grouping**: Organizes plans by features (pooling, auto-change capability, etc.)
- **Validation**: Ensures rate plans have valid pricing (overage rates > 0)

#### Step 3: Auto Change Logic Analysis
```
Check Auto Change Settings → Determine Optimization Mode → Configure Rate Pools
```
- **Auto Change Enabled**: System can dynamically change rate plans for optimal cost
- **Auto Change Disabled**: System works within existing rate plan groups (pools)
- **Rate Pool Collections**: Creates groups of compatible rate plans for testing

#### Step 4: Device Processing Strategy
```
Group Devices → Generate Rate Plan Combinations → Create Optimization Queues
```
- **Grouping Options**:
  - By Rate Pool ID (for pooled plans)
  - By Communication Plan (for M2M consistency)
  - Individual processing (for maximum flexibility)
- **Permutation Generation**: Creates all valid rate plan combinations to test
- **Queue Creation**: Breaks work into parallel processing jobs

### Phase 2: SimCardCostOptimizer - Cost Calculation & Optimization

#### Step 1: Queue Processing
```
Receive Queue Job → Load Device Data → Load Rate Plan Options → Validate Configuration
```
- **Job Retrieval**: Gets specific optimization job from queue
- **Data Loading**: Retrieves device usage history and current assignments
- **Rate Plan Loading**: Gets available rate plan options for this group

#### Step 2: Strategy Execution
The system tests multiple assignment strategies:

**Strategy A: No Grouping + Largest to Smallest**
- Processes devices individually
- Assigns highest usage devices first to most cost-effective plans
- Optimizes for maximum cost reduction

**Strategy B: No Grouping + Smallest to Largest**
- Processes devices individually  
- Assigns lowest usage devices first
- Optimizes for plan utilization efficiency

**Strategy C: Communication Plan Grouping (M2M Only)**
- Groups devices by communication plan type
- Maintains consistency within customer communication standards
- Optimizes for bulk assignments

#### Step 3: Cost Calculation Engine
```
For Each Strategy:
  For Each Device Group:
    For Each Rate Plan Sequence:
      Calculate Base Cost + Overage + Fees = Total Cost
      Compare with Current Cost
      Track Best Assignment
```

**Cost Components:**
- **Base Cost**: Monthly plan cost × (billing days / 30)
- **Overage Cost**: Excess usage × overage rate  
- **Regulatory Fees**: Carrier-specific fees
- **Taxes**: Location-based tax calculations

#### Step 4: Result Evaluation
```
Compare All Strategy Results → Select Lowest Cost → Validate Savings → Store Results
```
- **Cost Comparison**: Evaluates total cost for each strategy
- **Savings Validation**: Ensures optimization actually saves money
- **Result Storage**: Saves winning assignment details for compilation

### Phase 3: SimCardCostOptimizerCleanup - Finalization & Reporting

#### Step 1: Processing Monitoring
```
Monitor Queue Depths → Check Completion Status → Apply Backoff Logic → Validate All Jobs Done
```
- **Queue Monitoring**: Checks if all optimization jobs have completed
- **Backoff Logic**: Uses exponential delays (30s → 60s → 120s → 300s max)
- **Completion Validation**: Ensures no jobs are still processing

#### Step 2: Result Compilation
```
Identify Winning Assignments → Compile Cost Savings → Generate Statistics → Create Reports
```
- **Winner Selection**: Chooses best assignment for each device group
- **Savings Calculation**: Computes total cost reduction achieved
- **Statistics Generation**: Creates optimization performance metrics
- **Report Creation**: Builds Excel reports with detailed assignments

#### Step 3: Report Generation
**M2M Portal Reports Include:**
- Device assignment spreadsheets showing old vs new rate plans
- Cost savings summaries with before/after comparisons  
- Rate plan utilization statistics
- Optimization group details and strategy results

#### Step 4: Notification & Cleanup
```
Send Email Reports → Update Processing Status → Clean Temporary Data → Archive Results
```
- **Email Delivery**: Sends results to stakeholders with Excel attachments
- **Status Updates**: Marks optimization session as complete
- **Data Cleanup**: Removes temporary processing data
- **Result Archiving**: Stores final results for audit purposes

## Optimization Logic Deep Dive

### Rate Plan Assignment Algorithm

#### Input Data Analysis
1. **Device Usage Patterns**: Historical data usage per device
2. **Current Rate Plan Costs**: Existing monthly costs per device
3. **Available Rate Plans**: All eligible plans with pricing
4. **Constraints**: Customer-specific limitations and requirements

#### Optimization Process
```
For each possible rate plan combination:
  1. Calculate total monthly cost for all devices
  2. Account for pooling benefits (if applicable)
  3. Add regulatory fees and taxes
  4. Compare with baseline cost
  5. Track if this is the best option found
```

#### Cost Calculation Formula
```
Total Cost = (Base Plan Cost × Proration) + Overage Charges + Regulatory Fees + Taxes

Where:
- Proration = Billing Days / 30
- Overage = (Usage - Plan Allowance) × Overage Rate (if positive)
- Regulatory Fees = Plan-specific carrier fees
- Taxes = Location and plan-specific tax rates
```

### Strategy Selection Logic

#### Portal Type Determination
- **M2M Portal**: Uses device grouping and communication plan strategies
- **Other Portals**: Currently not supported (system specifically for M2M)

#### Strategy Ranking
1. **Cost Effectiveness**: Total cost reduction achieved
2. **Implementation Simplicity**: Fewer rate plan changes preferred
3. **Risk Minimization**: Proven rate plan combinations preferred
4. **Customer Requirements**: Specific customer constraints honored

## Data Flow & Database Integration

### Core Database Tables

#### OptimizationSession
- **Purpose**: Tracks optimization sessions across service providers
- **Key Data**: Session ID, customer info, start/end times, status

#### OptimizationInstance  
- **Purpose**: Individual optimization runs per service provider
- **Key Data**: Instance ID, session reference, processing state, results

#### OptimizationQueue
- **Purpose**: Manages work queues for parallel processing
- **Key Data**: Queue items, processing status, rate pool assignments

#### OptimizationCustomerProcessing
- **Purpose**: Tracks processing state and progress
- **Key Data**: Processing status, completion flags, error handling

### Data Flow Between Components

#### Session Data Flow
```
AMOP 2.0 Request → OptimizationSession → OptimizationInstance → 
Rate Pool Processing → Results Storage → Report Generation
```

#### Device Data Flow  
```
Rate Plan Discovery → Device Processing → Rate Pool Assignment → 
Cost Calculation → Result Storage
```

#### Result Data Flow
```
Optimization Results → Result Compilation → Report Generation → 
Email Delivery → Processing Cleanup
```

## System Constraints & Limitations

### Processing Limits
- **Minimum Devices**: Requires > 1 device for optimization (single device has no optimization options)
- **Rate Plan Limits**: Maximum 15 rate plans per group (prevents combinatorial explosion)
- **Auto Change Requirements**: Minimum 2 rate plans needed for auto change optimization
- **Portal Support**: M2M portal type only

### Performance Considerations
- **Queue Management**: Uses parallel processing to handle large customer bases
- **Memory Limits**: Rate plan permutations controlled to prevent memory issues
- **Processing Time**: Exponential backoff prevents resource exhaustion
- **Error Handling**: Comprehensive retry logic for failed optimizations

## Success Metrics & Reporting

### Key Performance Indicators
- **Cost Savings**: Total monthly cost reduction achieved
- **Devices Optimized**: Number of devices with improved rate plans  
- **Processing Time**: Time to complete optimization for customer
- **Success Rate**: Percentage of successful optimizations

### Report Contents
- **Before/After Comparison**: Old vs new rate plan assignments
- **Cost Analysis**: Detailed cost breakdown and savings
- **Device Details**: Individual device optimization results
- **Summary Statistics**: Overall optimization performance

## Error Handling & Recovery

### Common Error Scenarios
1. **Invalid Customer Data**: Missing or incorrect customer information
2. **Rate Plan Issues**: Unavailable or incorrectly priced rate plans
3. **Processing Failures**: Lambda timeouts or resource constraints
4. **Data Inconsistencies**: Mismatched device or rate plan data

### Recovery Mechanisms
- **Automatic Retry**: Failed jobs automatically retry with backoff
- **Error Logging**: Comprehensive logging for troubleshooting
- **Fallback Processing**: Alternative strategies when primary fails
- **Manual Override**: Ability to manually intervene in failed optimizations

This documentation provides a complete understanding of how the Customer Optimization System works, from initial request through final reporting, with clear explanations of the logic, constraints, and benefits for anyone needing to understand the system.