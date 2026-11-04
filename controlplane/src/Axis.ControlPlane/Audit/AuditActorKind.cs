namespace Axis.ControlPlane.Audit;

/// <summary>Who made a change: a person of the billing portal, a provider's webhook, a job, or an operator.</summary>
internal enum AuditActorKind
{
    Account,
    Webhook,
    Job,
    Operator,
}
