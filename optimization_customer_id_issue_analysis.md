# Optimization Customer ID Issue Analysis and Fix

## Problem Summary
Customer ID and ICCID are missing from error messages sent to AMOP 2.0 when optimization errors occur. This makes it difficult to identify which specific customer experienced the error.

## Root Cause Analysis

### Issue #1: Cross-Provider Optimization - Wrong Variable Type
**Location**: `AltaworxSimCardCostQueueCustomerOptimization.cs` lines 750 and 767

**Problem**: 
The method `RunCrossProviderCustomerOptimization` accepts an `int customerId` parameter, but in error handling, it's being converted to string using `customerId.ToString()`. However, this `customerId` is an AMOP Customer ID (integer), not the actual customer GUID that should be used for identification.

**Code Issue**:
```csharp
// Line 683: Method signature shows customerId is int (AMOP Customer ID)
private async Task RunCrossProviderCustomerOptimization(KeySysLambdaContext context, int tenantId, int customerId, ...)

// Lines 750 & 767: Using wrong customerId (AMOP ID instead of actual customer GUID)
OptimizationAmopApiTrigger.SendResponseToAMOP20(context, "ErrorMessage", optimizationSessionId.ToString(), null, 0, errorMessage, 0, customerId.ToString(), additionalData);
```

**Root Cause**: The method gets customer information via `crossProviderOptimizationRepository.GetOptimizationCustomer()` which returns an `OptimizationCustomer` object containing the actual customer GUID (`customer.CustomerId`), but the error handling is incorrectly using the integer AMOP Customer ID instead.

### Issue #2: RevAccountNumber vs CustomerID Confusion
**Location**: Various locations in both optimization files

**Problem**: 
In some error cases, the code sends `revAccountNumber` instead of `customerId`, which may be empty or null if `GetRevAccountNumber()` fails to find a matching record.

**Code Examples**:
```csharp
// AltaworxSimCardCostMobilityCustomerOptimization.cs line 262
OptimizationAmopApiTrigger.SendResponseToAMOP20(context, "ErrorMessage", optimizationSessionId.ToString(), null, 0, errorMessage, 0, revAccountNumber, additionalData);

// AltaworxSimCardCostQueueCustomerOptimization.cs lines 350, 386
OptimizationAmopApiTrigger.SendResponseToAMOP20(context, "ErrorMessage", optimizationSessionId.ToString(), null, 0, errorMessage, 0, revAccountNumber, additionalData);
```

### Issue #3: GetRevAccountNumber Query Issue
**Location**: `AWSFunctionBase.cs` lines 940-950

**Problem**: 
The `GetRevAccountNumber` method queries for `RevCustomerId` but the actual customer identification in logs should use the customer GUID consistently.

```sql
SELECT RevCustomerId FROM RevCustomer rc WHERE rc.Id = @customerId
```

This query retrieves the `RevCustomerId` (account number) but error logging should include the actual customer GUID for proper identification.

## Detailed Analysis by Method

### ProcessCustomerId Methods
**Files**: Both optimization files
- ✅ **Correct**: These methods properly get customer GUID and use it correctly in most cases
- ❌ **Issue**: Some error paths use `revAccountNumber` instead of the customer GUID

### ProcessAMOPCustomerId Methods  
**Files**: Both optimization files
- ✅ **Correct**: These methods properly use `amopCustomerId.ToString()` for AMOP customers
- ✅ **Correct**: Correctly handle the case where customer is identified by AMOP ID

### RunCrossProviderCustomerOptimization Method
**File**: `AltaworxSimCardCostQueueCustomerOptimization.cs`
- ❌ **Major Issue**: Uses integer `customerId` (AMOP ID) instead of `customer.CustomerId` (GUID)
- ❌ **Impact**: This is the most critical issue causing missing customer identification

## Fix Implementation

### Fix #1: Correct Cross-Provider Customer ID Usage
**File**: `AltaworxSimCardCostQueueCustomerOptimization.cs`
**Lines**: 750, 767

**Change**:
```csharp
// BEFORE (incorrect):
OptimizationAmopApiTrigger.SendResponseToAMOP20(context, "ErrorMessage", optimizationSessionId.ToString(), null, 0, errorMessage, 0, customerId.ToString(), additionalData);

// AFTER (correct):
OptimizationAmopApiTrigger.SendResponseToAMOP20(context, "ErrorMessage", optimizationSessionId.ToString(), null, 0, errorMessage, 0, customer.CustomerId.ToString(), additionalData);
```

### Fix #2: Ensure Customer GUID is Used Instead of RevAccountNumber
**Files**: Both optimization files
**Lines**: Multiple locations where `revAccountNumber` is used

**Strategy**: 
- Where we have access to the actual customer GUID, use that instead of `revAccountNumber`
- For mobility optimization: Use the `customerId` parameter (which is a GUID)
- For queue optimization: Use the `customerId` parameter (which is a GUID) 

### Fix #3: Add Fallback Handling
**Strategy**: 
- Ensure that if `revAccountNumber` is null/empty, we still send the customer GUID
- Add logging to track when customer identification fails

## Impact Assessment

### High Priority Issues:
1. **Cross-Provider Optimization** (Lines 750, 767): Critical - completely wrong customer identifier
2. **Error Messages with RevAccountNumber**: Medium - may result in empty customer ID if account lookup fails

### Low Priority Issues:
1. **GetRevAccountNumber method**: The SQL query works correctly, but error handling could be improved

## Testing Recommendations

1. **Test Cross-Provider Optimization Errors**: Verify customer.CustomerId is correctly sent to AMOP 2.0
2. **Test Regular Optimization Errors**: Verify customer GUID is sent instead of account number where applicable
3. **Test Edge Cases**: Verify behavior when GetRevAccountNumber returns null/empty
4. **Log Verification**: Ensure all error messages contain proper customer identification

## Files to Modify

1. `AltaworxSimCardCostQueueCustomerOptimization.cs` - Primary fix for cross-provider issue
2. `AltaworxSimCardCostMobilityCustomerOptimization.cs` - Secondary fixes for consistency
3. Consider adding validation/logging to track customer identification issues

## Summary

The primary issue is in the cross-provider optimization error handling where an integer AMOP Customer ID is being used instead of the actual customer GUID. The secondary issue is inconsistent use of revAccountNumber vs customer GUID in error messages. The fixes are straightforward and involve using the correct customer identifier variables that are already available in the scope.