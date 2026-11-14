namespace Axis.Processes.Work;

/// <summary>
/// Counts the emails a tenant sends per UTC day against its <c>emailsPerDay</c> quota. The email handler takes a slot
/// just before it sends an email and gives the slot back when the send fails. Only a cell's worker has one, so
/// outside a cell nothing is counted or held. The first held email of a day takes the tenant's emails-held notice,
/// see <see cref="IHeldNotices"/>.
/// </summary>
public interface IEmailQuota : IHeldNotices
{
    /// <summary>
    /// Takes one slot of the tenant's emails of the current UTC day. <see cref="EmailSlot.Taken"/> is
    /// <see langword="false"/> when the day's quota is used up. The result names the day, which the caller passes to
    /// <see cref="ReturnAsync"/> and uses to find the next 00:00 UTC.
    /// </summary>
    Task<EmailSlot> TakeAsync(string tenantId, CancellationToken cancellationToken);

    /// <summary>Gives back a slot taken on <paramref name="day"/>, because the email was not sent.</summary>
    Task ReturnAsync(string tenantId, DateOnly day, CancellationToken cancellationToken);
}

/// <summary>
/// The result of <see cref="IEmailQuota.TakeAsync"/>: the UTC day it counted on, whether a slot was free, and the
/// tenant's <c>emailsPerDay</c> limit, or <see langword="null"/> when it has none.
/// </summary>
public readonly record struct EmailSlot(DateOnly Day, bool Taken, long? Limit);
