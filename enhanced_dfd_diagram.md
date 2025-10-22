# Enhanced Data Flow Diagram with Lambda Functions

## Visual DFD with Lambda Integration

```mermaid
flowchart TD
    A["🔴 AMOP 2.0 Trigger<br/>Customer Request"] --> |Customer Parameters| B
    
    B["🔵 Session Management<br/>Customer Validation"] --> |Customer Data| C
    
    C["🟢 Rate Plan Discovery<br/>Auto Change Detection"] --> |Rate Plan Status| D
    
    D["🟡 Customer Validation<br/>Eligibility Check"] --> |Validated Customer Data| E
    
    E["🟣 Rate Pool Generation<br/>Customer Pool Calculation"] --> |Customer Rate Pool Data| F
    
    F["🔵 Queue Creation<br/>Customer Job Queuing<br/><br/>📋 Lambda:<br/>Altaworx.SimCard.Cost.<br/>QueueCustomerOptimization"] --> |Customer Queue Items| G
    
    G["🟠 Optimization Execution<br/>Customer Algorithm Processing<br/><br/>⚙️ Lambdas:<br/>• Altaworx.SimCard.Cost.Optimizer<br/>• Altaworx.SimCard.Cost.Optimizer.Cleanup"] --> |Customer Optimization Results| H
    
    H["🔵 Result Compilation<br/>Customer Data Aggregation"] --> |Compiled Customer Results| I
    
    I["🟣 Customer Email & Reporting<br/>Customer Finalization"]

    classDef redBox fill:#ffcccc,stroke:#ff0000,stroke-width:3px,color:#000000
    classDef blueBox fill:#cce5ff,stroke:#0066cc,stroke-width:3px,color:#000000
    classDef greenBox fill:#ccffcc,stroke:#00cc00,stroke-width:3px,color:#000000
    classDef yellowBox fill:#ffffcc,stroke:#cccc00,stroke-width:3px,color:#000000
    classDef purpleBox fill:#e6ccff,stroke:#9933cc,stroke-width:3px,color:#000000
    classDef orangeBox fill:#ffe6cc,stroke:#ff6600,stroke-width:3px,color:#000000

    class A redBox
    class B,F,H blueBox
    class C greenBox
    class D yellowBox
    class E,I purpleBox
    class G orangeBox
```

## Lambda Function Details

### 🔵 Queue Creation Stage
**Lambda Function:** `Altaworx.SimCard.Cost.QueueCustomerOptimization`
- **Purpose:** Manages customer job queuing and scheduling
- **Input:** Customer Rate Pool Data
- **Output:** Customer Queue Items
- **Responsibilities:**
  - Job prioritization and scheduling
  - Queue state management
  - Customer job tracking

### 🟠 Optimization Execution Stage
**Primary Lambda:** `Altaworx.SimCard.Cost.Optimizer`
- **Purpose:** Core optimization processing engine
- **Responsibilities:**
  - Executes cost optimization algorithms
  - Processes customer-specific scenarios
  - Manages computational resources

**Cleanup Lambda:** `Altaworx.SimCard.Cost.Optimizer.Cleanup`
- **Purpose:** Post-processing cleanup operations
- **Responsibilities:**
  - Resource cleanup and management
  - Data sanitization
  - System stability maintenance

## Data Flow Sequence
1. **Customer Parameters** → Session Management
2. **Customer Data** → Rate Plan Discovery  
3. **Rate Plan Status** → Customer Validation
4. **Validated Customer Data** → Rate Pool Generation
5. **Customer Rate Pool Data** → Queue Creation (QueueCustomerOptimization λ)
6. **Customer Queue Items** → Optimization Execution (Optimizer & Cleanup λ)
7. **Customer Optimization Results** → Result Compilation
8. **Compiled Customer Results** → Email & Reporting

## Architecture Overview
- **🔴 Red:** Initial trigger point
- **🔵 Blue:** Management and compilation stages  
- **🟢 Green:** Discovery and analysis
- **🟡 Yellow:** Validation processes
- **🟣 Purple:** Generation and finalization
- **🟠 Orange:** Core processing with lambda integration