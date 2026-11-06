namespace Axis.ControlPlane.Billing;

/// <summary>What a ledger entry records. The column holds the lowercase name.</summary>
internal enum LedgerEntryKind
{
    Charge,
    Credit,
    Payment,
    Refund,
    Proration,
}
