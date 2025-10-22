# Enhanced Data Flow Diagram (DFD) - Customer Optimization Process

## Overview
This enhanced DFD incorporates specific lambda function names into the customer optimization workflow, providing a more detailed technical view of the system architecture.

## Process Flow with Lambda Integration

### 1. **AMOP 2.0 Trigger - Customer Request** 
- **Stage**: Initial Trigger
- **Input**: Customer Parameters
- **Description**: Entry point for customer optimization requests
- **Color Code**: Red

---

### 2. **Session Management - Customer Validation**
- **Stage**: Authentication & Session
- **Input**: Customer Parameters
- **Output**: Customer Data
- **Description**: Validates customer credentials and manages session state
- **Color Code**: Blue

---

### 3. **Rate Plan Discovery - Auto Change Detection**
- **Stage**: Discovery & Analysis
- **Input**: Customer Data
- **Output**: Rate Plan Status
- **Description**: Analyzes current rate plans and detects potential optimization opportunities
- **Color Code**: Green

---

### 4. **Customer Validation - Eligibility Check**
- **Stage**: Validation
- **Input**: Rate Plan Status
- **Output**: Validated Customer Data
- **Description**: Performs eligibility verification for optimization processes
- **Color Code**: Yellow

---

### 5. **Rate Pool Generation - Customer Pool Calculation**
- **Stage**: Pool Management
- **Input**: Validated Customer Data
- **Output**: Customer Rate Pool Data
- **Description**: Generates and calculates customer-specific rate pools
- **Color Code**: Purple

---

### 6. **Queue Creation - Customer Job Queuing**
- **Stage**: Queue Management
- **Lambda Function**: **`Altaworx.SimCard.Cost.QueueCustomerOptimization`**
- **Input**: Customer Rate Pool Data
- **Output**: Customer Queue Items
- **Description**: Creates optimization jobs and manages customer queuing system
- **Technical Details**: 
  - Handles job scheduling and prioritization
  - Manages queue state and customer job tracking
  - Integrates with optimization pipeline
- **Color Code**: Light Blue

---

### 7. **Optimization Execution - Customer Algorithm Processing**
- **Stage**: Core Processing
- **Lambda Functions**: 
  - **`Altaworx.SimCard.Cost.Optimizer`** (Primary optimization engine)
  - **`Altaworx.SimCard.Cost.Optimizer.Cleanup`** (Post-processing cleanup)
- **Input**: Customer Queue Items
- **Output**: Customer Optimization Results
- **Description**: Executes optimization algorithms and performs cleanup operations
- **Technical Details**:
  - **Optimizer**: Runs core cost optimization algorithms
  - **Cleanup**: Handles resource cleanup and data sanitization
  - Processes customer-specific optimization scenarios
  - Manages computational resources and algorithm execution
- **Color Code**: Orange

---

### 8. **Result Compilation - Customer Data Aggregation**
- **Stage**: Data Aggregation
- **Input**: Customer Optimization Results
- **Output**: Compiled Customer Results
- **Description**: Aggregates and compiles optimization results for reporting
- **Color Code**: Blue

---

### 9. **Customer Email & Reporting - Customer Finalization**
- **Stage**: Finalization & Communication
- **Input**: Compiled Customer Results
- **Description**: Generates reports and sends customer notifications
- **Color Code**: Purple

---

## Lambda Function Mapping

| **Lambda Function** | **Stage** | **Primary Role** |
|---------------------|-----------|------------------|
| `Altaworx.SimCard.Cost.QueueCustomerOptimization` | Queue Creation | Job scheduling and queue management |
| `Altaworx.SimCard.Cost.Optimizer` | Optimization Execution | Core optimization processing |
| `Altaworx.SimCard.Cost.Optimizer.Cleanup` | Optimization Execution | Post-processing cleanup |

## Data Flow Summary

```
Customer Parameters → Customer Data → Rate Plan Status → Validated Customer Data → 
Customer Rate Pool Data → Customer Queue Items → Customer Optimization Results → 
Compiled Customer Results → Final Customer Output
```

## Technical Architecture Notes

- **Queue Management**: The `QueueCustomerOptimization` lambda ensures proper job scheduling and resource allocation
- **Optimization Engine**: The `Optimizer` lambda handles the computational heavy lifting for cost optimization
- **Cleanup Operations**: The `Optimizer.Cleanup` lambda ensures proper resource management and data cleanup
- **Scalability**: The queue-based approach allows for horizontal scaling of optimization jobs
- **Reliability**: Separate cleanup processes ensure system stability and resource management

## Integration Points

1. **Input Validation**: Customer parameters are validated through multiple stages
2. **Rate Plan Analysis**: Automated detection of optimization opportunities
3. **Queue Processing**: Asynchronous job processing for scalability
4. **Result Aggregation**: Compiled results for comprehensive reporting
5. **Customer Communication**: Automated reporting and notification system