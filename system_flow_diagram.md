# Altaworx SimCard Cost Optimizer - System Flow Diagram

```mermaid
graph TD
    %% User Interface Layer
    UI[Web UI/Client] --> API[OptimizationApiController]
    
    %% API Layer
    API --> |start-confirm| ValidateAuth[Validate Headers & Permissions]
    ValidateAuth --> |"user-name, x-tenant-id"| CreateSession[Create Optimization Session]
    CreateSession --> QueueMsg[Queue SQS Message]
    
    %% Queue Processing
    QueueMsg --> |SQS Event| SQSQueue[(SQS Queue)]
    SQSQueue --> |Trigger| Lambda1[AltaworxSimCardCostOptimizer Lambda]
    SQSQueue --> |Trigger| Lambda2[AltaworxSimCardCostQueueCustomerOptimization Lambda]
    SQSQueue --> |Trigger| Lambda3[AltaworxSimCardCostOptimizerCleanup Lambda]
    
    %% Main Lambda Processing Flow
    Lambda1 --> |Handler| ProcessEvent[ProcessEvent]
    ProcessEvent --> ProcessRecord[ProcessEventRecord]
    ProcessRecord --> |Parse Message Attributes| ParseAttr[Parse QueueIds, ChargeType, SkipLowerCostCheck]
    ParseAttr --> |Decision| ChainDecision{Is Chaining Process?}
    
    ChainDecision --> |Yes| CheckRedis[Check Redis Cache Connection]
    CheckRedis --> |Valid| ProcessContinue[ProcessQueuesContinue]
    CheckRedis --> |Invalid| ErrorStop[Stop Process - No Cache]
    
    ChainDecision --> |No| ProcessQueues[ProcessQueues]
    
    %% Core Optimization Process
    ProcessQueues --> StartInstance[StartOptimizationInstance]
    ProcessContinue --> StartInstance
    
    StartInstance --> |Status: NotStarted| LoadData[Load SimCard Data & Rate Plans]
    LoadData --> |Status: CommGroupSetup| BuildRatePools[Build Rate Pool Collection]
    BuildRatePools --> |Status: RunningPermutations| OptimizeRates[Run Optimization Algorithm]
    
    %% Optimization Engine
    OptimizeRates --> CalcCosts[Calculate Costs for All Permutations]
    CalcCosts --> FindBest[Find Best Cost Optimization]
    FindBest --> ValidateResults[Validate Results]
    
    %% Result Processing
    ValidateResults --> |Success| WriteResults[OptimizationResultDbWriter.RecordResults]
    ValidateResults --> |Error| ErrorHandling[Error Handling]
    
    WriteResults --> RecordAssignments[Record Rate Pool Assignments]
    RecordAssignments --> RecordCosts[Record Total Costs]
    RecordCosts --> UpdateStatus[Update Optimization Status]
    
    %% Status Management
    UpdateStatus --> |Success| StopSuccess[StopOptimizationInstance: CompleteWithSuccess]
    ErrorHandling --> StopError[StopOptimizationInstance: CompleteWithErrors]
    
    %% Cleanup Process
    StopSuccess --> |Trigger Cleanup| Lambda3
    StopError --> |Trigger Cleanup| Lambda3
    Lambda3 --> |Status: CleaningUp| CleanupProcess[Cleanup Temporary Data]
    CleanupProcess --> FinalStatus[Final Status: CompleteWithSuccess/CompleteWithErrors]
    
    %% Data Storage
    WriteResults --> DB[(SQL Server Database)]
    LoadData --> DB
    UpdateStatus --> DB
    
    %% Caching Layer
    CheckRedis --> |Optional| RedisCache[(Redis Cache)]
    LoadData --> |Cache Hit/Miss| RedisCache
    
    %% Repository Layer
    LoadData --> DeviceRepo[OptimizationMobilityDeviceRepository]
    LoadData --> RatePlanRepo[CarrierRatePlanRepository]
    DeviceRepo --> DB
    RatePlanRepo --> DB
    
    %% Status Flow
    subgraph "Optimization Status Flow"
        NotStarted[NotStarted] --> CommGroupSetup[CommGroupSetup]
        CommGroupSetup --> RunningPermutations[RunningPermutations]
        RunningPermutations --> CleaningUp[CleaningUp]
        CleaningUp --> CompleteSuccess[CompleteWithSuccess]
        CleaningUp --> CompleteErrors[CompleteWithErrors]
    end
    
    %% API Endpoints
    subgraph "API Endpoints"
        StartConfirm["POST /start-confirm"]
        CreateSession2["POST /Create-Confirm-Session"]
        QueueRatePlan["POST /Queue-Rate-Plan-Changes"]
        Upload["POST /Upload"]
    end
    
    %% AWS Components
    subgraph "AWS Infrastructure"
        SQSQueue
        Lambda1
        Lambda2
        Lambda3
        RedisCache
    end
    
    %% Error Handling Flow
    ErrorHandling --> LogError[Log Error Information]
    LogError --> SendEmail[Send Error Notification Email]
    SendEmail --> StopError
    
    %% Monitoring & Logging
    Lambda1 --> CloudWatch[AWS CloudWatch Logs]
    Lambda2 --> CloudWatch
    Lambda3 --> CloudWatch
    
    style UI fill:#e1f5fe
    style API fill:#f3e5f5
    style SQSQueue fill:#fff3e0
    style Lambda1 fill:#e8f5e8
    style Lambda2 fill:#e8f5e8
    style Lambda3 fill:#e8f5e8
    style DB fill:#fce4ec
    style RedisCache fill:#f1f8e9
    style ErrorHandling fill:#ffebee
```

## Key Components Explanation

### 1. **API Layer (OptimizationApiController)**
- Handles HTTP requests from web UI
- Validates user authentication and permissions
- Creates optimization sessions
- Queues SQS messages for processing

### 2. **Lambda Functions**
- **AltaworxSimCardCostOptimizer**: Main optimization processing
- **AltaworxSimCardCostQueueCustomerOptimization**: Customer-specific optimization
- **AltaworxSimCardCostOptimizerCleanup**: Post-processing cleanup

### 3. **Optimization Workflow**
1. Parse SQS message attributes (QueueIds, ChargeType, etc.)
2. Start optimization instance with status tracking
3. Load device data and rate plans from database
4. Build rate pool collections
5. Run optimization algorithms to find best cost scenarios
6. Validate and record results
7. Update status and trigger cleanup

### 4. **Status Management**
- **NotStarted** → **CommGroupSetup** → **RunningPermutations** → **CleaningUp** → **Complete**
- Error states lead to **CompleteWithErrors**
- Success states lead to **CompleteWithSuccess**

### 5. **Data Flow**
- Input: Customer devices, billing periods, rate plans
- Processing: Cost calculations, optimization algorithms
- Output: Optimized rate assignments, cost savings reports

### 6. **Infrastructure**
- **SQS**: Message queuing for asynchronous processing
- **Lambda**: Serverless compute for optimization logic
- **SQL Server**: Primary data storage
- **Redis**: Optional caching layer for performance
- **CloudWatch**: Logging and monitoring