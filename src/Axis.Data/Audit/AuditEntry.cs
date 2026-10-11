using System.Text.Json.Nodes;

namespace Axis.Data.Audit;

/// <summary>
/// An audit record to append: who did what, to which application, entity, record and process
/// instance. <see cref="Details"/> depends on the action and never holds field values.
/// </summary>
/// <param name="ActorName">
/// The actor's display name, stored as it is now, or null for <c>system</c> and <c>anonymous</c>.
/// </param>
public sealed record AuditEntry(
    string Actor,
    string Action,
    Guid ApplicationId,
    Guid? EntityId,
    Guid? RecordId,
    Guid? ProcessInstanceId,
    JsonObject Details,
    string? ActorName = null);
