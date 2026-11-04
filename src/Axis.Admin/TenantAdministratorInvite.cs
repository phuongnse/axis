using Axis.Data.Storage;
using Axis.Tenancy;
using Axis.Tenancy.Platform;
using Axis.Tenancy.Realms;

namespace Axis.Admin;

/// <summary>
/// Invites the first administrator of a tenant: <c>tenant create --administrator</c> and the <c>invite</c> step of a create job
/// run it. It sets no tenant status, so the caller decides when the tenant becomes active.
/// </summary>
internal static class TenantAdministratorInvite
{
    // Creates the invited account, or finds the one an earlier run created, gives it the administrator role with the audit
    // records and has the realm email the link. The audit records never hold the link, and the output never names the email.
    public static async Task InviteAsync(
        HttpClient http,
        TenantRow row,
        TenantSecretCipher cipher,
        TenantDatabaseSteps steps,
        string email,
        string command,
        CancellationToken cancellationToken)
    {
        var access = DirectoryTenantCatalog.RealmUsers(row, cipher)
            ?? throw new InvalidOperationException("The tenant has no realm to invite an administrator to.");
        var realm = new RealmUsers(http, access);
        var id = await realm.FindAccountIdByEmailAsync(email, cancellationToken);
        var created = id is null;
        id ??= await realm.CreateInvitedAsync(email, null, null, cancellationToken);

        var administrator = new GivenRole(null, UserInvitations.AdministratorKind);
        await using (var connection = await steps.OpenRuntimeConnectionAsync())
        {
            if (created || !(await GivenRoles.ListAsync(connection, id, cancellationToken)).Contains(administrator))
            {
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                await UserInvitations.RecordAsync(transaction, command, id, email, [administrator], cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }

        // A person who has set a password needs no new link. The command names no return address: the realm accepts none yet.
        if (created || !(await realm.ReadInviteeAsync(id, cancellationToken)).HasPassword)
        {
            await realm.SendInvitationAsync(id, null, cancellationToken);
        }
    }
}
