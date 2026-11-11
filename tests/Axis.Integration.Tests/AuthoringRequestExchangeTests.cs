using System.Net;
using Axis.Data.Audit;
using Axis.Data.Storage;
using Axis.Tenancy.Keys;
using Axis.Tenancy.Realms;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using static Axis.Integration.Tests.AuthoringRealm;

namespace Axis.Integration.Tests;

/// <summary>
/// A send of an authoring request gets the agent host's token for the author before it writes anything, see
/// <c>docs/reference/authoring-requests.md#acting-for-the-author</c>. The realm, the author and the server come from <see cref="AuthoringRealm"/>, in a real Keycloak.
/// After a send answers 202, the token stored on the row, once refreshed as <c>axis-agent</c>, opens an MCP session as the author.
/// When the realm user has lost the role, the send answers 403, and when an admin has logged the author out at the realm, it answers 401.
/// Neither writes a request row or an audit record.
/// </summary>
public sealed class AuthoringRequestExchangeTests(KeycloakFixture keycloak, PostgreSqlFixture database) : IAsyncLifetime, IClassFixture<PostgreSqlFixture>
{
    private readonly AuthoringRealm _realm = new(keycloak, database);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_send_stores_a_token_that_once_refreshed_opens_an_mcp_session_as_the_author()
    {
        using var client = await _realm.SignInAsync();

        using var accepted = await StudioPublishTests.SendAsync(client, HttpMethod.Post, Url, Host, Text);

        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        using var sent = await StudioPublishTests.ReadAsync(accepted);
        var id = Guid.Parse(sent.RootElement.GetProperty("id").GetString()!);
        var stored = (byte[]?)await PostgreSqlServer.AdminScalarAsync(
            _realm.SchemaConnection, $"SELECT refresh_token FROM axis.authoring_requests WHERE id = '{id}'", CancellationToken);
        Assert.NotNull(stored);
        var refreshToken = await _realm.Values().DecryptTextAsync(AuthoringRequestTokens.Scope(Tenant, id), AuthoringRequestTokens.FieldName, stored, CancellationToken);

        var (status, refreshed) = await _realm.RefreshAsync(refreshToken);
        Assert.True(status == HttpStatusCode.OK, refreshed.GetRawText());
        var access = refreshed.GetProperty("access_token").GetString()!;
        Assert.Equal(_realm.UserId, new JsonWebToken(access).Subject);

        using var mcp = _realm.Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = false,
        });
        using var initialized = await McpTestCalls.PostAsync(mcp, Host, access, McpTestCalls.Initialize("2025-11-25"), null, null, null, CancellationToken);
        Assert.True(initialized.StatusCode == HttpStatusCode.OK, await initialized.Content.ReadAsStringAsync(CancellationToken));
        Assert.True(initialized.Headers.Contains("Mcp-Session-Id"));
    }

    [Fact]
    public async Task A_send_after_the_realm_user_lost_the_author_role_answers_403_and_writes_no_request_and_no_audit_record()
    {
        using var client = await _realm.SignInAsync();
        await _realm.RemoveAuthorRoleAsync();

        using var refused = await StudioPublishTests.SendAsync(client, HttpMethod.Post, Url, Host, Text);

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        using var problem = await StudioPublishTests.ReadAsync(refused);
        Assert.Equal("Your sign-in no longer holds the author role.", problem.RootElement.GetProperty("title").GetString());
        await AssertNothingWrittenAsync();
    }

    [Fact]
    public async Task A_send_after_an_admin_logged_the_author_out_at_the_realm_answers_401_and_writes_no_request()
    {
        using var client = await _realm.SignInAsync();
        await _realm.LogOutAuthorAsync();

        using var refused = await StudioPublishTests.SendAsync(client, HttpMethod.Post, Url, Host, Text);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        await AssertNothingWrittenAsync();
    }

    public async ValueTask InitializeAsync() => await _realm.InitializeAsync();

    public async ValueTask DisposeAsync() => await _realm.DisposeAsync();

    private async Task AssertNothingWrittenAsync()
    {
        Assert.Equal(0L, await PostgreSqlServer.AdminScalarAsync(_realm.SchemaConnection, "SELECT count(*) FROM axis.authoring_requests", CancellationToken));
        Assert.Equal(
            0L,
            await PostgreSqlServer.AdminScalarAsync(
                _realm.SchemaConnection, $"SELECT count(*) FROM axis.audit_records WHERE action = '{AuditActions.AuthoringRequestSent}'", CancellationToken));
    }
}
