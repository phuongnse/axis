using System.Net.Http.Json;
using System.Text.Json;

namespace Axis.Tenancy.Realms;

/// <summary>
/// Switches the deny step of a tenant realm's first-login flow (<see cref="RealmSync.FirstLoginFlowAlias"/>) on or off by the
/// number of the realm's users. While the step is on, a person who signs in through a brokered provider for the first time is
/// refused with the message <see cref="RefusalMessageKey"/> and no user is created, and a person who already has a user still signs in.
/// It counts the enabled users as <c>axis-users</c> and switches the step as <c>axis-activation</c>, both with the client credentials grant.
/// An error names the step and the status code, never a response body or a secret.
/// </summary>
public sealed class RealmUsersQuota(HttpClient http)
{
    /// <summary>The Keycloak provider of the deny step.</summary>
    public const string DenyAccessProvider = "deny-access-authenticator";

    /// <summary>The key of the refusal text in the Axis sign-in theme.</summary>
    public const string RefusalMessageKey = "axisUsersQuotaReached";

    /// <summary>The key of the deny step's configuration that names its message.</summary>
    public const string DenyMessageConfigKey = "denyErrorMessage";

    /// <summary>
    /// Counts the realm's enabled users and switches the deny step on when the count is at or past <paramref name="limit"/>,
    /// and off otherwise. A <c>null</c> limit means no limit, so the step goes off and nothing is counted. Returns whether the step is now on.
    /// </summary>
    /// <exception cref="InvalidOperationException">An identity provider step answered an error status, or the realm's flow has no deny step.</exception>
    public async Task<bool> RefreshAsync(TenantRealmAccess realm, TenantRealmUsers users, long? limit, CancellationToken cancellationToken)
    {
        var on = false;
        if (limit is { } max)
        {
            on = await new RealmUsers(http, users).CountEnabledAsync(cancellationToken) >= max;
        }

        var sync = new RealmSync(http, realm.BaseAddress);
        var token = await sync.ActivationTokenAsync(realm.Name, realm.ActivationClientSecret.Reveal(), cancellationToken);
        var path = $"admin/realms/{Uri.EscapeDataString(realm.Name)}/authentication/flows/{Uri.EscapeDataString(RealmSync.FirstLoginFlowAlias)}/executions";
        using var read = await sync.SendAsync(token, HttpMethod.Get, path, null, cancellationToken);
        RealmSync.EnsureSuccess(read, "read the first-login flow");
        var deny = (await read.Content.ReadFromJsonAsync<JsonElement>(cancellationToken)).EnumerateArray()
            .Cast<JsonElement?>()
            .FirstOrDefault(execution => execution!.Value.TryGetProperty("providerId", out var provider) && provider.GetString() == DenyAccessProvider)
            ?? throw new InvalidOperationException("The identity provider step 'find the deny step' found no execution.");

        var wanted = on ? "REQUIRED" : "DISABLED";
        if (deny.TryGetProperty("requirement", out var requirement) && requirement.GetString() == wanted)
        {
            return on;
        }

        var body = deny.Deserialize<Dictionary<string, JsonElement>>()!;
        body["requirement"] = JsonSerializer.SerializeToElement(wanted);
        using var updated = await sync.SendAsync(token, HttpMethod.Put, path, body, cancellationToken);
        RealmSync.EnsureSuccess(updated, on ? "switch on the users quota step" : "switch off the users quota step");
        return on;
    }
}
