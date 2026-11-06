using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Data.Profiles;
using Axis.Server.Tenancy;
using Axis.Server.Users;
using Microsoft.Net.Http.Headers;
using Npgsql;

namespace Axis.Server.Studio;

/// <summary>
/// The routes of the application settings that live in the tenant database and in no resource file.
/// <c>GET /api/studio/apps/{application}/settings</c> answers <c>{ autoAccept }</c> to any author, and <c>false</c> for
/// a folder whose manifest has no UUID <c>id</c>. <c>PUT</c> on the same path changes it and answers the new value. The
/// checks run in the order of the rest of the authoring API: session (401), author role (403), publisher role (403), the
/// application (404), the manifest <c>id</c> (409), the content type (415), then the body (400): a JSON object with
/// only a boolean <c>autoAccept</c>. A change that sets the value the application already has writes nothing. A real
/// change and its <c>application.autoAcceptChanged</c> audit record are saved in one transaction. Only a browser
/// session reaches these routes: a bearer token is never a session, so it answers 401.
/// </summary>
internal static class StudioSettingsEndpoints
{
    public static void MapStudioSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/studio/apps/{application}/settings", GetAsync);
        endpoints.MapPut("/api/studio/apps/{application}/settings", PutAsync);
    }

    private static async Task<IResult> GetAsync(
        string application, HttpContext httpContext, StudioOptions options, TenantDatabase database)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioFolder.ReadManifest(folder) is not { Id: { } applicationId })
        {
            return Results.Ok(new StudioSettingsResponse(false));
        }

        var cancellationToken = httpContext.RequestAborted;
        var connection = await database.GetConnectionAsync(cancellationToken);
        return Results.Ok(new StudioSettingsResponse(await ReadAutoAcceptAsync(connection, null, applicationId, cancellationToken)));
    }

    private static async Task<IResult> PutAsync(
        string application, HttpContext httpContext, StudioOptions options, TenantDatabase database)
    {
        var (folder, refusal) = await StudioEndpoints.FindFolderAsync(application, httpContext, options, publisher: true);
        if (folder is null)
        {
            return refusal!;
        }

        if (StudioFolder.ReadManifest(folder) is not { Id: { } applicationId })
        {
            return StudioEndpoints.Problem(StatusCodes.Status409Conflict, "The application manifest has no valid id.");
        }

        var request = httpContext.Request;
        if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var mediaType)
            || !mediaType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
        {
            return StudioEndpoints.Problem(StatusCodes.Status415UnsupportedMediaType, "The body must be application/json.");
        }

        bool value;
        try
        {
            var body = await JsonNode.ParseAsync(request.Body, cancellationToken: httpContext.RequestAborted) as JsonObject
                ?? throw new JsonException();
            if (!TryReadBody(body, out value))
            {
                return BadBody();
            }
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            return BadBody();
        }

        var user = (await CurrentUsers.FindAsync(httpContext))!;
        var cancellationToken = httpContext.RequestAborted;
        var connection = await database.GetConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        if (await ReadAutoAcceptAsync(connection, transaction, applicationId, cancellationToken) != value)
        {
            await using var upsert = new NpgsqlCommand(
                """
                INSERT INTO axis.application_settings (application_id, auto_accept) VALUES (@application, @value)
                ON CONFLICT (application_id) DO UPDATE SET auto_accept = excluded.auto_accept
                WHERE axis.application_settings.auto_accept IS DISTINCT FROM excluded.auto_accept
                """,
                connection,
                transaction);
            upsert.Parameters.AddWithValue("application", applicationId);
            upsert.Parameters.AddWithValue("value", value);

            // Two changes at once can both see the old value. Only the one that changes the row writes the record.
            if (await upsert.ExecuteNonQueryAsync(cancellationToken) == 1)
            {
                await AuditRecords.AppendAsync(
                    transaction,
                    new AuditEntry(
                        user.Id,
                        AuditActions.ApplicationAutoAcceptChanged,
                        applicationId,
                        null,
                        null,
                        null,
                        new JsonObject { ["autoAccept"] = value }),
                    cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new StudioSettingsResponse(value));
    }

    // No row means the default: off.
    private static async Task<bool> ReadAutoAcceptAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid applicationId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT auto_accept FROM axis.application_settings WHERE application_id = @application", connection, transaction);
        command.Parameters.AddWithValue("application", applicationId);
        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    // Exactly one key, autoAccept, with a boolean.
    private static bool TryReadBody(JsonObject body, out bool value)
    {
        value = false;
        return body.Count == 1
            && body["autoAccept"] is JsonValue json
            && json.TryGetValue(out value);
    }

    private static IResult BadBody() =>
        StudioEndpoints.Problem(StatusCodes.Status400BadRequest, "The body must be an object with a boolean autoAccept.");
}

/// <summary>The application settings that live in the tenant database.</summary>
internal sealed record StudioSettingsResponse(bool AutoAccept);
