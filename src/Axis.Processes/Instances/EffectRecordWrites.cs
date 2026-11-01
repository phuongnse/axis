using System.Text.Json.Nodes;
using Axis.Configuration.Model;
using Axis.Data.Audit;
using Axis.Data.Records;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Axis.Processes.Instances;

/// <summary>
/// Audits the records that the effects of a transition created and starts their data change
/// triggers, for a write that a process makes: a task completion, an <c>updateRecord</c> step, a
/// connector or package output and a call resolution. Each effect record gets one
/// <c>record.created</c> audit record with the version and the effect's named point, as the record API
/// writes it, and the create triggers on the effect's entity start for it. Both happen in the
/// transaction of the write that made the transition.
/// </summary>
internal static class EffectRecordWrites
{
    /// <summary>
    /// Appends the audit record of each of <paramref name="effects"/> for <paramref name="actorId"/>, linked to
    /// <paramref name="processInstanceId"/>, then starts the create triggers of the <paramref name="active"/> release
    /// for it. The write used the release <paramref name="writeReleaseId"/>. A null <paramref name="active"/>
    /// starts no trigger.
    /// </summary>
    public static async Task AppendAsync(
        NpgsqlTransaction transaction,
        string tenantId,
        ApplicationModel? active,
        Guid activeReleaseId,
        Guid writeReleaseId,
        Guid applicationId,
        Guid? processInstanceId,
        string actorId,
        IReadOnlyList<EffectRecord> effects,
        RecordWriteContext write,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(effects);

        foreach (var effect in effects)
        {
            await AuditRecords.AppendAsync(
                transaction,
                new AuditEntry(
                    actorId,
                    AuditActions.RecordCreated,
                    applicationId,
                    effect.Entity.Id,
                    effect.Record.Id,
                    processInstanceId,
                    new JsonObject { ["version"] = effect.Record.Version, ["effect"] = effect.Point }),
                cancellationToken);
            if (active is not null)
            {
                await DataChangeTriggerStarts.StartAfterCreateAsync(
                    transaction, tenantId, active, activeReleaseId, writeReleaseId, effect.Entity, effect.Record, write, logger, cancellationToken);
            }
        }
    }
}
