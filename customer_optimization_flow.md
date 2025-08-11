# Customer Optimization Flow - Data Flow Diagram

## Issue Identified
**Problem**: Customer ID and ICCID were missing if there was an error. In version 2.0, an Error message was added to all customers, making it hard to identify which specific customer had an error.

```mermaid
graph TB
    A[API Request - StartConfirm] --> B{Optimization Type?}
    
    B -->|Customer| C[Create Customer Optimization Session]
    
    C --> E[Validate Permissions & Parameters]
    
    E --> G{Portal Type Detection}
    
    G -->|M2M| I[Enqueue M2M Customer Optimization]
    G -->|Mobility| J[Enqueue Mobility Customer Optimization]
    G -->|Cross-Provider| K[Enqueue Cross-Provider Customer Optimization]
    
    I --> L[M2M Customer Optimization Queue]
    J --> M[Mobility Customer Optimization Queue]
    K --> N[Cross-Provider Customer Optimization Queue]
    
    L --> P[Lambda: AltaworxSimCardCostQueueCustomerOptimization]
    M --> P
    N --> P
    
    P --> R[Start Optimization Instance]
    
    R --> T[Create Communication Plan Groups]
    
    T --> V[Enqueue Individual Queue Items]
    
    V --> X[SIM Card Cost Optimizer Queue]
    
    X --> Y[Lambda: AltaworxSimCardCostOptimizer]
    
    Y --> Z[Process Queue Records]
    Z --> AA{Processing Type?}
    
    AA -->|Initial| BB[ProcessQueues]
    AA -->|Continue| CC[ProcessQueuesContinue]
    
    BB --> DD[Load SIM Cards Data with Customer Context]
    CC --> EE[Resume from Redis Cache]
    
    DD --> FF{Portal Type?}
    FF -->|M2M| GG[GetSimCards - M2M Portal]
    FF -->|Mobility| HH[GetOptimizationMobilityDevices]
    FF -->|Cross-Provider| II[GetCrossProviderOptimizationDevices]
    
    GG --> JJ[Rate Pool Collection]
    HH --> JJ
    II --> JJ
    
    JJ --> KK[Rate Pool Calculator]
    KK --> LL[Create Rate Pools]
    LL --> MM[Rate Pool Assigner]
    
    MM --> NN{Cache Enabled?}
    NN -->|Yes| OO[Cache SIM Cards Data with Customer/ICCID Context]
    NN -->|No| PP[Direct Processing]
    
    OO --> QQ[Assignment Processing]
    PP --> QQ
    
    QQ --> RR{Processing Complete?}
    RR -->|No| SS[Save Partial State to Cache]
    RR -->|Yes| TT[Generate Optimization Results]
    
    SS --> UU[Enqueue Continue Process]
    UU --> CC
    
    TT --> VV[Record Results to Database]
    VV --> WW[Update Queue Status]
    
    %% ERROR HANDLING FLOWS - ISSUE OCCURS HERE
    DD --> ERROR1[❌ Error in Data Loading]
    GG --> ERROR2[❌ Error in M2M Data Retrieval]
    HH --> ERROR3[❌ Error in Mobility Data Retrieval]
    II --> ERROR4[❌ Error in Cross-Provider Data Retrieval]
    KK --> ERROR5[❌ Error in Rate Pool Calculation]
    QQ --> ERROR6[❌ Error in Assignment Processing]
    
    ERROR1 --> ISSUE_POINT[🚨 ISSUE: Error Logging Without Customer ID/ICCID Context]
    ERROR2 --> ISSUE_POINT
    ERROR3 --> ISSUE_POINT
    ERROR4 --> ISSUE_POINT
    ERROR5 --> ISSUE_POINT
    ERROR6 --> ISSUE_POINT
    
    ISSUE_POINT --> ERROR_LOG[Error Message Logged to All Customers]
    ERROR_LOG --> ERROR_DB[Update Database with Generic Error]
    
    ERROR_DB --> WW
    
    WW --> XX{All Queues Complete?}
    XX -->|No| YY[Continue Processing Other Queues]
    XX -->|Yes| ZZ[Mark Instance Complete]
    
    YY --> Z
    ZZ --> AAA[Trigger Cleanup Process]
    
    AAA --> BBB[Lambda: AltaworxSimCardCostOptimizerCleanup]
    BBB --> CCC[Send Email Notifications]
    CCC --> DDD[Update Final Status]
    DDD --> EEE[Clean Up Resources]
    
    EEE --> FFF[End Process]
    
    %% PROPOSED SOLUTION FLOW
    ISSUE_POINT -.-> SOLUTION[💡 PROPOSED: Enhanced Error Context Capture]
    SOLUTION -.-> ENHANCED_ERROR[Log Error with Customer ID + ICCID + Specific Error Details]
    ENHANCED_ERROR -.-> TARGETED_DB[Update Database with Customer-Specific Error Info]
    
    %% Styling
    style A fill:#e1f5fe
    style FFF fill:#c8e6c9
    style Y fill:#fff3e0
    style P fill:#fff3e0
    style BBB fill:#fff3e0
    style ISSUE_POINT fill:#ffcdd2,stroke:#d32f2f,stroke-width:3px
    style ERROR_LOG fill:#ffcdd2
    style ERROR_DB fill:#ffcdd2
    style SOLUTION fill:#c8e6c9,stroke:#388e3c,stroke-width:2px
    style ENHANCED_ERROR fill:#c8e6c9
    style TARGETED_DB fill:#c8e6c9
    
    %% Error styling
    style ERROR1 fill:#ffcdd2
    style ERROR2 fill:#ffcdd2
    style ERROR3 fill:#ffcdd2
    style ERROR4 fill:#ffcdd2
    style ERROR5 fill:#ffcdd2
    style ERROR6 fill:#ffcdd2
```

## Problem Details

### Current Issue (Version 2.0)
1. **Missing Context in Error Handling**: When errors occur during customer optimization processing, the error logging mechanism doesn't capture the specific Customer ID and ICCID that caused the issue.

2. **Generic Error Broadcasting**: Errors are logged generically and applied to all customers in the optimization session, making it impossible to identify which specific customer data caused the problem.

3. **Difficult Troubleshooting**: Support teams cannot efficiently identify and resolve customer-specific issues because the error context is lost.

### Error Occurrence Points
The issue can occur at multiple stages in the customer optimization flow:
- **Data Loading Phase**: When retrieving customer SIM card data
- **Portal-Specific Data Retrieval**: During M2M, Mobility, or Cross-Provider data fetching
- **Rate Pool Calculation**: When calculating optimal rate pools for customers
- **Assignment Processing**: During the rate pool assignment process

### Proposed Solution
Implement enhanced error context capture that includes:
- **Customer ID**: Specific identifier of the affected customer
- **ICCID**: SIM card identifier that experienced the issue
- **Error Details**: Specific error message and stack trace
- **Processing Stage**: Which stage of the optimization process failed
- **Timestamp**: When the error occurred

This would enable targeted error resolution and better customer support.