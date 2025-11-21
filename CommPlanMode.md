In sim.cost.optimizer’s CommPlan mode, active communication plans without an associated `CustomerId` continue through optimization because every step keys off the communication-plan group rather than customer ownership.

1. `GetCommPlansForCommGroup` only filters by `CommGroupId` and `IsDeleted = 0`, so any non-deleted plan in the group is returned, regardless of customer linkage.

```578:606:AWSFunctionBase.cs
public List<string> GetCommPlansForCommGroup(KeySysLambdaContext context, long commGroupId)
{
    ...
    using (var Cmd = new SqlCommand("SELECT jcp.[Id],jcp.[CommunicationPlanName] FROM [dbo].[OptimizationCommGroup_CommPlan] ocgcp INNER JOIN [dbo].[JasperCommunicationPlan] jcp ON ocgcp.CommPlanId = jcp.Id WHERE ocgcp.CommGroupId = @commGroupId AND jcp.IsDeleted = 0", Conn))
    {
        ...
        while (rdr.Read())
        {
            var commPlan = rdr["CommunicationPlanName"].ToString();
            commPlans.Add(commPlan);
        }
    }
    return commPlans;
}
```

2. Those plan names drive device selection in the optimizer; `GetSimCardsByPortalType` passes the list into `GetSimCards`, which likewise has no `CustomerId` constraint.

```192:224:AltaworxSimCardCostOptimizer.cs
if (isFirstId)
{
    commPlanGroupId = queue.CommPlanGroupId;
    var commPlans = new List<string>();
    if (instance.PortalType == PortalTypes.M2M && !instance.IsCustomerOptimization)
    {
        commPlans = GetCommPlansForCommGroup(context, queue.CommPlanGroupId);
    }
    ...
    simCards = GetSimCardsByPortalType(context, instance, queue.ServiceProviderId, billingPeriod, instance.PortalType, commPlanGroupId, commPlans, optimizationGroups);
}
```

3. The SIM lookup query further shows that devices are filtered by instance/comm-group/service-provider and the optional list of plan names; there is no requirement for a `CustomerId`, allowing “customer-less” plans to stay in scope.

```1276:1353:AWSFunctionBase.cs
public List<SimCard> GetSimCards(KeySysLambdaContext context, long instanceId, int? serviceProviderId, List<string> commPlanNames, BillingPeriod billingPeriod, long commGroupId, bool isCustomerOptimization, bool autoChangeRatePlan = true)
{
    ...
    using (var cmd = new SqlCommand(@"SELECT ... FROM OptimizationDevice 
                                      WHERE [InstanceId] = @instanceId 
                                        AND (@CommGroupId IS NULL OR [OptimizationCommGroupId] = @CommGroupId)
                                        AND (@serviceProviderId IS NULL OR [ServiceProviderId] = @serviceProviderId)
                                        AND [AutoChangeRatePlan] = @autoChangeRatePlan", conn))
    {
        ...
        if (commPlanNames == null || commPlanNames.Count == 0 || commPlanNames.Contains(simCard.CommunicationPlan))
        {
            simCards.Add(simCard);
        }
    }
}
```
