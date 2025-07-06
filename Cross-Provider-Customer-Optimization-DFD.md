# Cross-Provider Customer Optimization - Data Flow Diagram (DFD)

## Level 0 - Context Diagram

```
┌─────────────────┐    Customer Parameters    ┌──────────────────────────────────┐
│   AMOP 2.0      │ ─────────────────────────► │                                  │
│   System        │                            │   Cross-Provider Customer        │
└─────────────────┘                            │   Optimization System            │
                                               │                                  │
┌─────────────────┐    Provider Configurations ├──────────────────────────────────┤
│   Service       │ ─────────────────────────► │                                  │
│   Providers     │◄─────────────────────────── │  - QueueCustomerOptimization     │
│ (Verizon, AT&T, │   Provider Results         │  - SimCardCostOptimizer          │
│  T-Mobile, etc.)│                            │  - SimCardCostOptimizerCleanup   │
└─────────────────┘                            │                                  │
                                               └──────────────────────────────────┘
┌─────────────────┐    Optimization Results                    │
│   Customer      │◄──────────────────────────────────────────┘
│   Stakeholders  │    Cross-Provider Reports
└─────────────────┘
```

## Level 1 - System Overview DFD

```
                    ┌─────────────────┐
                    │   AMOP 2.0      │
                    │   Trigger       │
                    └─────────┬───────┘
                              │ Customer Parameters
                              ▼
    ┌─────────────────────────────────────────────────────────────────┐
    │              1. CROSS-PROVIDER ORCHESTRATION                    │
    │                                                                 │
    │  ┌─────────────────────────────────────────────────────────┐   │
    │  │         QueueCustomerOptimization Lambda                │   │
    │  │                                                         │   │
    │  │  • Multi-Provider Session Management                    │   │
    │  │  • Cross-Provider Customer Validation                   │   │
    │  │  • Multi-Provider Rate Plan Discovery                   │   │
    │  │  • Cross-Provider Queue Generation                      │   │
    │  └─────────────────────────────────────────────────────────┘   │
    └─────────┬───────────────────────────────────────────────────────┘
              │ Cross-Provider Queue Items
              ▼
    ┌─────────────────────────────────────────────────────────────────┐
    │              2. CROSS-PROVIDER OPTIMIZATION                     │
    │                                                                 │
    │  ┌─────────────────────────────────────────────────────────┐   │
    │  │         SimCardCostOptimizer Lambda                     │   │
    │  │                                                         │   │
    │  │  • Multi-Provider Algorithm Processing                  │   │
    │  │  • Cross-Provider Cost Calculation                      │   │
    │  │  • Provider Migration Analysis                          │   │
    │  │  • Cross-Provider Assignment Strategies                 │   │
    │  └─────────────────────────────────────────────────────────┘   │
    └─────────┬───────────────────────────────────────────────────────┘
              │ Cross-Provider Optimization Results
              ▼
    ┌─────────────────────────────────────────────────────────────────┐
    │              3. CROSS-PROVIDER FINALIZATION                     │
    │                                                                 │
    │  ┌─────────────────────────────────────────────────────────┐   │
    │  │      SimCardCostOptimizerCleanup Lambda                 │   │
    │  │                                                         │   │
    │  │  • Multi-Provider Result Compilation                    │   │
    │  │  • Cross-Provider Report Generation                     │   │
    │  │  • Provider Coordination & Cleanup                      │   │
    │  │  • Consolidated Customer Communications                 │   │
    │  └─────────────────────────────────────────────────────────┘   │
    └─────────┬───────────────────────────────────────────────────────┘
              │ Compiled Cross-Provider Results
              ▼
         ┌──────────────┐
         │  Customer    │
         │ Stakeholders │
         └──────────────┘
```

## Level 2 - Detailed Process DFD

### Process 1: Cross-Provider Orchestration (QueueCustomerOptimization)

```
┌─────────────────┐
│   AMOP 2.0      │
│   Trigger       │
└─────────┬───────┘
          │ Customer Parameters
          ▼
    ┌─────────────────┐      Customer Data      ┌────────────────────┐
    │  1.1 Session    │ ────────────────────────►│ D1: Optimization   │
    │  Management &   │◄──────────────────────── │ Session Store      │
    │  Customer       │      Session Info       └────────────────────┘
    │  Validation     │
    └─────────┬───────┘
              │ Validated Customer Data
              ▼
┌──────────────────┐ Provider Rate Plans ┌───────────────────────┐
│ Service Providers│ ──────────────────►  │  1.2 Cross-Provider  │
│ (Verizon, AT&T,  │◄────────────────────│  Rate Plan Discovery  │
│  T-Mobile, etc.) │ Rate Plan Requests  │  & Auto Change        │
└──────────────────┘                     │  Detection            │
                                         └─────────┬─────────────┘
                                                   │ Multi-Provider Rate Plan Status
                                                   ▼
    ┌─────────────────┐      Customer Data      ┌────────────────────┐
    │  1.3 Customer   │ ────────────────────────►│ D2: Customer       │
    │  Validation &   │◄──────────────────────── │ Validation Store   │
    │  Eligibility    │      Validation Status  └────────────────────┘
    │  Check          │
    └─────────┬───────┘
              │ Validated Multi-Provider Customer Data
              ▼
    ┌─────────────────┐   Cross-Provider Pool Data  ┌─────────────────┐
    │  1.4 Cross-     │ ─────────────────────────────►│ D3: Rate Pool   │
    │  Provider Rate  │◄───────────────────────────── │ Generation      │
    │  Pool Generation│      Pool Configurations     │ Store           │
    │  & Calculation  │                              └─────────────────┘
    └─────────┬───────┘
              │ Cross-Provider Rate Pool Data
              ▼
    ┌─────────────────┐   Queue Items Creation    ┌─────────────────────┐
    │  1.5 Multi-     │ ─────────────────────────►│ D4: Optimization    │
    │  Provider Queue │                           │ Queue Store         │
    │  Creation &     │                           │ (SQS/Database)      │
    │  Job Queuing    │                           └─────────────────────┘
    └─────────────────┘
```

### Process 2: Cross-Provider Optimization (SimCardCostOptimizer)

```
┌─────────────────────┐
│ D4: Optimization    │ Cross-Provider Queue Items
│ Queue Store         │ ─────────────────────────┐
└─────────────────────┘                          │
                                                 ▼
    ┌─────────────────┐   Load Customer Data   ┌─────────────────────┐
    │  2.1 Cross-     │ ─────────────────────► │ D5: Customer        │
    │  Provider       │◄─────────────────────  │ Instance Store      │
    │  Customer       │   Customer Instance    └─────────────────────┘
    │  Processing     │
    └─────────┬───────┘
              │ Multi-Provider Customer Data
              ▼
    ┌─────────────────┐   Rate Pool Data      ┌─────────────────────┐
    │  2.2 Cross-     │ ─────────────────────►│ D6: Cross-Provider  │
    │  Provider Rate  │◄───────────────────── │ Rate Pool Store     │
    │  Pool Processing│   Pool Collections    └─────────────────────┘
    └─────────┬───────┘
              │ Cross-Provider Customer Collections
              ▼
    ┌─────────────────┐
    │  2.3 Cross-     │    ┌──────────────────────────────────────┐
    │  Provider       │    │ Assignment Strategies:               │
    │  Assignment     │    │ • Cross-Provider No Grouping        │
    │  Strategy       │    │ • Multi-Provider Communication Plan │
    │  Execution      │    │ • Provider-Specific Optimization    │
    └─────────┬───────┘    └──────────────────────────────────────┘
              │ Strategy Results
              ▼
┌──────────────────┐ Provider Pricing    ┌─────────────────────────┐
│ Service Providers│ ──────────────────► │  2.4 Cross-Provider     │
│ Rate/Cost Data   │◄────────────────────│  Cost Calculation       │
└──────────────────┘ Cost Requests       │  Engine                 │
                                         └─────────┬───────────────┘
                                                   │ Multi-Provider Cost Data
                                                   ▼
    ┌─────────────────┐   Optimization Results  ┌─────────────────────┐
    │  2.5 Cross-     │ ─────────────────────── ►│ D7: Optimization    │
    │  Provider Result│                          │ Results Store       │
    │  Evaluation &   │                          └─────────────────────┘
    │  Storage        │
    └─────────────────┘
```

### Process 3: Cross-Provider Finalization (SimCardCostOptimizerCleanup)

```
┌─────────────────────┐
│ D7: Optimization    │ Cross-Provider Results
│ Results Store       │ ─────────────────────────┐
└─────────────────────┘                          │
                                                 ▼
    ┌─────────────────┐   Queue Status Check   ┌─────────────────────┐
    │  3.1 Cross-     │ ─────────────────────► │ D4: Optimization    │
    │  Provider Queue │◄─────────────────────  │ Queue Store         │
    │  Monitoring &   │   Queue Depths         └─────────────────────┘
    │  Coordination   │
    └─────────┬───────┘
              │ Multi-Provider Completion Status
              ▼
    ┌─────────────────┐   Winning Results      ┌─────────────────────┐
    │  3.2 Cross-     │ ─────────────────────► │ D8: Compiled        │
    │  Provider Result│◄─────────────────────  │ Results Store       │
    │  Compilation &  │   Compiled Statistics  └─────────────────────┘
    │  Aggregation    │
    └─────────┬───────┘
              │ Cross-Provider Compiled Results
              ▼
    ┌─────────────────┐   Report Generation    ┌─────────────────────┐
    │  3.3 Multi-     │ ─────────────────────► │ D9: Report          │
    │  Provider Report│                        │ Generation Store    │
    │  Generation &   │                        │ (Excel/PDF)         │
    │  Documentation  │                        └─────────────────────┘
    └─────────┬───────┘
              │ Multi-Provider Reports
              ▼
    ┌─────────────────┐   Email Delivery       ┌──────────────────┐
    │  3.4 Cross-     │ ─────────────────────► │  Customer        │
    │  Provider Email │                        │  Stakeholders    │
    │  & Reporting    │                        └──────────────────┘
    │  Finalization   │
    └─────────┬───────┘
              │ Processing Updates
              ▼
    ┌─────────────────┐   Cleanup Status       ┌─────────────────────┐
    │  3.5 Cross-     │ ─────────────────────► │ D10: Processing     │
    │  Provider       │◄─────────────────────  │ Tracking Store      │
    │  Processing     │   Processing State     └─────────────────────┘
    │  Cleanup        │
    └─────────────────┘
```

## Data Stores (D1-D10)

### D1: Optimization Session Store
- **Content**: Cross-provider optimization session tracking
- **Access**: QueueCustomerOptimization (R/W)
- **Data**: Session IDs, customer parameters, provider associations, timestamps

### D2: Customer Validation Store
- **Content**: Multi-provider customer validation results
- **Access**: QueueCustomerOptimization (R/W)
- **Data**: Customer eligibility, provider associations, validation status

### D3: Rate Pool Generation Store
- **Content**: Cross-provider rate pool configurations
- **Access**: QueueCustomerOptimization (R/W)
- **Data**: Rate pools, provider mappings, auto-change settings

### D4: Optimization Queue Store (SQS/Database)
- **Content**: Cross-provider optimization work queues
- **Access**: QueueCustomerOptimization (W), SimCardCostOptimizer (R), SimCardCostOptimizerCleanup (R)
- **Data**: Queue items, provider-specific jobs, processing status

### D5: Customer Instance Store
- **Content**: Multi-provider customer optimization instances
- **Access**: SimCardCostOptimizer (R/W)
- **Data**: Customer instances, provider settings, device groups

### D6: Cross-Provider Rate Pool Store
- **Content**: Rate pool data across providers
- **Access**: SimCardCostOptimizer (R/W)
- **Data**: Rate pools, device assignments, provider constraints

### D7: Optimization Results Store
- **Content**: Cross-provider optimization results
- **Access**: SimCardCostOptimizer (W), SimCardCostOptimizerCleanup (R)
- **Data**: Assignment results, cost calculations, provider comparisons

### D8: Compiled Results Store
- **Content**: Aggregated multi-provider results
- **Access**: SimCardCostOptimizerCleanup (R/W)
- **Data**: Winning assignments, statistics, savings summaries

### D9: Report Generation Store
- **Content**: Generated cross-provider reports
- **Access**: SimCardCostOptimizerCleanup (R/W)
- **Data**: Excel files, PDF reports, provider-specific documentation

### D10: Processing Tracking Store
- **Content**: Cross-provider processing state tracking
- **Access**: SimCardCostOptimizerCleanup (R/W)
- **Data**: Processing status, completion tracking, cleanup state

## External Entities

### AMOP 2.0 System
- **Role**: Triggers cross-provider customer optimization requests
- **Data Provided**: Customer parameters, optimization settings, provider specifications
- **Interaction**: Sends SQS messages to initiate cross-provider optimization

### Service Providers (Verizon, AT&T, T-Mobile, etc.)
- **Role**: Provide rate plans, pricing, and service capabilities
- **Data Provided**: Rate plans, pricing structures, service constraints
- **Data Received**: Rate plan requests, optimization results
- **Interaction**: API calls for rate plan data and result submissions

### Customer Stakeholders
- **Role**: Receive optimization results and reports
- **Data Received**: Cross-provider reports, cost savings, provider recommendations
- **Interaction**: Email delivery of optimization results and Excel reports

## Cross-Provider Data Flow Characteristics

### Multi-Provider Coordination
- **Parallel Processing**: Simultaneous optimization across multiple providers
- **Provider Synchronization**: Coordinated completion across all providers
- **Cross-Provider Validation**: Unified validation across provider boundaries

### Provider-Specific Handling
- **Provider Constraints**: Individual provider business rules and limitations
- **Provider Authentication**: Separate authentication for each provider
- **Provider Rate Plans**: Provider-specific rate plan structures and pricing

### Cross-Provider Optimization
- **Cost Comparison**: Real-time cost analysis between providers
- **Migration Analysis**: Cost and benefit analysis for provider changes
- **Consolidated Reporting**: Unified reporting across all providers

This DFD illustrates the comprehensive data flow for the Cross-Provider Customer Optimization system, showing how data moves through the three Lambda functions while handling multiple service providers simultaneously.