using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Axis.Server.Tenancy;

namespace Axis.Server.Authentication;

/// <summary>
/// Writes the audit records of sign-in and sign-out in the database of the tenant that the host
/// resolves to. A record holds the shortened client address after the forwarded headers rules and
/// never a token, a cookie or the user agent.
/// </summary>
internal static class SessionAudit
{
    /// <summary>Appends one session audit record and commits it in its own transaction.</summary>
    /// <param name="reason">The refusal reason of a <c>session.signInRefused</c> record, otherwise null.</param>
    public static async Task AppendAsync(
        HttpContext httpContext,
        string action,
        string actor,
        string? actorName,
        string? reason,
        CancellationToken cancellationToken)
    {
        var database = httpContext.RequestServices.GetRequiredService<TenantDatabase>();
        var connection = await database.GetConnectionAsync(cancellationToken);

        var details = new JsonObject();
        if (reason is not null)
        {
            details["reason"] = reason;
        }

        details["clientAddress"] = ClientAddress.Shorten(httpContext.Connection.RemoteIpAddress);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(actor, action, null, null, null, null, details, actorName),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
