# Customer Optimization System - Data Flow Diagram

## Complete Customer Optimization Data Flow with Colors

```mermaid
graph TD
    %% Starting Point
    A[🔵 AMOP 2.0 Customer Request] -->|Customer SQS Message| B[🟢 Session Management]
    
    %% Session Management
    B -->|Validate Session| C[🟡 Customer Validation]
    B -->|Create/Resume Session| B1[Session Metadata]
    B -->|Track Progress| B2[AMOP 2.0 Integration]
    
    %% Customer Validation
    C -->|Rev Customer ID| C1[🟠 GUID Authentication]
    C -->|AMOP Customer ID| C2[🟠 Integer Processing]
    C -->|Billing Period Check| D[🔴 Rate Plan Discovery]
    
    %% Rate Plan Discovery
    D -->|Customer Rate Plans| D1[🟣 Service Provider Filter]
    D -->|Eligibility Check| D2[🔵 Overage Rate Validation]
    D -->|Auto Change Check| E[🟢 Auto Change Logic]
    
    %% Auto Change Logic
    E -->|Auto Change Enabled| E1[🟡 Dynamic Rate Plans]
    E -->|Auto Change Disabled| E2[🟠 Fixed Rate Pools]
    E -->|Rate Plan Codes| F[🔴 Rate Pool Generation]
    
    %% Rate Pool Generation
    F -->|Customer Rate Pools| F1[🟣 Rate Pool Collections]
    F -->|Permutation Logic| F2[🔵 Sequence Generation]
    F -->|Compatibility Check| G[🟢 Queue Creation]
    
    %% Queue Creation
    G -->|Customer Queues| G1[🟡 M2M Queues]
    G -->|Cross-Provider| G2[🟠 Multi-Provider Queues]
    G -->|Rate Plan Sequences| H[🔴 Optimization Execution]
    
    %% Optimization Execution
    H -->|Customer Algorithms| H1[🟣 Cost Calculation Engine]
    H -->|Assignment Strategies| H2[🔵 Strategy Processing]
    H -->|Best Results| I[🟢 Result Compilation]
    
    %% Result Compilation
    I -->|Winning Queues| I1[🟡 Customer Statistics]
    I -->|Cost Savings| I2[🟠 Excel Reports]
    I -->|Optimization Data| J[🔴 Customer Email & Reporting]
    
    %% Customer Email & Reporting
    J -->|Customer Notifications| J1[🟣 Email Generation]
    J -->|Report Attachments| J2[🔵 Excel Attachments]
    J -->|Processing Tracking| K[🟢 Cleanup]
    
    %% Cleanup
    K -->|Customer Processing| K1[🟡 Processing Table Updates]
    K -->|Cross-Provider Coordination| K2[🟠 Provider Cleanup]
    K -->|Final Status| K3[🔴 Session Completion]

    %% Styling
    classDef blueStyle fill:#4A90E2,stroke:#333,stroke-width:2px,color:#fff
    classDef greenStyle fill:#7ED321,stroke:#333,stroke-width:2px,color:#fff
    classDef yellowStyle fill:#F5A623,stroke:#333,stroke-width:2px,color:#fff
    classDef orangeStyle fill:#F5A623,stroke:#333,stroke-width:2px,color:#fff
    classDef redStyle fill:#D0021B,stroke:#333,stroke-width:2px,color:#fff
    classDef purpleStyle fill:#9013FE,stroke:#333,stroke-width:2px,color:#fff
    
    class A,C1,D2,F2,H2,J2 blueStyle
    class B,C,E,G,I,K greenStyle
    class C2,E1,F1,G1,I1,K1 yellowStyle
    class C1,E2,G2,I2,K2 orangeStyle
    class D,F,H,J,K3 redStyle
    class D1,F1,H1,J1 purpleStyle
```

## Detailed Process Flow with Color Coding

### 🔵 **Blue Components - Initialization & Authentication**
- **AMOP 2.0 Customer Request**: Initial trigger from customer interface
- **GUID Authentication**: Rev customer authentication process
- **Overage Rate Validation**: Validates rate plan eligibility
- **Sequence Generation**: Creates rate plan permutations
- **Strategy Processing**: Executes assignment strategies
- **Excel Attachments**: Generates customer reports

### 🟢 **Green Components - Core Processing**
- **Session Management**: Manages customer optimization sessions
- **Customer Validation**: Validates customer data and permissions
- **Auto Change Logic**: Processes rate plan change rules
- **Queue Creation**: Creates optimization work queues
- **Result Compilation**: Compiles optimization results
- **Cleanup**: Final cleanup and session completion

### 🟡 **Yellow Components - Customer Data Processing**
- **Integer Processing**: AMOP customer ID processing
- **Dynamic Rate Plans**: Auto change enabled processing
- **Rate Pool Collections**: Customer rate pool grouping
- **M2M Queues**: M2M portal queue creation
- **Customer Statistics**: Customer optimization statistics
- **Processing Table Updates**: Customer processing tracking

### 🟠 **Orange Components - Multi-Provider & Advanced**
- **Fixed Rate Pools**: Auto change disabled processing
- **Multi-Provider Queues**: Cross-provider queue creation
- **Excel Reports**: Customer report generation
- **Provider Cleanup**: Cross-provider cleanup coordination

### 🔴 **Red Components - Decision Points & Core Logic**
- **Rate Plan Discovery**: Discovers customer rate plans
- **Rate Pool Generation**: Generates customer rate pools
- **Optimization Execution**: Executes optimization algorithms
- **Customer Email & Reporting**: Customer notification system
- **Session Completion**: Final session status

### 🟣 **Purple Components - Filtering & Calc**
- **Service Provider Filter**: Filters by service provider
- **Cost Calculation Engine**: Calculates customer costs
- **Email Generation**: Generates customer emails

## Customer-Specific Data Flow Elements

### Customer Input Processing
```mermaid
graph LR
    A[Customer SQS Message] -->|Customer Type| B{Rev or AMOP?}
    B -->|Rev Customer| C[GUID + Integration Auth]
    B -->|AMOP Customer| D[Integer ID + Simplified Auth]
    C --> E[Customer Rate Plan Loading]
    D --> E
```

### Rate Plan Processing Flow
```mermaid
graph LR
    A[Customer Rate Plans] -->|Auto Change Check| B{Auto Change Enabled?}
    B -->|Yes| C[Dynamic Rate Plan Changes]
    B -->|No| D[Fixed Customer Rate Pools]
    C --> E[Rate Plan Permutations]
    D --> F[Customer Rate Pool Grouping]
    E --> G[Customer Optimization Queues]
    F --> G
```

### Cross-Provider Processing
```mermaid
graph LR
    A[Customer Request] -->|Multi-Provider| B[Service Provider IDs]
    B --> C[Cross-Provider Rate Plans]
    C --> D[Unified Customer Processing]
    D --> E[Cross-Provider Queues]
    E --> F[Unified Customer Reports]
```

### Customer Email Coordination
```mermaid
graph LR
    A[Customer Results] -->|Multiple Providers| B[Processing Coordination]
    B -->|All Complete?| C{Check Status}
    C -->|No| D[Wait & Retry]
    C -->|Yes| E[Consolidate Results]
    D --> C
    E --> F[Send Customer Email]
```

## Legend

| Color | Represents | Key Functions |
|-------|------------|---------------|
| 🔵 Blue | Initialization & Auth | Request processing, authentication, validation |
| 🟢 Green | Core Processing | Session management, validation, compilation |
| 🟡 Yellow | Customer Data | Customer-specific data processing and tracking |
| 🟠 Orange | Multi-Provider | Cross-provider and advanced processing |
| 🔴 Red | Decision Points | Core logic and decision-making processes |
| 🟣 Purple | Filtering & Calc | Data filtering, validation, and calculations |

This comprehensive DFD shows the complete customer optimization flow with color-coded components for easy understanding of the different processing stages and their relationships.