# Cross Provider Optimization - Data Flow Diagram (DFD)

## System Overview
The Cross Provider optimization system processes customer optimization requests across multiple service providers (Jasper, Pond, Telegence, Verizon IoT, Verizon PN) to find the most cost-effective rate plans and pooling strategies.

## Level 0 DFD - Context Diagram

```
                    ┌─────────────────┐
                    │   AMOP 1.0 UI   │
                    └─────────┬───────┘
                              │
                              ▼
    ┌─────────────────┐  optimization requests  ┌─────────────────────────────┐
    │   AMOP 2.0 UI   │ ────────────────────────▶│   Cross Provider            │
    └─────────────────┘                          │   Optimization System      │
                                                 └─────────────┬───────────────┘
    ┌─────────────────┐                                       │
    │ Customer Admin  │ ──────────────────────────────────────┘
    └─────────────────┘  rate pool configurations             │
                                                              │
    ┌─────────────────┐                          ┌────────────▼─────────────┐
    │ Service Provider│◀─────────────────────────│    Optimization         │
    │   Systems       │     device data sync     │    Results & Reports    │
    └─────────────────┘                          └──────────────────────────┘
```

## Level 1 DFD - Main Processes

```
                                    SQS Messages
                         ┌─────────────────────────────────┐
                         │                                 │
                         ▼                                 ▼
        ┌─────────────────────────────┐        ┌─────────────────────────────┐
        │    P1: Queue Message        │        │    P2: Cross Provider       │
        │    Processing               │        │    Customer Optimization    │
        │                             │        │                             │
        │ - Parse SQS attributes      │        │ - Get customer data         │
        │ - Validate portal type      │        │ - Retrieve rate plans       │
        │ - Route to optimization     │        │ - Create optimization       │
        └─────────┬───────────────────┘        │   instance                  │
                  │                            └─────────┬───────────────────┘
                  │                                      │
                  ▼                                      ▼
        ┌─────────────────────────────┐        ┌─────────────────────────────┐
        │    P3: Device Data          │        │    P4: Rate Pool            │
        │    Processing               │        │    Assignment               │
        │                             │        │                             │
        │ - Get device usage data     │        │ - Calculate optimal pools   │
        │ - Process SIM cards         │        │ - Assign devices to pools   │
        │ - Apply rate plans          │        │ - Generate cost savings     │
        └─────────┬───────────────────┘        └─────────┬───────────────────┘
                  │                                      │
                  ▼                                      ▼
        ┌─────────────────────────────┐        ┌─────────────────────────────┐
        │    P5: Results Processing   │        │    P6: Cleanup &            │
        │    & File Generation        │        │    Notification             │
        │                             │        │                             │
        │ - Build optimization result │        │ - Send AMOP 2.0 response    │
        │ - Generate Excel files      │        │ - Email notifications       │
        │ - Calculate statistics      │        │ - Update session status     │
        └─────────┬───────────────────┘        └─────────┬───────────────────┘
                  │                                      │
                  ▼                                      ▼
            [Result Files]                       [Session Complete]
```

## Level 2 DFD - Detailed Process Flow

### P1: Queue Message Processing
```
    [SQS Queue] ──────┐
                      │
                      ▼
    ┌─────────────────────────────────────────┐
    │ P1.1: Parse Message Attributes          │
    │                                         │
    │ - OptimizationSessionId                 │
    │ - AMOP_CUSTOMER_ID                      │
    │ - SERVICE_PROVIDER_IDS                  │
    │ - CUSTOMER_BILLING_PERIOD_ID            │
    │ - PORTAL_TYPE_ID                        │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐
    │ P1.2: Validate Portal Type              │
    │                                         │
    │ - Check for CrossProvider type          │
    │ - Route to appropriate processor        │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐
    │ P1.3: Duplicate Detection               │
    │                                         │
    │ - Check QUEUE_FINISHED_STATUSES         │
    │ - Prevent duplicate processing          │
    │ - Log warnings for duplicates           │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
                [Validated Message] ──▶ P2
```

### P2: Cross Provider Customer Optimization
```
    [Validated Message] ──┐
                          │
                          ▼
    ┌─────────────────────────────────────────┐
    │ P2.1: Get Customer Data                 │
    │                                         │
    │ crossProviderOptimizationRepository     │
    │ .GetOptimizationCustomer()              │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐       ┌─────────────────┐
    │ P2.2: Get Billing Period               │──────▶│   D1: Customer   │
    │                                         │       │   Database      │
    │ - Current billing period                │       └─────────────────┘
    │ - Next billing period for advance       │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐       ┌─────────────────┐
    │ P2.3: Get Customer Rate Plans           │──────▶│   D2: Rate Plan │
    │                                         │       │   Database      │
    │ customerRatePlanRepository              │       └─────────────────┘
    │ .GetCrossProviderCustomerRatePlans()    │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐       ┌─────────────────┐
    │ P2.4: Create Optimization Instance      │──────▶│   D3: Session   │
    │                                         │       │   Tracking DB   │
    │ StartCrossProviderOptimizationInstance  │       └─────────────────┘
    └─────────────────┬───────────────────────┘
                      │
                      ▼
                [Optimization Instance] ──▶ P3
```

### P3: Device Data Processing
```
    [Optimization Instance] ──┐
                              │
                              ▼
    ┌─────────────────────────────────────────┐       ┌─────────────────┐
    │ P3.1: Get SIM Card Data                 │──────▶│   D4: Device    │
    │                                         │       │   Database      │
    │ GetCrossProviderCustomerSimCards()      │       └─────────────────┘
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐
    │ P3.2: Process Rate Plan Groups          │
    │                                         │
    │ - Auto-change disabled rate plans       │
    │ - Group by CustomerRatePoolId           │
    │ - Validate zero-value plans             │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐       ┌─────────────────┐
    │ P3.3: Process No Rate Plan Devices      │──────▶│   D5: Queue     │
    │                                         │       │   Database      │
    │ - Record devices without rate plans     │       └─────────────────┘
    │ - Create default rate pool entries      │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
                [Processed Device Data] ──▶ P4
```

### P4: Rate Pool Assignment
```
    [Processed Device Data] ──┐
                              │
                              ▼
    ┌─────────────────────────────────────────┐
    │ P4.1: Build Rate Pool Collection        │
    │                                         │
    │ RatePoolCollectionFactory               │
    │ .CreateRatePoolCollection()             │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐
    │ P4.2: Assign SIM Cards to Pools         │
    │                                         │
    │ - Group by communication plan           │
    │ - Apply pooling rules                   │
    │ - Calculate cost optimizations          │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐
    │ P4.3: Handle Multiple Provider          │
    │ Scenarios                               │
    │                                         │
    │ - Scenario 1: Identical projected usage │
    │ - Scenario 2: Different projected usage │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
                [Optimized Assignments] ──▶ P5
```

### P5: Results Processing & File Generation
```
    [Optimized Assignments] ──┐
                              │
                              ▼
    ┌─────────────────────────────────────────┐       ┌─────────────────┐
    │ P5.1: Get Cross Provider Results        │──────▶│   D6: Results   │
    │                                         │       │   Database      │
    │ GetCrossProviderResults()               │       └─────────────────┘
    │ GetCrossProviderSharedPoolResults()     │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐
    │ P5.2: Build M2M Optimization Result     │
    │                                         │
    │ - BuildM2MOptimizationResult()          │
    │ - Calculate total device count          │
    │ - Generate cost savings statistics      │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐       ┌─────────────────┐
    │ P5.3: Generate Output Files             │──────▶│   D7: File      │
    │                                         │       │   Storage       │
    │ - RatePoolStatisticsWriter              │       └─────────────────┘
    │ - RatePoolAssignmentWriter              │
    │ - Excel file generation                 │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
                [Result Files] ──▶ P6
```

### P6: Cleanup & Notification
```
    [Result Files] ──┐
                     │
                     ▼
    ┌─────────────────────────────────────────┐       ┌─────────────────┐
    │ P6.1: Update Session Status             │──────▶│   D3: Session   │
    │                                         │       │   Tracking DB   │
    │ UpdateProcessingCustomerOptimization    │       └─────────────────┘
    │ Instance()                              │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐       ┌─────────────────┐
    │ P6.2: Send AMOP 2.0 Notification        │──────▶│   AMOP 2.0 API  │
    │                                         │       └─────────────────┘
    │ OptimizationAmopApiTrigger              │
    │ .SendResponseToAMOP20()                 │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐       ┌─────────────────┐
    │ P6.3: Queue Email Cleanup               │──────▶│   Email Service │
    │                                         │       └─────────────────┘
    │ QueueLastStepOptCustomerCleanup()       │
    └─────────────────┬───────────────────────┘
                      │
                      ▼
    ┌─────────────────────────────────────────┐       ┌─────────────────┐
    │ P6.4: Cleanup Processing Tables         │──────▶│   D8: Temp      │
    │                                         │       │   Processing DB │
    │ DeleteDataFromOptCustomerProcessing()   │       └─────────────────┘
    └─────────────────┬───────────────────────┘
                      │
                      ▼
                [Session Complete]
```

## Data Stores

| Store ID | Name | Description |
|----------|------|-------------|
| D1 | Customer Database | Customer information, account details, integration auth |
| D2 | Rate Plan Database | Service provider rate plans, communication plans |
| D3 | Session Tracking DB | Optimization sessions, instances, status tracking |
| D4 | Device Database | SIM card data, usage information, device details |
| D5 | Queue Database | Optimization queue entries, processing status |
| D6 | Results Database | Optimization results, device assignments, cost calculations |
| D7 | File Storage | Generated Excel files, statistics, assignment reports |
| D8 | Temp Processing DB | Temporary processing tables, cleanup tracking |

## External Entities

| Entity | Description | Interaction |
|--------|-------------|-------------|
| AMOP 1.0 UI | Legacy user interface | Displays optimization results with charges |
| AMOP 2.0 UI | Modern user interface | Receives API notifications, displays sessions |
| Customer Admin | System administrators | Configure rate pools, manage settings |
| Service Provider Systems | External carrier systems | Provide device data, rate information |
| Email Service | Notification system | Sends completion/error emails |

## Key Data Flows

1. **Optimization Request Flow**: AMOP UI → SQS Queue → Message Processing → Optimization Engine
2. **Device Data Flow**: Service Provider Systems → Device Database → Optimization Processing
3. **Rate Plan Flow**: Rate Plan Database → Optimization Engine → Results Processing
4. **Results Flow**: Optimization Engine → Results Database → File Generation → UI Display
5. **Notification Flow**: Results Processing → AMOP 2.0 API → UI Update
6. **Error Flow**: Any Process → Error Handler → AMOP 2.0 API + Email Service

## Critical Issues Addressed

1. **Session Visibility**: Enhanced AMOP 2.0 notification ensures sessions appear in modern UI
2. **Charge Calculation**: Improved logging detects zero charge issues in rate pool processing
3. **Progress Tracking**: Better error handling prevents optimizations from getting stuck
4. **Duplication Prevention**: Enhanced duplicate detection at session level
5. **Customer Rate Pool Handling**: Proper validation for multiple provider scenarios
6. **Session Persistence**: Delayed cleanup maintains UI visibility
7. **Small Pool Processing**: Enhanced logging tracks small customer pool optimizations

## Performance Considerations

- **Parallel Processing**: Multiple queues can be processed simultaneously
- **Caching**: Redis cache used for performance optimization
- **Batch Processing**: Device data processed in batches for efficiency
- **Asynchronous Operations**: SQS queues enable asynchronous processing
- **Database Optimization**: Indexed queries for fast data retrieval