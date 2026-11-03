using System.Text;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Records;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A create and an update set a matched reference to the target record whose unique field equals the record's <c>from</c> value,
/// or to <c>null</c> when <c>from</c> is empty or no target matches. The lookup reads as the system, so the actor needs no grant
/// on the target entity. The record API and the data layer's <c>CreateFromAsync</c> and <c>SetAsync</c> run the same step.
/// </summary>
public sealed class MatchedReferenceWriteTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private const string Reference = "requesterEmployee";

    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _employeeId = Guid.NewGuid();
    private readonly Guid _requestId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";
    private readonly string _tenant = $"t{Guid.NewGuid():N}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_create_sets_the_reference_an_update_moves_it_and_no_match_or_a_null_from_gives_null()
    {
        var (compiled, employee, request, connection) = await ProvisionAsync();
        await using var _ = connection;
        var ann = await InsertEmployeeAsync(connection, compiled, employee, "ann");
        var bob = await InsertEmployeeAsync(connection, compiled, employee, "bob");

        var created = await CreateAsync(connection, compiled, request, """{ "values": { "requester": "ann", "note": "first" } }""");

        Assert.Equal(RecordCommandOutcome.Written, created.Outcome);
        var id = created.Record!.Id;
        Assert.Equal(ann.ToString(), await ReadAsync(connection, compiled, request, id));

        var moved = await UpdateAsync(connection, compiled, request, id, """{ "version": 1, "values": { "requester": "bob" } }""");

        Assert.Equal(RecordCommandOutcome.Written, moved.Outcome);
        Assert.Equal(bob.ToString(), await ReadAsync(connection, compiled, request, id));

        var unknown = await UpdateAsync(connection, compiled, request, id, """{ "version": 2, "values": { "requester": "nobody" } }""");

        Assert.Equal(RecordCommandOutcome.Written, unknown.Outcome);
        Assert.Null(await ReadAsync(connection, compiled, request, id));

        var back = await UpdateAsync(connection, compiled, request, id, """{ "version": 3, "values": { "requester": "ann" } }""");
        Assert.Equal(RecordCommandOutcome.Written, back.Outcome);
        Assert.Equal(ann.ToString(), await ReadAsync(connection, compiled, request, id));

        var cleared = await UpdateAsync(connection, compiled, request, id, """{ "version": 4, "values": { "requester": null } }""");

        Assert.Equal(RecordCommandOutcome.Written, cleared.Outcome);
        Assert.Null(await ReadAsync(connection, compiled, request, id));

        var none = await CreateAsync(connection, compiled, request, """{ "values": { "requester": "carol" } }""");

        Assert.Equal(RecordCommandOutcome.Written, none.Outcome);
        Assert.Null(await ReadAsync(connection, compiled, request, none.Record!.Id));
    }

    [Fact]
    public async Task An_update_without_from_in_the_body_matches_the_stored_from()
    {
        var (compiled, employee, request, connection) = await ProvisionAsync();
        await using var _ = connection;

        var created = await CreateAsync(connection, compiled, request, """{ "values": { "requester": "carol", "note": "first" } }""");

        Assert.Equal(RecordCommandOutcome.Written, created.Outcome);
        var id = created.Record!.Id;
        Assert.Null(await ReadAsync(connection, compiled, request, id));

        var carol = await InsertEmployeeAsync(connection, compiled, employee, "carol");
        var updated = await UpdateAsync(connection, compiled, request, id, """{ "version": 1, "values": { "note": "second" } }""");

        Assert.Equal(RecordCommandOutcome.Written, updated.Outcome);
        Assert.Equal(carol.ToString(), await ReadAsync(connection, compiled, request, id));
    }

    [Fact]
    public async Task The_data_layer_create_and_set_match_the_reference()
    {
        var (compiled, employee, request, connection) = await ProvisionAsync();
        await using var _ = connection;
        var ann = await InsertEmployeeAsync(connection, compiled, employee, "ann");
        var bob = await InsertEmployeeAsync(connection, compiled, employee, "bob");
        var requester = request.Fields.First(field => field.Name == "requester");

        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var now = await RecordQueries.TransactionTimeAsync(connection, CancellationToken);
        var created = await RecordCommands.CreateFromAsync(
            connection, compiled.Model!, request, compiled.Release!.Id, RecordWriteContext.None, [new(requester, "ann")], null, now, cancellationToken: CancellationToken);

        Assert.Equal(RecordWriteOutcome.Written, created.Outcome);
        var id = created.Record!.Id;
        Assert.Equal(ann.ToString(), (await RecordQueries.GetAsync(connection, compiled.Model!, request, id, null, CancellationToken))!.Values[Reference]!.GetValue<string>());

        Assert.True(await RecordCommands.LockAsync(connection, request, id, CancellationToken));
        var stored = (await RecordQueries.GetAsync(connection, compiled.Model!, request, id, null, CancellationToken))!;
        var set = await RecordCommands.SetAsync(
            connection, compiled.Model!, request, compiled.Release.Id, RecordWriteContext.None, stored, [new(requester, "bob")], now, cancellationToken: CancellationToken);

        Assert.Equal(RecordWriteOutcome.Written, set.Outcome);
        await transaction.CommitAsync(CancellationToken);
        Assert.Equal(bob.ToString(), await ReadAsync(connection, compiled, request, id));
    }

    private static async Task<Guid> InsertEmployeeAsync(NpgsqlConnection connection, ReleaseCompilationResult compiled, EntityModel employee, string user)
    {
        await using var transaction = await connection.BeginTransactionAsync(CancellationToken);
        var result = await RecordCommands.CreateAsync(
            connection,
            compiled.Model!,
            employee,
            compiled.Release!.Id,
            RecordWriteContext.None,
            [new RecordValue(employee.Fields.First(field => field.Name == "user"), user), new RecordValue(employee.Fields.First(field => field.Name == "name"), user)],
            [],
            cancellationToken: CancellationToken);
        Assert.Equal(RecordWriteOutcome.Written, result.Outcome);
        await transaction.CommitAsync(CancellationToken);
        return result.Record!.Id;
    }

    private static async Task<string?> ReadAsync(NpgsqlConnection connection, ReleaseCompilationResult compiled, EntityModel request, Guid id) =>
        (await RecordQueries.GetAsync(connection, compiled.Model!, request, id, null, CancellationToken))!.Values[Reference]?.GetValue<string>();

    private async Task<(ReleaseCompilationResult Compiled, EntityModel Employee, EntityModel Request, NpgsqlConnection Connection)> ProvisionAsync()
    {
        using var folder = Folder();
        ReleaseCompilationResult compiled;
        await using (var context = database.CreateConfigurationContext())
        {
            compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        }

        Assert.Empty(compiled.Diagnostics);
        Assert.NotNull(compiled.Release);
        var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using (var configuration = DataDatabaseFixture.CreateConfigurationContext(connection))
        await using (var data = DataDatabaseFixture.CreateContext(connection))
        {
            var result = await ReleaseActivator.ActivateAsync(
                compiled, new ActiveReleaseStore(configuration), data, ActivationBindings.None(_tenant), OpenInstanceCounter.Instance, 10, CancellationToken);
            Assert.Empty(result.Diagnostics);
        }

        Assert.True(compiled.Model!.TryGetEntity("Employee", out var employee));
        Assert.True(compiled.Model.TryGetEntity("Request", out var request));
        return (compiled, employee, request, connection);
    }

    private Task<RecordCommandResult> CreateAsync(NpgsqlConnection connection, ReleaseCompilationResult compiled, EntityModel request, string body) =>
        RecordCommand.CreateAsync(
            connection, _tenant, compiled.Model!, compiled.Release!.Id, request, new RecordActor("requester", ["clerk"]), Body(body), null, null, NullLoggerFactory.Instance, CancellationToken);

    private Task<RecordCommandResult> UpdateAsync(NpgsqlConnection connection, ReleaseCompilationResult compiled, EntityModel request, Guid id, string body) =>
        RecordCommand.UpdateAsync(
            connection, _tenant, compiled.Model!, compiled.Release!.Id, request, id, new RecordActor("requester", ["clerk"]), Body(body), null, null, NullLoggerFactory.Instance, CancellationToken);

    private static RecordBodyReader Body(string json) => _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(json));

    private TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With(
                "entities/employee.json",
                $$"""
                { "id": "{{_employeeId}}", "kind": "entity", "name": "Employee", "formatVersion": 1, "displayField": "name", "fields": [
                    { "name": "user", "type": "text", "unique": true },
                    { "name": "name", "type": "text", "required": true } ] }
                """)
            .With(
                "entities/request.json",
                $$"""
                { "id": "{{_requestId}}", "kind": "entity", "name": "Request", "formatVersion": 1, "fields": [
                    { "name": "requester", "type": "text" },
                    { "name": "note", "type": "text" },
                    { "name": "requesterEmployee", "type": "reference", "target": "Employee", "match": { "field": "user", "from": "requester" } } ] }
                """)
            .With("roles/clerk.json", $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "role", "name": "clerk", "formatVersion": 1, "label": { "textKey": "role.clerk" } }""")
            .With(
                "texts/en.json",
                $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en", "texts": { "role.clerk": "Clerk" } }""")
            .With(
                "policies/clerk-access.json",
                $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "policy", "name": "ClerkAccess", "formatVersion": 1, "roles": ["clerk"], "grants": [
                    { "name": "requestAccess", "entity": "Request", "actions": ["read", "create", "update"] }] }
                """);
}
