using System.Security.Cryptography;
using System.Text;
using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.DataSources;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Instances;
using Axis.Processes.Records;
using Axis.Tenancy.Keys;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Axis.Integration.Tests;

/// <summary>
/// A policy grant's filter <c>nationalId == '…'</c> or <c>!= '…'</c> on an exact lookup field runs against the stored lookup
/// hashes, in the record list and in the single-record match. So does a data source's filter or a measure's filter, against a
/// literal or a data source parameter, in the rows and in the trigger fan-out's id read. A filter that needs the tenant's
/// lookup keys and gets none throws.
/// </summary>
public sealed class ExactLookupFilterTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    private const string Matching = "079-123";
    private const string User = "someone";

    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _personId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";
    private readonly string _tenant = $"t{Guid.NewGuid():N}";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_record_list_under_an_equality_grant_returns_only_the_record_with_that_value()
    {
        var cipher = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;
        var ids = await CreateRecordsAsync(connection, compiled, person, cipher);

        var listed = await ListAsync(connection, compiled, person, cipher, "matcher");

        Assert.Equal([ids.Matching], listed);
    }

    [Fact]
    public async Task The_record_list_under_an_inequality_grant_returns_the_others_including_an_empty_value()
    {
        var cipher = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;
        var ids = await CreateRecordsAsync(connection, compiled, person, cipher);

        var listed = await ListAsync(connection, compiled, person, cipher, "other");

        Assert.Equal(new HashSet<Guid> { ids.Other, ids.Empty }, [.. listed]);
    }

    [Fact]
    public async Task A_record_written_under_an_older_lookup_key_still_matches_after_a_newer_key_is_added()
    {
        var older = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;
        var ids = await CreateRecordsAsync(connection, compiled, person, older);
        var rotated = Cipher(1, 2);
        var newer = await CreateAsync(connection, compiled, person, rotated, $$"""{ "values": { "name": "Newer", "nationalId": "{{Matching}}" } }""");

        var listed = await ListAsync(connection, compiled, person, rotated, "matcher");

        Assert.Equal(new HashSet<Guid> { ids.Matching, newer.Record!.Id }, [.. listed]);
    }

    [Fact]
    public async Task The_single_record_match_under_an_equality_grant_finds_the_record_with_that_value_and_not_another()
    {
        var cipher = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;
        var ids = await CreateRecordsAsync(connection, compiled, person, cipher);
        var filters = Filters(compiled, person, "matcher");

        var matching = await RecordFilters.MatchAsync(connection, null, compiled.Model!, person, ids.Matching, filters, User, cipher, CancellationToken);
        var other = await RecordFilters.MatchAsync(connection, null, compiled.Model!, person, ids.Other, filters, User, cipher, CancellationToken);
        var empty = await RecordFilters.MatchAsync(connection, null, compiled.Model!, person, ids.Empty, filters, User, cipher, CancellationToken);

        Assert.True(matching is { Found: true, Matched: true });
        Assert.True(other is { Found: true, Matched: false });
        Assert.True(empty is { Found: true, Matched: false });
    }

    [Fact]
    public async Task A_filter_on_an_exact_lookup_field_without_a_cipher_throws_and_never_compares_plain_text()
    {
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;
        var filters = Filters(compiled, person, "matcher");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RecordFilters.ConditionAsync(compiled.Model!, person, filters, User, null, CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RecordFilters.MatchAsync(connection, null, compiled.Model!, person, Guid.NewGuid(), filters, User, null, CancellationToken));
    }

    [Fact]
    public async Task A_tenant_without_a_lookup_key_matches_nothing_for_equality_and_everything_for_inequality()
    {
        var withKey = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;
        var ids = await CreateRecordsAsync(connection, compiled, person, withKey);
        var withoutKey = Cipher();

        Assert.Empty(await ListAsync(connection, compiled, person, withKey, "matcher", withoutKey));
        Assert.Equal(
            new HashSet<Guid> { ids.Matching, ids.Other, ids.Empty },
            [.. await ListAsync(connection, compiled, person, withKey, "other", withoutKey)]);
    }

    [Fact]
    public async Task A_data_source_equality_on_a_parameter_returns_the_record_with_that_value_under_any_lookup_key()
    {
        var older = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;
        var ids = await CreateRecordsAsync(connection, compiled, person, older);

        Assert.Equal([ids.Matching], await RowIdsAsync(connection, compiled, "PeopleEqual", older, Matching));

        var rotated = Cipher(1, 2);
        var newer = await CreateAsync(connection, compiled, person, rotated, $$"""{ "values": { "name": "Newer", "nationalId": "{{Matching}}" } }""");

        Assert.Equal(
            new HashSet<Guid> { ids.Matching, newer.Record!.Id },
            [.. await RowIdsAsync(connection, compiled, "PeopleEqual", rotated, Matching)]);
    }

    [Fact]
    public async Task A_data_source_inequality_on_a_parameter_returns_the_other_records_including_an_empty_value()
    {
        var cipher = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;
        var ids = await CreateRecordsAsync(connection, compiled, person, cipher);

        var rows = await RowIdsAsync(connection, compiled, "PeopleNotEqual", cipher, Matching);

        Assert.Equal(new HashSet<Guid> { ids.Other, ids.Empty }, [.. rows]);
    }

    [Fact]
    public async Task A_data_source_equality_without_the_parameter_returns_only_the_record_with_an_empty_value()
    {
        var cipher = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;
        var ids = await CreateRecordsAsync(connection, compiled, person, cipher);

        Assert.Equal([ids.Empty], await RowIdsAsync(connection, compiled, "PeopleEqual", cipher, null));
        Assert.Equal(
            new HashSet<Guid> { ids.Matching, ids.Other },
            [.. await RowIdsAsync(connection, compiled, "PeopleNotEqual", cipher, null)]);
    }

    [Fact]
    public async Task A_data_source_equality_on_a_literal_returns_the_record_with_that_value()
    {
        var cipher = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;
        var ids = await CreateRecordsAsync(connection, compiled, person, cipher);

        Assert.Equal([ids.Matching], await RowIdsAsync(connection, compiled, "PeopleLiteral", cipher, null));
    }

    [Fact]
    public async Task A_measure_filter_on_a_parameter_counts_only_the_records_with_that_value()
    {
        var cipher = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;
        await CreateRecordsAsync(connection, compiled, person, cipher);
        Assert.True(compiled.Model!.TryGetDataSource("PeopleMatching", out var dataSource));

        var page = await DataSourceQueries.ListAsync(
            connection, null, compiled.Model, dataSource, 1, 50, null, [new(dataSource.Parameters[0], Matching)], null, null, cipher, CancellationToken);

        Assert.NotNull(page);
        var counts = page.Items.ToDictionary(row => row.Values["name"]!.GetValue<string>(), row => row.Values["matching"]!.GetValue<long>());
        Assert.Equal(new Dictionary<string, long> { ["Match"] = 1, ["Other"] = 0, ["Empty"] = 0 }, counts);
    }

    [Fact]
    public async Task A_data_source_read_by_id_after_a_cursor_applies_the_lookup_filter_and_needs_a_cipher()
    {
        var cipher = Cipher(1);
        var (compiled, person, connection) = await ProvisionAsync();
        await using var _ = connection;
        var ids = await CreateRecordsAsync(connection, compiled, person, cipher);
        Assert.True(compiled.Model!.TryGetDataSource("PeopleEqual", out var dataSource));
        DataSourceParameterValue[] parameters = [new(dataSource.Parameters[0], Matching)];

        var listed = await DataSourceQueries.ListIdsAfterAsync(connection, compiled.Model, dataSource, parameters, null, 50, cipher, CancellationToken);

        Assert.Equal([ids.Matching], listed);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DataSourceQueries.ListIdsAfterAsync(connection, compiled.Model, dataSource, parameters, null, 50, null, CancellationToken));
    }

    [Fact]
    public async Task A_data_source_read_of_rows_without_a_cipher_throws_for_a_lookup_filter_and_never_compares_plain_text()
    {
        var (compiled, _, connection) = await ProvisionAsync();
        await using var _ = connection;
        Assert.True(compiled.Model!.TryGetDataSource("PeopleEqual", out var dataSource));
        DataSourceParameterValue[] parameters = [new(dataSource.Parameters[0], Matching)];

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => DataSourceQueries.ListAsync(connection, null, compiled.Model, dataSource, 1, 50, null, parameters, null, null, null, CancellationToken));
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in DataSourceQueries.ReadAllAsync(connection, compiled.Model, dataSource, null, parameters, null, null, 50, null, CancellationToken))
            {
            }
        });
    }

    private static async Task<List<Guid>> RowIdsAsync(
        NpgsqlConnection connection, ReleaseCompilationResult compiled, string name, RecordCipher cipher, string? parameter)
    {
        Assert.True(compiled.Model!.TryGetDataSource(name, out var dataSource));
        var page = await DataSourceQueries.ListAsync(
            connection, null, compiled.Model, dataSource, 1, 50, null, [.. dataSource.Parameters.Select(declared => new DataSourceParameterValue(declared, parameter))], null, null, cipher, CancellationToken);
        Assert.NotNull(page);
        return [.. page.Items.Select(row => row.Id!.Value)];
    }

    private static IReadOnlyList<ExpressionModel> Filters(ReleaseCompilationResult compiled, EntityModel person, string role)
    {
        var access = PolicyEvaluator.Access(compiled.Model!, [role], person, PolicyAction.Read);
        Assert.True(access.Granted);
        Assert.NotNull(access.Filters);
        return [.. access.Filters.Select(filter => filter.Expression)];
    }

    // The condition is built with conditionCipher, which hashes the literal, and the page is read with cipher, which decrypts.
    private static async Task<List<Guid>> ListAsync(
        NpgsqlConnection connection,
        ReleaseCompilationResult compiled,
        EntityModel person,
        RecordCipher cipher,
        string role,
        RecordCipher? conditionCipher = null)
    {
        var filters = Filters(compiled, person, role);
        var condition = await RecordFilters.ConditionAsync(compiled.Model!, person, filters, User, conditionCipher ?? cipher, CancellationToken);
        var page = await RecordQueries.ListAsync(connection, null, person, 1, 50, null, null, condition, cipher, CancellationToken);
        Assert.NotNull(page);
        return [.. page.Items.Select(record => record.Id)];
    }

    private async Task<(Guid Matching, Guid Other, Guid Empty)> CreateRecordsAsync(
        NpgsqlConnection connection, ReleaseCompilationResult compiled, EntityModel person, RecordCipher cipher)
    {
        var matching = await CreateAsync(connection, compiled, person, cipher, $$"""{ "values": { "name": "Match", "nationalId": "{{Matching}}" } }""");
        var other = await CreateAsync(connection, compiled, person, cipher, """{ "values": { "name": "Other", "nationalId": "999-000" } }""");
        var empty = await CreateAsync(connection, compiled, person, cipher, """{ "values": { "name": "Empty" } }""");
        return (matching.Record!.Id, other.Record!.Id, empty.Record!.Id);
    }

    // The tenant holds data key 1 and a random lookup key for each number in lookupKeys, none when it is empty.
    private readonly Dictionary<int, string> _lookupKeys = [];
    private readonly string _dataKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private RecordCipher Cipher(params int[] lookupKeys)
    {
        var settings = new Dictionary<string, string?> { [$"Tenants:{_tenant}:DataKeys:1"] = _dataKey };
        foreach (var number in lookupKeys)
        {
            if (!_lookupKeys.TryGetValue(number, out var key))
            {
                key = _lookupKeys[number] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            }

            settings[$"Tenants:{_tenant}:LookupKeys:{number}"] = key;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new RecordCipher(new SensitiveValues(new TenantKeys(new ConfigurationTenantKeySource(configuration))), _tenant);
    }

    private async Task<(ReleaseCompilationResult Compiled, EntityModel Person, NpgsqlConnection Connection)> ProvisionAsync()
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

        Assert.True(compiled.Model!.TryGetEntity("Person", out var person));
        return (compiled, person, connection);
    }

    private Task<RecordCommandResult> CreateAsync(
        NpgsqlConnection connection, ReleaseCompilationResult compiled, EntityModel person, RecordCipher cipher, string body) =>
        RecordCommand.CreateAsync(
            connection,
            _tenant,
            compiled.Model!,
            compiled.Release!.Id,
            person,
            new RecordActor("requester", ["clerk"]),
            _ => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes(body)),
            null,
            cipher,
            NullLoggerFactory.Instance,
            CancellationToken);

    private TemporaryFolder Folder() =>
        new TemporaryFolder()
            .With("application.json", $$"""{ "id": "{{_applicationId}}", "kind": "application", "name": "{{_name}}", "formatVersion": 1 }""")
            .With(
                "entities/person.json",
                $$"""
                { "id": "{{_personId}}", "kind": "entity", "name": "Person", "formatVersion": 1, "fields": [
                    { "name": "name", "type": "text" },
                    { "name": "nationalId", "type": "text", "sensitive": true, "exactLookup": true } ] }
                """)
            .With(
                "data-sources/people-equal.json",
                $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "dataSource", "name": "PeopleEqual", "formatVersion": 1, "entity": "Person",
                  "fields": [{ "name": "name", "path": "name" }, { "name": "nationalId", "path": "nationalId" }],
                  "parameters": [{ "name": "id", "type": "text" }], "filter": "nationalId == id" }
                """)
            .With(
                "data-sources/people-not-equal.json",
                $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "dataSource", "name": "PeopleNotEqual", "formatVersion": 1, "entity": "Person",
                  "fields": [{ "name": "name", "path": "name" }, { "name": "nationalId", "path": "nationalId" }],
                  "parameters": [{ "name": "id", "type": "text" }], "filter": "nationalId != id" }
                """)
            .With(
                "data-sources/people-literal.json",
                $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "dataSource", "name": "PeopleLiteral", "formatVersion": 1, "entity": "Person",
                  "fields": [{ "name": "name", "path": "name" }], "filter": "'{{Matching}}' == nationalId" }
                """)
            .With(
                "data-sources/people-matching.json",
                $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "dataSource", "name": "PeopleMatching", "formatVersion": 1, "entity": "Person",
                  "fields": [{ "name": "name", "path": "name" }],
                  "parameters": [{ "name": "id", "type": "text" }],
                  "aggregate": { "groupBy": ["name"], "measures": [{ "name": "matching", "function": "count", "filter": "nationalId == id" }] } }
                """)
            .With("roles/clerk.json", $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "role", "name": "clerk", "formatVersion": 1, "label": { "textKey": "role.clerk" } }""")
            .With("roles/matcher.json", $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "role", "name": "matcher", "formatVersion": 1, "label": { "textKey": "role.matcher" } }""")
            .With("roles/other.json", $$"""{ "id": "{{Guid.NewGuid()}}", "kind": "role", "name": "other", "formatVersion": 1, "label": { "textKey": "role.other" } }""")
            .With(
                "texts/en.json",
                $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
                  "texts": { "role.clerk": "Clerk", "role.matcher": "Matcher", "role.other": "Other" } }
                """)
            .With(
                "policies/clerk-access.json",
                $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "policy", "name": "ClerkAccess", "formatVersion": 1, "roles": ["clerk"], "grants": [
                    { "name": "personRead", "entity": "Person", "actions": ["read", "create", "update"] }] }
                """)
            .With(
                "policies/matcher-access.json",
                $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "policy", "name": "MatcherAccess", "formatVersion": 1, "roles": ["matcher"], "grants": [
                    { "name": "personRead2", "entity": "Person", "actions": ["read"], "filter": "nationalId == '{{Matching}}'" }] }
                """)
            .With(
                "policies/other-access.json",
                $$"""
                { "id": "{{Guid.NewGuid()}}", "kind": "policy", "name": "OtherAccess", "formatVersion": 1, "roles": ["other"], "grants": [
                    { "name": "personRead3", "entity": "Person", "actions": ["read"], "filter": "nationalId != '{{Matching}}'" }] }
                """);
}
