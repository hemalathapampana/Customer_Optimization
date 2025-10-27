# Cross-Provider Customer Optimization Issue Analysis

## Issue Summary

**Customer:** Altaworx Mobility CFU  
**Customer Cycle:** February  
**Providers:** Jasper, Pond, Telegence UAT Only, Verizon IoT, Verizon PN

### Reported Issues:
1. **UI Display Problems:**
   - Optimizations started from 1.0 show up in 1.0 UI but with no charges (incorrect)
   - Neither optimization shows up in 2.0 UI
   - Sessions appearing in triplicate (duplication issue)

2. **Session Management Issues:**
   - Optimizations started but never appear in session list
   - Optimizations stuck in progress with no movement for hours
   - Large optimization disappeared from list after 2 days

3. **Email Notification Issues:**
   - Received 4 emails for optimization summaries 2 days after completion
   - Optimization no longer appearing in list after email notification

## Root Cause Analysis

### 1. Cross-Provider Session Tracking Issue
**Location:** `AltaworxSimCardCostQueueCustomerOptimization.cs` lines 725-728, 746-747

**Problem:** Cross-provider optimizations use different session tracking mechanism than standard M2M optimizations:
- Uses `crossProviderOptimizationRepository.StartCrossProviderOptimizationInstance()`
- Updates via `crossProviderOptimizationRepository.UpdateProcessingCustomerOptimizationInstance()`
- Different portal type (`PortalTypes.CrossProvider` vs `PortalTypes.M2M`)

**Impact:** UI queries may not be properly filtering/displaying cross-provider sessions

### 2. AMOP API Response Inconsistency
**Location:** `AltaworxSimCardCostQueueCustomerOptimization.cs` lines 748-749, 765-766

**Problem:** Only error messages are sent to AMOP 2.0 API via `OptimizationAmopApiTrigger.SendResponseToAMOP20()`. Success responses may not be properly communicated to the 2.0 UI.

### 3. Customer Rate Pool Duplication
**Location:** `AltaworxSimCardCostQueueCustomerOptimization.cs` lines 532-533, 818-819

**Problem:** Rate pool grouping logic groups by `CustomerRatePoolId`, but the ticket mentions that database maintains single records while UI shows multiple entries based on projected usage differences.

### 4. Results Processing Timing Issue
**Location:** `AltaworxSimCardCostOptimizerCleanup.cs` lines 2346-2358

**Problem:** Results processing for cross-provider happens in cleanup phase, which may cause delays in session visibility and charge calculations.

## Data Flow Diagram (DFD)

```mermaid
graph TD
    A[SQS Message Queue] --> B[Function Handler]
    B --> C{Portal Type Detection}
    
    C -->|M2M| D[ProcessCustomerOptimizationByPortalType]
    C -->|Cross-Provider| E[ProcessCrossProviderCustomerOptimization]
    
    E --> F[Extract Customer ID & Service Provider IDs]
    F --> G[RunCrossProviderCustomerOptimization]
    
    G --> H[Get Customer Data]
    H --> I[Get Billing Period]
    I --> J[Get Cross-Provider Rate Plans]
    
    J --> K{Rate Plans Found?}
    K -->|No| L[Create Error Instance]
    K -->|Yes| M[StartCrossProviderOptimizationInstance]
    
    M --> N[Process Rate Pool Groups]
    N --> O[Group SIM Cards by CustomerRatePoolId]
    O --> P[Process Each Rate Pool Group]
    
    P --> Q[ProcessRatePoolGroup]
    Q --> R[Optimization Algorithm Processing]
    R --> S[Store Results in Database]
    
    S --> T[UpdateProcessingCustomerOptimizationInstance]
    T --> U{Is Last Instance?}
    
    U -->|Yes| V[Trigger Cleanup Process]
    U -->|No| W[Mark Instance Processed]
    
    V --> X[AltaworxSimCardCostOptimizerCleanup]
    X --> Y[WriteCrossProviderCustomerResults]
    Y --> Z[Generate Result Files]
    Z --> AA[ProcessResultForCrossProvider]
    
    AA --> BB[Send Email Notification]
    AA --> CC{Error Occurred?}
    
    CC -->|Yes| DD[Send Error to AMOP 2.0 API]
    CC -->|No| EE[Silent Success - No API Call]
    
    L --> FF[Send Error to AMOP 2.0 API]
    
    % UI Display Issues
    GG[1.0 UI Query] --> HH[Standard Optimization Repository]
    II[2.0 UI Query] --> JJ[Cross-Provider Repository?]
    
    HH --> KK[Shows with No Charges]
    JJ --> LL[No Results Found]
    
    % Database Layer
    S --> MM[(Cross-Provider Results DB)]
    T --> NN[(Optimization Instance DB)]
    AA --> OO[(Customer Rate Pool DB)]
    
    % Email System
    BB --> PP[Email Service]
    PP --> QQ[Customer Email Notification]
    
    style E fill:#ffcccc
    style EE fill:#ffcccc
    style KK fill:#ffcccc
    style LL fill:#ffcccc
    style O fill:#fff2cc
```

## Key Issues Identified

### 1. Missing Success Response to AMOP 2.0
**File:** `AltaworxSimCardCostQueueCustomerOptimization.cs`
**Issue:** Only error cases call `OptimizationAmopApiTrigger.SendResponseToAMOP20()`. Successful completions don't notify the 2.0 UI.

### 2. UI Query Inconsistency
**Issue:** 1.0 and 2.0 UIs likely query different repositories or use different filtering criteria for cross-provider optimizations.

### 3. Session State Management
**File:** `AltaworxSimCardCostOptimizerCleanup.cs` line 2350
**Issue:** Session updates happen late in the cleanup process, causing delays in UI visibility.

### 4. Rate Pool Display Logic
**Issue:** Customer Rate Pool grouping creates multiple UI entries for same pool when projected usage differs, but database maintains single record.

## Recommended Fixes

### 1. Add Success Response to AMOP 2.0
```csharp
// In ProcessResultForCrossProvider method
if (isCustomerOptimization && !hasErrors)
{
    OptimizationAmopApiTrigger.SendResponseToAMOP20(context, "Success", 
        instance.SessionId.ToString(), fileResult, fileResult.TotalDeviceCount, 
        null, 0, instance.AMOPCustomerId.ToString(), additionalData);
}
```

### 2. Standardize UI Queries
- Ensure both 1.0 and 2.0 UIs query the same cross-provider repository
- Add proper filtering for cross-provider portal type

### 3. Improve Session Tracking
- Update session status immediately after instance creation
- Add real-time progress updates during processing

### 4. Fix Rate Pool Display
- Implement consistent rate pool display logic
- Ensure charge calculations are properly associated with sessions

### 5. Add Monitoring and Logging
- Enhanced logging for cross-provider optimization flows
- Real-time monitoring of session states
- Alerts for stuck optimizations

## Testing Strategy

1. **Cross-Provider Session Creation Test**
   - Verify sessions appear in both 1.0 and 2.0 UIs immediately
   - Confirm charge calculations are displayed correctly

2. **Rate Pool Display Test**
   - Test scenarios with identical projected usage
   - Test scenarios with different projected usage across providers

3. **Email Notification Timing Test**
   - Verify emails are sent promptly after completion
   - Ensure sessions remain visible after email notification

4. **Progress Tracking Test**
   - Monitor optimization progress in real-time
   - Verify stuck optimization detection and recovery