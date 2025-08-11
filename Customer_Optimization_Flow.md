# Customer Optimization Flow with Error Logging

## Overview
The Customer Optimization system is a cloud-based solution that optimizes SIM card cost assignments for customers across different portal types (M2M, Cross-Provider, Mobility). This document outlines the complete flow with error handling and logging mechanisms.

## System Architecture

```mermaid
graph TB
    A[SQS Event Trigger] --> B[Function Handler]
    B --> C{Validate Message}
    C -->|Valid| D[Initialize Context]
    C -->|Invalid| E[Log Error & Exit]
    D --> F[Redis Cache Check]
    F --> G{Cache Available?}
    G -->|Yes| H[Use Cache]
    G -->|No| I[Log Warning & Continue]
    H --> J[Process Event]
    I --> J
    J --> K[Route by Portal Type]
    K --> L[M2M Optimization]
    K --> M[Cross-Provider Optimization]
    K --> N[Mobility Optimization]
    L --> O[Generate Results]
    M --> O
    N --> O
    O --> P[Write to Database]
    P --> Q[Update Status]
    Q --> R[Cleanup]
    
    E --> S[Error Notification]
    O -->|Error| S
    P -->|Error| S
    Q -->|Error| S
```

## Main Flow Components

### 1. Entry Point Flow

```mermaid
sequenceDiagram
    participant SQS as SQS Queue
    participant FH as Function Handler
    participant CTX as Lambda Context
    participant LOG as Logger
    participant ERR as Error Handler

    SQS->>FH: SQS Event Message
    FH->>CTX: Initialize Context
    FH->>LOG: LogInfo("Processing started")
    
    alt Message Validation Success
        FH->>FH: Process Message
    else Validation Failure
        FH->>ERR: Log Exception
        FH->>LOG: LogInfo("EXCEPTION", "Validation failed")
        FH->>ERR: Send Error Notification
    end
```

### 2. Customer Optimization Processing Flow

```mermaid
flowchart TD
    A[Start Processing] --> B{Check Customer Type}
    B -->|M2M/REV| C[Process REV Customer]
    B -->|AMOP| D[Process AMOP Customer]
    B -->|Cross-Provider| E[Process Cross-Provider]
    
    C --> F[Validate Customer ID]
    D --> G[Validate AMOP Customer ID]
    E --> H[Validate Service Provider IDs]
    
    F -->|Valid| I[Get Rate Plans]
    F -->|Invalid| J[Log Error: Blank Customer ID]
    G -->|Valid| K[Get AMOP Rate Plans]
    G -->|Invalid| L[Log Error: Invalid AMOP ID]
    H -->|Valid| M[Get Cross-Provider Plans]
    H -->|Invalid| N[Log Error: Invalid Service Provider]
    
    I --> O[Start Optimization Instance]
    K --> O
    M --> O
    
    O --> P{Instance Created?}
    P -->|Yes| Q[Run Optimization Algorithm]
    P -->|No| R[Log Error: Instance Creation Failed]
    
    Q --> S[Calculate Results]
    S --> T[Record Results to DB]
    T --> U[Update Status: Complete with Success]
    
    J --> V[Stop Instance: Complete with Errors]
    L --> V
    N --> V
    R --> V
    
    U --> W[Send Completion Notification]
    V --> X[Send Error Notification]
```

## Error Handling and Logging

### Error Categories

| Error Type | Log Level | Action | Notification |
|------------|-----------|--------|--------------|
| **Validation Errors** | EXCEPTION | Stop processing | Email to admin |
| **Database Errors** | EXCEPTION | Retry with backoff | Email to admin |
| **Cache Errors** | WARNING | Continue without cache | Log only |
| **Rate Plan Errors** | EXCEPTION | Stop optimization | Email to admin |
| **Timeout Errors** | EXCEPTION | Mark as failed | Email to admin |

### Error Logging Format

```csharp
// Standard error logging pattern used throughout the system
LogInfo(context, "EXCEPTION", $"Error message with details: {ex.Message}");
LogInfo(context, "EXCEPTION", $"Stack trace: {ex.StackTrace}");
```

### Key Error Scenarios

```mermaid
graph LR
    A[Error Occurs] --> B{Error Type}
    B -->|Validation| C[Log: Customer ID Missing]
    B -->|Database| D[Log: SQL Connection Failed]
    B -->|Timeout| E[Log: Process Timeout]
    B -->|Cache| F[Log: Redis Unavailable]
    B -->|Rate Plan| G[Log: Invalid Rate Plan Config]
    
    C --> H[Stop Processing]
    D --> I[Retry with Backoff]
    E --> H
    F --> J[Continue Without Cache]
    G --> H
    
    H --> K[Set Status: CompleteWithErrors]
    I --> L{Retry Success?}
    J --> M[Continue Processing]
    
    L -->|Yes| M
    L -->|No| K
    
    K --> N[Send Error Notification]
    M --> O[Continue Normal Flow]
```

## Detailed Process Flows

### 1. Message Validation Flow

```mermaid
flowchart TD
    A[Receive SQS Message] --> B{Single Message?}
    B -->|No| C[Log: Multiple messages received]
    B -->|Yes| D{Customer Type Present?}
    
    C --> E[Exit with Error]
    D -->|No| F[Log: No Customer Type]
    D -->|Yes| G{Customer ID Present?}
    
    F --> E
    G -->|No| H[Log: No Customer ID]
    G -->|Yes| I{Billing Period Present?}
    
    H --> E
    I -->|No| J[Log: No Billing Period]
    I -->|Yes| K[Validation Successful]
    
    J --> E
    E --> L[Send Error Notification]
    K --> M[Proceed to Processing]
```

### 2. Optimization Instance Management

```mermaid
stateDiagram-v2
    [*] --> NotStarted
    NotStarted --> CommGroupSetup: Initialize
    CommGroupSetup --> RunningPermutations: Start calculation
    RunningPermutations --> CleaningUp: Complete calculation
    CleaningUp --> CompleteWithSuccess: Success
    CleaningUp --> CompleteWithErrors: Errors occurred
    
    NotStarted --> CompleteWithErrors: Validation failed
    CommGroupSetup --> CompleteWithErrors: Setup failed
    RunningPermutations --> CompleteWithErrors: Calculation failed
    
    CompleteWithSuccess --> [*]
    CompleteWithErrors --> [*]
```

### 3. Error Recovery and Retry Logic

```mermaid
flowchart TD
    A[Operation Failed] --> B{Retryable Error?}
    B -->|Yes| C[Check Retry Count]
    B -->|No| D[Log Fatal Error]
    
    C --> E{Retry Count < Max?}
    E -->|Yes| F[Wait with Backoff]
    E -->|No| G[Log Max Retries Exceeded]
    
    F --> H[Retry Operation]
    H --> I{Success?}
    I -->|Yes| J[Continue Processing]
    I -->|No| A
    
    D --> K[Stop Processing]
    G --> K
    K --> L[Set Error Status]
    L --> M[Send Error Notification]
```

## Database Operations and Error Handling

### Critical Database Operations

1. **Optimization Instance Management**
   - Create instance: `INSERT INTO OptimizationInstance`
   - Update status: `UPDATE OptimizationInstance SET RunStatusId`
   - Stop instance: `UPDATE OptimizationInstance SET RunEndTime`

2. **Result Recording**
   - Record rate pool assignments
   - Update total costs
   - Save optimization results

3. **Error Tracking**
   - Log error messages in OptimizationCustomerProcessing
   - Track failed operations with timestamps
   - Maintain error count metrics

### SQL Error Handling Pattern

```csharp
try
{
    // Database operation
    using (var conn = new SqlConnection(connectionString))
    {
        // SQL commands
    }
}
catch (SqlException ex)
{
    LogInfo(context, "EXCEPTION", 
        $"SQL Error: {ex.Message}, ErrorCode:{ex.ErrorCode}-{ex.Number}");
    LogInfo(context, "EXCEPTION", $"Stack Trace: {ex.StackTrace}");
    
    // Set error status and notify
    StopOptimizationInstance(context, instanceId, OptimizationStatus.CompleteWithErrors);
}
catch (Exception ex)
{
    LogInfo(context, "EXCEPTION", $"General Error: {ex.Message}");
    LogInfo(context, "EXCEPTION", $"Stack Trace: {ex.StackTrace}");
}
```

## Monitoring and Alerting

### Key Metrics to Monitor

1. **Performance Metrics**
   - Processing time per customer
   - Queue processing rate
   - Cache hit/miss ratio
   - Database response times

2. **Error Metrics**
   - Error rate by error type
   - Failed optimization instances
   - Retry count distribution
   - Timeout occurrences

3. **Business Metrics**
   - Customers processed per hour
   - Cost optimization savings
   - Rate plan assignment accuracy

### Alert Configuration

```yaml
alerts:
  high_error_rate:
    condition: error_rate > 5%
    action: notify_admin
    
  processing_timeout:
    condition: processing_time > 300s
    action: notify_admin
    
  cache_unavailable:
    condition: cache_miss_rate > 90%
    action: log_warning
    
  database_errors:
    condition: sql_error_count > 10/hour
    action: notify_admin
```

## Best Practices for Error Handling

### 1. Logging Standards

- **Always include context**: Customer ID, Instance ID, Queue ID
- **Use structured logging**: Include error codes and categories
- **Log at appropriate levels**: INFO, WARNING, EXCEPTION
- **Include stack traces**: For debugging and root cause analysis

### 2. Error Recovery

- **Implement retry logic**: With exponential backoff
- **Graceful degradation**: Continue without non-critical components
- **Circuit breaker pattern**: For external dependencies
- **Timeout handling**: Prevent indefinite waits

### 3. Notification Strategy

- **Immediate alerts**: For critical system failures
- **Batched notifications**: For non-critical warnings
- **Escalation procedures**: For unresolved issues
- **Status dashboards**: For real-time monitoring

## Configuration Management

### Environment Variables

| Variable | Purpose | Error Handling |
|----------|---------|----------------|
| `QueuesPerInstance` | Controls processing load | Default to 5 if not set |
| `SanityCheckTimeLimit` | Timeout for operations | Default to 180s if not set |
| `ErrorNotificationEmailReceiver` | Alert destination | Log error if not configured |
| `Redis Connection String` | Cache configuration | Continue without cache if invalid |

### Error Handling for Missing Configuration

```csharp
// Example configuration validation
if (QueuesPerInstance == 0)
{
    QueuesPerInstance = DEFAULT_QUEUES_PER_INSTANCE;
    LogInfo(context, "WARNING", "QueuesPerInstance not configured, using default");
}

if (string.IsNullOrEmpty(ErrorNotificationEmailReceiver))
{
    LogInfo(context, "WARNING", "Error notification email not configured");
}
```

## Conclusion

The Customer Optimization system implements comprehensive error handling and logging to ensure reliable processing of customer data. The multi-layered approach includes:

- **Proactive validation** at entry points
- **Graceful error recovery** with retry mechanisms
- **Comprehensive logging** for troubleshooting
- **Real-time monitoring** and alerting
- **Structured error categorization** for appropriate responses

This design ensures system resilience while maintaining visibility into operations and quick issue resolution.