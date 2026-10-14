using System.Text.Json.Nodes;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Processes.Instances;

/// <summary>
/// Writes the event occurrences that <c>raiseEvent</c> steps raise over a connection to a tenant
/// database, and reads one back for its delivery. An occurrence is stored even when no trigger listens.
/// </summary>
internal static class ProcessEvents
{
    /// <summary>Inserts <paramref name="occurrence"/>. It is raised at the transaction's time.</summary>
    internal static async Task InsertAsync(NpgsqlTransaction transaction, NewEvent occurrence, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO axis.events
                (id, application_id, event, release_id, entity_id, subject_id, payload, process_instance_id, raised_at)
            VALUES (@id, @application, @event, @release, @entity, @subject, @payload, @instance, now())
            """,
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("id", occurrence.Id);
        command.Parameters.AddWithValue("application", occurrence.ApplicationId);
        command.Parameters.AddWithValue("event", occurrence.Event);
        command.Parameters.AddWithValue("release", occurrence.ReleaseId);
        command.Parameters.AddWithValue("entity", occurrence.EntityId);
        command.Parameters.AddWithValue("subject", occurrence.SubjectId);
        command.Parameters.Add(new NpgsqlParameter("payload", NpgsqlDbType.Jsonb) { Value = occurrence.Payload.ToJsonString() });
        command.Parameters.AddWithValue("instance", occurrence.ProcessInstanceId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The read of one occurrence by its id, with its parameter <c>@id</c>.</summary>
    internal const string LoadSql = "SELECT application_id, event, entity_id, subject_id, payload::text FROM axis.events WHERE id = @id";

    /// <summary>Returns the occurrence <paramref name="id"/>, or <see langword="null"/> when there is none.</summary>
    internal static async Task<StoredEvent?> LoadAsync(NpgsqlTransaction transaction, Guid id, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            LoadSql,
            transaction.Connection ?? throw new ArgumentException("The transaction is completed.", nameof(transaction)),
            transaction);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StoredEvent(
            id,
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetGuid(2),
            reader.GetGuid(3),
            JsonNode.Parse(reader.GetString(4)) as JsonObject ?? []);
    }
}

/// <summary>An event occurrence that a <c>raiseEvent</c> step raises about its subject record.</summary>
/// <param name="Id">The event's id, a version 7 UUID.</param>
/// <param name="Event">The event's declared name.</param>
/// <param name="ReleaseId">The release the raising instance is pinned to.</param>
/// <param name="EntityId">The event's entity.</param>
/// <param name="SubjectId">The record the occurrence names.</param>
/// <param name="Payload">The evaluated payload, keyed by the payload fields' declared names.</param>
internal sealed record NewEvent(
    Guid Id,
    Guid ApplicationId,
    string Event,
    Guid ReleaseId,
    Guid EntityId,
    Guid SubjectId,
    JsonObject Payload,
    Guid ProcessInstanceId);

/// <summary>A stored event occurrence, as its delivery reads it.</summary>
/// <param name="Event">The event's declared name, as the release that raised it declares it.</param>
/// <param name="EntityId">The event's entity.</param>
/// <param name="SubjectId">The record the occurrence names.</param>
/// <param name="Payload">The stored payload, keyed by the payload fields' declared names.</param>
internal sealed record StoredEvent(Guid Id, Guid ApplicationId, string Event, Guid EntityId, Guid SubjectId, JsonObject Payload);
