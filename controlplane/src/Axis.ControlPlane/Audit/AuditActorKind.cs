namespace Axis.ControlPlane.Audit;

/// <summary>Who made a change: a person of the billing portal, a provider's webhook, a job, an operator, or the operator's billing system.</summary>
internal enum AuditActorKind
{
    Account,
    Webhook,
    Job,
    Operator,
    Billing,
}
