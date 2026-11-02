using System.Security.Cryptography;
using Axis.Data.Naming;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using NpgsqlTypes;

namespace Axis.Integration.Tests;

/// <summary>
/// One tenant database, <c>a</c>, with the ApprovalsApp fixture activated by <see cref="Steps"/>. The real server
/// serves the active release on host <c>a.example.test</c>. The tenant has a data key, because <c>phone</c> is
/// sensitive. The application declares the built-in modules <c>organization</c> and <c>approvals</c>, which the test
/// project copies to <c>BuiltInModules</c>, maps the slot <c>admin</c> of each to <c>admin</c> and the slot
/// <c>member</c> of each to <c>employee</c>, and has policies of its own that grant access to the entities of
/// <c>approvals</c> within the module's ceilings, and the module's own policies apply too. It adds the process
/// <c>DecideRequest</c>, whose task <c>decide</c> a test completes as another user and whose steps write the request as
/// a task completion or a process step does. The worker is the one of <see cref="Steps"/>, which runs only when a test calls it. The test authentication scheme signs
/// in <c>appr-admin</c>, who holds <c>admin</c> there, and the members <c>appr-ann</c>, <c>appr-bob</c> and
/// <c>appr-carl</c>, who hold <c>employee</c> there. A member's employee record carries the member's id in
/// <c>user</c>, which is unique, so each test uses its own member.
/// </summary>
public sealed class ApprovalsAppFixture : IAsyncLifetime
{
    public const string Host = "a.example.test";

    /// <summary>The <c>id</c> of ApprovalsApp.</summary>
    public static readonly Guid AppId = Guid.Parse("140c35f8-24e6-4c90-80a7-3dadf905e3d9");

    private static readonly string _fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly string[] _members = ["appr-ann", "appr-bob", "appr-carl"];

    private WebApplicationFactory<Program>? _factory;

    /// <summary>The database, the activation and the worker.</summary>
    public ProcessStepFixture Steps { get; } = new(
        ProcessStepFixture.TenantId,
        Path.Combine(_fixtures, "ApprovalsApp"),
        Path.Combine(AppContext.BaseDirectory, "BuiltInModules"));

    /// <summary>A new client of the server with its own cookies. The caller disposes it.</summary>
    public HttpClient CreateClient() =>
        (_factory ?? throw new InvalidOperationException("The test host is not started.")).CreateClient();

    /// <summary>A data source for the tenant database, for SQL checks.</summary>
    public NpgsqlDataSource DataSource => Steps.DataSource;

    public async ValueTask InitializeAsync()
    {
        await Steps.InitializeAsync();
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(Steps.ConnectionString);
            builder.UseSetting($"Tenants:{Steps.Tenant}:Hosts:0", Host);
            builder.UseSetting($"Tenants:{Steps.Tenant}:ConnectionString", PostgreSqlServer.RuntimeConnectionString(Steps.ConnectionString));
            builder.UseSetting($"Tenants:{Steps.Tenant}:SchemaConnectionString", Steps.ConnectionString);
            builder.UseSetting($"Tenants:{Steps.Tenant}:DataKeys:1", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
            builder.UseTestOidc(Steps.Tenant);
            builder.UseTestAuthentication(
                [
                    new TestIdentity("appr-admin", "Approvals Admin") { ApplicationRoles = Roles("admin") },
                    .. _members.Select(id => new TestIdentity(id, id) { ApplicationRoles = Roles("employee") }),
                ]);
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }

        await Steps.DisposeAsync();
    }

    /// <summary>
    /// Inserts a request of the type <paramref name="type"/> with a new title, which skips the contract, and returns
    /// its id. Its number is empty.
    /// </summary>
    public async Task<Guid> InsertRequestAsync(
        Guid type,
        string requester,
        string status,
        int? stepsRequired = null,
        int? step = null,
        string? approverUser = null,
        string? onBehalfOf = null,
        decimal? amount = null)
    {
        var id = Guid.NewGuid();
        await using var command = Steps.DataSource.CreateCommand(
            $"""
            INSERT INTO {Table()}
                ({Column("id")}, {Column("type")}, {Column("title")}, {Column("requester")}, {Column("status")}, {Column("stepsRequired")},
                 {Column("step")}, {Column("approverUser")}, {Column("onBehalfOf")}, {Column("amount")})
            VALUES (@id, @type, @title, @requester, @status, @stepsRequired, @step, @approverUser, @onBehalfOf, @amount)
            """);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("title", $"Request {id:N}");
        command.Parameters.AddWithValue("requester", requester);
        command.Parameters.AddWithValue("status", status);
        command.Parameters.Add(new NpgsqlParameter("stepsRequired", NpgsqlDbType.Bigint) { Value = (object?)stepsRequired ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("step", NpgsqlDbType.Bigint) { Value = (object?)step ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("approverUser", NpgsqlDbType.Text) { Value = (object?)approverUser ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("onBehalfOf", NpgsqlDbType.Text) { Value = (object?)onBehalfOf ?? DBNull.Value });
        command.Parameters.Add(new NpgsqlParameter("amount", NpgsqlDbType.Numeric) { Value = (object?)amount ?? DBNull.Value });
        await command.ExecuteNonQueryAsync();
        return id;
    }

    /// <summary>The status and the step of the request <paramref name="id"/>.</summary>
    public async Task<(string Status, long? Step)> RequestAsync(Guid id)
    {
        await using var command = Steps.DataSource.CreateCommand(
            $"SELECT {Column("status")}, {Column("step")} FROM {Table()} WHERE {Column("id")} = @id");
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), await reader.IsDBNullAsync(1) ? null : reader.GetInt64(1));
    }

    private string Table()
    {
        Assert.True(Steps.Model.TryGetEntity("approvals/Request", out var model));
        return EntityNaming.QualifiedTable(EntityNaming.Table(model.Id));
    }

    private static string Column(string field) =>
        EntityNaming.Quote(field == "id" ? EntityNaming.IdColumn : EntityNaming.Column(field));

    private static Dictionary<Guid, IReadOnlyList<string>> Roles(string role) => new() { [AppId] = [role] };
}
