using System.Text.Json.Nodes;
using Axis.Data.Audit;
using Npgsql;

namespace Axis.Data.Storage;

/// <summary>
/// The database part of inviting a person, shared by the invite route of the users page and <c>tenant create --administrator</c>.
/// It gives the roles and writes the audit records in the caller's transaction: a <c>user.roleGiven</c> record for each role
/// that was new, then one <c>user.invited</c> record. No record holds the invitation link.
/// </summary>
public static class UserInvitations
{
    /// <summary>The kind of the administrator platform role.</summary>
    public const string AdministratorKind = "administrator";

    /// <summary>The details of a <c>user.roleGiven</c> or <c>user.roleRemoved</c> record. A platform role says so.</summary>
    public static JsonObject RoleDetails(string userId, Guid? applicationId, string role)
    {
        var details = new JsonObject { ["userId"] = userId, ["role"] = role };
        if (applicationId is null)
        {
            details["platform"] = true;
        }

        return details;
    }

    /// <summary>Gives the invited person the roles and appends the audit records, with <paramref name="actor"/> as their actor.</summary>
    public static async Task RecordAsync(
        NpgsqlTransaction transaction,
        string actor,
        string userId,
        string email,
        IReadOnlyList<GivenRole> roles,
        CancellationToken cancellationToken = default)
    {
        foreach (var role in roles)
        {
            if (await GivenRoles.GiveAsync(transaction, userId, role.ApplicationId, role.Role, cancellationToken))
            {
                await AuditRecords.AppendAsync(
                    transaction,
                    new AuditEntry(actor, AuditActions.UserRoleGiven, role.ApplicationId, null, null, null, RoleDetails(userId, role.ApplicationId, role.Role)),
                    cancellationToken);
            }
        }

        await AuditRecords.AppendAsync(
            transaction,
            new AuditEntry(actor, AuditActions.UserInvited, null, null, null, null, new JsonObject { ["userId"] = userId, ["email"] = email }),
            cancellationToken);
    }
}
