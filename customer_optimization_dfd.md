# Customer Optimization Data Flow Diagram

## Overview
This DFD shows the customer optimization process flow with error handling and logging mechanisms.

## Data Flow Diagram

```mermaid
graph TB
    %% External Entities
    API[API Client] 
    DB[(Database)]
    CACHE[(Redis Cache)]
    EMAIL[Email Service]
    LOG[(Error Logs)]
    
    %% Processes
    P1[1.0 StartConfirm API]
    P2[2.0 Validate Permissions]
    P3[3.0 Portal Type Detection]
    P4[4.0 Queue Customer Optimization]
    P5[5.0 Process Optimization]
    P6[6.0 Load SIM Data]
    P7[7.0 Rate Pool Calculation]
    P8[8.0 Assignment Processing]
    P9[9.0 Generate Results]
    P10[10.0 Cleanup & Notification]
    
    %% Data Stores
    DS1[(Optimization Sessions)]
    DS2[(Queue Items)]
    DS3[(SIM Cards Data)]
    DS4[(Rate Pools)]
    DS5[(Optimization Results)]
    
    %% Main Flow
    API -->|Request| P1
    P1 -->|Session Data| DS1
    P1 -->|Validation Request| P2
    P2 -->|Portal Detection| P3
    P3 -->|Queue Item| P4
    P4 -->|Queue Data| DS2
    
    DS2 -->|Queue Item| P5
    P5 -->|SIM Request| P6
    P6 -->|SIM Data| DS3
    P6 -->|Rate Calculation| P7
    P7 -->|Rate Pools| DS4
    DS4 -->|Assignment Data| P8
    
    P8 -->|Results Data| P9
    P9 -->|Final Results| DS5
    DS5 -->|Completion Trigger| P10
    P10 -->|Notification| EMAIL
    
    %% Error Handling & Logging
    P1 -.->|Validation Errors| LOG
    P2 -.->|Permission Errors| LOG
    P3 -.->|Detection Errors| LOG
    P4 -.->|Queue Errors| LOG
    P5 -.->|Processing Errors| LOG
    P6 -.->|Data Load Errors| LOG
    P7 -.->|Calculation Errors| LOG
    P8 -.->|Assignment Errors| LOG
    P9 -.->|Result Generation Errors| LOG
    P10 -.->|Cleanup Errors| LOG
    
    %% Caching Flow
    P8 -.->|Cache Data| CACHE
    CACHE -.->|Cached Data| P8
    
    %% Database Interactions
    P2 <-->|User Permissions| DB
    P6 <-->|SIM Cards| DB
    P7 <-->|Rate Plans| DB
    P9 -->|Store Results| DB
    
    %% Process States
    P5 -.->|Partial State| CACHE
    CACHE -.->|Resume State| P5
    
    style API fill:#e1f5fe
    style LOG fill:#ffebee
    style EMAIL fill:#f3e5f5
    style CACHE fill:#fff3e0
    style DB fill:#e8f5e8
```

## Process Descriptions

### 1.0 StartConfirm API
- **Input**: API Request with optimization parameters
- **Output**: Session creation, validation trigger
- **Error Logging**: Invalid request format, missing parameters

### 2.0 Validate Permissions
- **Input**: User credentials, session data
- **Output**: Permission validation result
- **Error Logging**: Unauthorized access, invalid credentials

### 3.0 Portal Type Detection
- **Input**: Session parameters
- **Output**: Portal type (M2M/Mobility/Cross-Provider)
- **Error Logging**: Unknown portal type, configuration errors

### 4.0 Queue Customer Optimization
- **Input**: Portal type, optimization parameters
- **Output**: Queue item creation
- **Error Logging**: Queue service failures, resource limits

### 5.0 Process Optimization
- **Input**: Queue items
- **Output**: Processing triggers, state management
- **Error Logging**: Processing failures, timeout errors

### 6.0 Load SIM Data
- **Input**: Portal type, filtering criteria
- **Output**: SIM cards dataset
- **Error Logging**: Data access failures, empty datasets

### 7.0 Rate Pool Calculation
- **Input**: SIM data, rate plan configurations
- **Output**: Calculated rate pools
- **Error Logging**: Calculation errors, invalid rate plans

### 8.0 Assignment Processing
- **Input**: Rate pools, SIM assignments
- **Output**: Optimization assignments
- **Error Logging**: Assignment conflicts, resource exhaustion

### 9.0 Generate Results
- **Input**: Assignment data
- **Output**: Optimization results
- **Error Logging**: Result generation failures, data inconsistencies

### 10.0 Cleanup & Notification
- **Input**: Completion status
- **Output**: Email notifications, resource cleanup
- **Error Logging**: Notification failures, cleanup errors

## Data Stores

| Store | Description | Error Conditions |
|-------|-------------|-----------------|
| Optimization Sessions | Active optimization sessions | Session corruption, timeout |
| Queue Items | Processing queue entries | Queue overflow, item corruption |
| SIM Cards Data | Device and usage information | Data staleness, access failures |
| Rate Pools | Available rate configurations | Pool exhaustion, invalid rates |
| Optimization Results | Final optimization outcomes | Result corruption, storage failures |

## Error Handling Strategy

### Error Categories
1. **Validation Errors**: Input validation, permission checks
2. **Processing Errors**: Calculation failures, resource limits
3. **Data Errors**: Database access, data integrity issues
4. **System Errors**: Service unavailability, timeout conditions

### Error Logging Format
```json
{
  "timestamp": "ISO-8601",
  "process_id": "Process identifier",
  "error_type": "Category",
  "error_code": "Specific error code",
  "message": "Human readable message",
  "context": {
    "session_id": "Optimization session",
    "user_id": "Requesting user",
    "portal_type": "Portal variant"
  }
}
```

### Recovery Mechanisms
- **Retry Logic**: Automatic retry for transient failures
- **State Persistence**: Cache intermediate states for resumption
- **Graceful Degradation**: Partial results when possible
- **Notification**: Alert administrators for critical failures