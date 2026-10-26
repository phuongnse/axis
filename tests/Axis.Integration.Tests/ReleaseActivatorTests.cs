using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;
using Axis.Configuration.Storage;
using Axis.Configuration.Tests;
using Axis.Data;
using Axis.Data.Naming;
using Axis.Data.Schema;
using Axis.Processes.Instances;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Axis.Integration.Tests;

public sealed class ReleaseActivatorTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>
{
    // Each test uses its own application and entity ids and names, so tests sharing the database do
    // not see each other's tables, records and active releases.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly string _name = $"App{Guid.NewGuid():N}";
    private readonly string _sitePath = $"site-{Guid.NewGuid():N}";
    private readonly Guid _connectorId = Guid.NewGuid();

    private const string NumberField = """{ "name": "number", "type": "text", "required": true, "maxLength": 20 }""";
    private const string NoteField = """{ "name": "note", "type": "text" }""";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Activating_a_release_provisions_its_tables_and_stores_one_active_row_and_activating_it_again_changes_only_the_time()
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField);
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var store = new ActiveReleaseStore(configuration);
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, configuration, CancellationToken);
        Assert.NotNull(compiled.Release);

        var first = await ReleaseActivator.ActivateAsync(compiled, store, data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);

        Assert.Empty(first.Diagnostics);
        var table = Assert.Single(await ReadTablesAsync(_orderId));
        Assert.Equal(["id", "version", "computed_release_id", "f_number"], table.Columns.Select(column => column.Name));
        var active = await FindActiveAsync(_applicationId);
        Assert.NotNull(active);
        Assert.Equal((_applicationId, _name, compiled.Release.Id), (active.ApplicationId, active.Name, active.ReleaseId));
        Assert.Equal(1L, await CountActiveRowsAsync(_applicationId, _name));
        var recordsBefore = await ReadRecordsAsync(_applicationId);

        var again = await ReleaseActivator.ActivateAsync(compiled, store, data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);

        Assert.Empty(again.Diagnostics);
        AssertSameTables([table], await ReadTablesAsync(_orderId));
        var recordsAfter = await ReadRecordsAsync(_applicationId);
        Assert.NotEmpty(recordsBefore.Entities);
        Assert.Equal(recordsBefore.Entities, recordsAfter.Entities);
        Assert.Equal(recordsBefore.EnumValues, recordsAfter.EnumValues);
        var reactivated = await FindActiveAsync(_applicationId);
        Assert.NotNull(reactivated);
        Assert.Equal(active with { ActivatedAt = reactivated.ActivatedAt }, reactivated);
        Assert.True(reactivated.ActivatedAt > active.ActivatedAt);
        Assert.Equal(1L, await CountActiveRowsAsync(_applicationId, _name));
    }

    [Fact]
    public async Task Activating_a_later_release_that_adds_a_field_updates_the_active_release_and_keeps_rows()
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField);
        var first = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(first)).Diagnostics);
        var rowId = await InsertOrderAsync();

        folder.With("entities/order.json", OrderFile(_orderId, NumberField, NoteField));
        var second = await CompileAsync(folder);
        Assert.NotNull(second.Release);
        Assert.NotEqual(first.Release?.Id, second.Release.Id);

        var result = await ActivateAsync(second);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(second.Release.Id, (await FindActiveAsync(_applicationId))?.ReleaseId);
        var table = Assert.Single(await ReadTablesAsync(_orderId));
        Assert.Equal(["id", "version", "computed_release_id", "f_number", "f_note"], table.Columns.Select(column => column.Name));
        Assert.Equal([rowId], await ReadIdsAsync(_orderId));
    }

    [Fact]
    public async Task Activating_a_release_whose_provisioning_is_rejected_returns_its_diagnostics_and_keeps_the_previous_release_active()
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField, NoteField);
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        var before = await FindActiveAsync(_applicationId);

        folder.With("entities/order.json", OrderFile(_orderId, NumberField));
        var removed = await CompileAsync(folder);
        Assert.NotNull(removed.Release);

        var result = await ActivateAsync(removed);

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal((DiagnosticCodes.RemovedField, "entities/order.json", "/fields", _orderId), (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
        Assert.NotNull(before);
        Assert.Equal(before, await FindActiveAsync(_applicationId));
    }

    [Theory]
    [InlineData(null, "secret", "/baseUrl/binding", "Tenant 'acme' has no binding 'PurchasingSystemUrl'. Add a 'url' binding with that name to the tenant.")]
    [InlineData("secret", "secret", "/baseUrl/binding", "Tenant 'acme' binding 'PurchasingSystemUrl' is a 'secret' binding. It is used here as a 'url' binding.")]
    [InlineData("url", null, "/auth/token/binding", "Tenant 'acme' has no binding 'PurchasingSystemToken'. Add a 'secret' binding with that name to the tenant.")]
    [InlineData("url", "url", "/auth/token/binding", "Tenant 'acme' binding 'PurchasingSystemToken' is a 'url' binding. It is used here as a 'secret' binding.")]
    public async Task A_connector_binding_the_tenant_lacks_or_holds_with_another_kind_is_a_diagnostic_and_changes_nothing(
        string? urlKind, string? tokenKind, string path, string message)
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField);
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        var before = await FindActiveAsync(_applicationId);
        folder
            .With("entities/order.json", OrderFile(_orderId, NumberField, NoteField))
            .With("connectors/purchasing-system.json", PurchasingConnector(_connectorId));
        var compiled = await CompileAsync(folder);
        var bindings = new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase);
        if (urlKind is not null)
        {
            bindings["PurchasingSystemUrl"] = new ActivationBinding(urlKind, urlKind == "url" ? new Uri("https://purchasing.example.test/api") : null);
        }

        if (tokenKind is not null)
        {
            bindings["PurchasingSystemToken"] = new ActivationBinding(tokenKind, tokenKind == "url" ? new Uri("https://token.example.test") : null);
        }

        var result = await ActivateAsync(compiled, new ActivationBindings("acme", bindings, AllowHttp: false));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingDeploymentBinding, "connectors/purchasing-system.json", path, _connectorId, message),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId, diagnostic.Message));
        Assert.Equal(before, await FindActiveAsync(_applicationId));
        Assert.Equal(["id", "version", "computed_release_id", "f_number"], Assert.Single(await ReadTablesAsync(_orderId)).Columns.Select(column => column.Name));
    }

    [Theory]
    [InlineData(null, "Tenant 'acme' has no binding 'Files'. Add a 'storage' binding with that name to the tenant.")]
    [InlineData("url", "Tenant 'acme' binding 'Files' is a 'url' binding. It is used here as a 'storage' binding.")]
    public async Task A_file_storage_binding_the_tenant_lacks_or_holds_with_another_kind_is_a_diagnostic_and_changes_nothing(
        string? kind, string message)
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField);
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        var before = await FindActiveAsync(_applicationId);
        folder.With("application.json", Manifest(_applicationId, _name, "Files"));
        var compiled = await CompileAsync(folder);
        var bindings = new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase);
        if (kind is not null)
        {
            bindings["Files"] = new ActivationBinding(kind, new Uri("https://files.example.test"));
        }

        var result = await ActivateAsync(compiled, new ActivationBindings("acme", bindings, AllowHttp: false));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingDeploymentBinding, "application.json", "/fileStorage/binding", _applicationId, message),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId, diagnostic.Message));
        Assert.Equal(before, await FindActiveAsync(_applicationId));
    }

    [Fact]
    public async Task A_storage_binding_the_tenant_holds_activates_a_release_that_names_it()
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField);
        folder.With("application.json", Manifest(_applicationId, _name, "Files"));
        var compiled = await CompileAsync(folder);
        var bindings = new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase)
        {
            ["files"] = new(ActivationBinding.StorageKind, null),
        };

        var result = await ActivateAsync(compiled, new ActivationBindings("acme", bindings, AllowHttp: false));

        Assert.Empty(result.Diagnostics);
        Assert.Equal(compiled.Release?.Id, (await FindActiveAsync(_applicationId))?.ReleaseId);
    }

    [Fact]
    public async Task An_application_without_file_storage_activates_with_no_bindings()
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField);
        var compiled = await CompileAsync(folder);

        var result = await ActivateAsync(compiled, ActivationBindings.None("acme"));

        Assert.Empty(result.Diagnostics);
        Assert.Equal(compiled.Release?.Id, (await FindActiveAsync(_applicationId))?.ReleaseId);
    }

    [Fact]
    public async Task Every_binding_a_tenant_lacks_is_reported_and_nothing_is_provisioned_or_activated()
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField)
            .With("connectors/purchasing-system.json", PurchasingConnector(_connectorId));
        var compiled = await CompileAsync(folder);

        var result = await ActivateAsync(compiled, ActivationBindings.None("acme"));

        Assert.Equal(
            [
                (DiagnosticCodes.MissingDeploymentBinding, "/auth/token/binding"),
                (DiagnosticCodes.MissingDeploymentBinding, "/baseUrl/binding"),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path)).Order());
        Assert.Empty(await ReadTablesAsync(_orderId));
        Assert.Null(await FindActiveAsync(_applicationId));
    }

    [Fact]
    public async Task On_a_preview_tenant_every_missing_binding_is_a_warning_and_the_release_is_activated()
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField)
            .With("connectors/purchasing-system.json", PurchasingConnector(_connectorId));
        var compiled = await CompileAsync(folder);

        var result = await ActivateAsync(compiled, ActivationBindings.None("acme") with { IsPreview = true });

        Assert.Equal(
            [
                (DiagnosticCodes.MissingDeploymentBinding, "/auth/token/binding", DiagnosticSeverity.Warning),
                (DiagnosticCodes.MissingDeploymentBinding, "/baseUrl/binding", DiagnosticSeverity.Warning),
            ],
            result.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Path, diagnostic.Severity)).Order());
        Assert.Equal(compiled.Release?.Id, (await FindActiveAsync(_applicationId))?.ReleaseId);
    }

    [Theory]
    [InlineData("secret", "secret", "/baseUrl/binding")]
    [InlineData("url", "url", "/auth/token/binding")]
    public async Task On_a_preview_tenant_a_binding_of_the_wrong_kind_is_still_an_error_and_changes_nothing(
        string urlKind, string tokenKind, string path)
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField)
            .With("connectors/purchasing-system.json", PurchasingConnector(_connectorId));
        var compiled = await CompileAsync(folder);
        var bindings = new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase)
        {
            ["PurchasingSystemUrl"] = new(urlKind, urlKind == "url" ? new Uri("https://purchasing.example.test/api") : null),
            ["PurchasingSystemToken"] = new(tokenKind, tokenKind == "url" ? new Uri("https://token.example.test") : null),
        };

        var result = await ActivateAsync(compiled, new ActivationBindings("acme", bindings, AllowHttp: false, IsPreview: true));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.MissingDeploymentBinding, path, DiagnosticSeverity.Error),
            (diagnostic.Code, diagnostic.Path, diagnostic.Severity));
        Assert.Empty(await ReadTablesAsync(_orderId));
        Assert.Null(await FindActiveAsync(_applicationId));
    }

    [Fact]
    public async Task On_a_preview_tenant_an_http_base_url_is_still_an_error()
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField)
            .With("connectors/purchasing-system.json", PurchasingConnector(_connectorId));
        var compiled = await CompileAsync(folder);
        var bindings = new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase)
        {
            ["PurchasingSystemUrl"] = new(ActivationBinding.UrlKind, new Uri("http://purchasing.example.test/api")),
        };

        var result = await ActivateAsync(compiled, new ActivationBindings("acme", bindings, AllowHttp: false, IsPreview: true));

        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == DiagnosticCodes.InsecureBaseUrl && diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Null(await FindActiveAsync(_applicationId));
    }

    [Fact]
    public async Task An_http_base_url_is_refused_unless_http_is_allowed()
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField);
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        var before = await FindActiveAsync(_applicationId);
        folder.With("connectors/purchasing-system.json", PurchasingConnector(_connectorId));
        var compiled = await CompileAsync(folder);
        var bindings = new Dictionary<string, ActivationBinding>(StringComparer.OrdinalIgnoreCase)
        {
            ["purchasingsystemurl"] = new(ActivationBinding.UrlKind, new Uri("http://purchasing.example.test/api")),
            ["PurchasingSystemToken"] = new(ActivationBinding.SecretKind, null),
        };

        var refused = await ActivateAsync(compiled, new ActivationBindings("acme", bindings, AllowHttp: false));

        var diagnostic = Assert.Single(refused.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.InsecureBaseUrl, "connectors/purchasing-system.json", "/baseUrl/binding",
                "Tenant 'acme' binding 'PurchasingSystemUrl' is not an https URL. Only Development allows http."),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.Message));
        Assert.Equal(before, await FindActiveAsync(_applicationId));

        var allowed = await ActivateAsync(compiled, new ActivationBindings("acme", bindings, AllowHttp: true));

        Assert.Empty(allowed.Diagnostics);
        Assert.Equal(compiled.Release?.Id, (await FindActiveAsync(_applicationId))?.ReleaseId);
    }

    [Fact]
    public async Task Name_active_for_another_application_in_another_letter_case_returns_one_diagnostic_and_changes_nothing()
    {
        var otherApplicationId = Guid.NewGuid();
        using var other = ApplicationFolder(otherApplicationId, _name, Guid.NewGuid(), NumberField);
        Assert.Empty((await ActivateAsync(await CompileAsync(other))).Diagnostics);
        var otherActive = await FindActiveAsync(otherApplicationId);
        using var folder = ApplicationFolder(_applicationId, _name.ToUpperInvariant(), _orderId, NumberField);
        var compiled = await CompileAsync(folder);

        var result = await ActivateAsync(compiled);

        AssertNameActiveForOtherApplication(result);
        Assert.Empty(await ReadTablesAsync(_orderId));
        var records = await ReadRecordsAsync(_applicationId);
        Assert.Empty(records.Entities);
        Assert.Empty(records.EnumValues);
        Assert.Null(await FindActiveAsync(_applicationId));
        Assert.Equal(otherActive, await FindActiveAsync(otherApplicationId));

        // A store that does not see the other application, as when it activates concurrently,
        // is refused the name at the write, and provisioning rolls back with it.
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var hiding = await ReleaseActivator.ActivateAsync(compiled, new ConflictHidingStore(new ActiveReleaseStore(configuration)), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);

        AssertNameActiveForOtherApplication(hiding);
        Assert.Empty(await ReadTablesAsync(_orderId));
        Assert.Empty((await ReadRecordsAsync(_applicationId)).Entities);
        Assert.Null(await FindActiveAsync(_applicationId));
        Assert.Equal(otherActive, await FindActiveAsync(otherApplicationId));
    }

    [Fact]
    public async Task Renamed_application_holds_its_new_name_and_frees_the_old_one()
    {
        var newName = $"App{Guid.NewGuid():N}";
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField);
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);

        folder.With("application.json", Manifest(_applicationId, newName));
        var renamed = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(renamed)).Diagnostics);

        var active = await FindActiveAsync(_applicationId);
        Assert.NotNull(active);
        Assert.Equal((newName, renamed.Release?.Id), (active.Name, (Guid?)active.ReleaseId));
        await using var configuration = database.CreateConfigurationContext();
        var store = new ActiveReleaseStore(configuration);
        Assert.Null(await store.FindByNameAsync(_name, CancellationToken));
        Assert.Equal(_applicationId, (await store.FindByNameAsync(newName.ToLowerInvariant(), CancellationToken))?.ApplicationId);

        var otherApplicationId = Guid.NewGuid();
        using var other = ApplicationFolder(otherApplicationId, _name, Guid.NewGuid(), NumberField);
        Assert.Empty((await ActivateAsync(await CompileAsync(other))).Diagnostics);
        Assert.Equal(_name, (await FindActiveAsync(otherApplicationId))?.Name);
    }

    [Fact]
    public async Task Store_upserts_per_application_refuses_a_taken_name_and_stays_usable()
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField);
        var first = await CompileAsync(folder);
        folder.With("entities/order.json", OrderFile(_orderId, NumberField, NoteField));
        var second = await CompileAsync(folder);
        Assert.NotNull(first.Release);
        Assert.NotNull(second.Release);
        var otherApplicationId = Guid.NewGuid();
        var activatedAt = new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.FromHours(7));
        await using var configuration = database.CreateConfigurationContext();
        var store = new ActiveReleaseStore(configuration);

        Assert.True((await store.TrySetAsync(_applicationId, _name, first.Release.Id, [], activatedAt, ActivationActor.Startup, CancellationToken)).IsSet);
        Assert.True((await store.TrySetAsync(_applicationId, _name, second.Release.Id, [], activatedAt.AddMinutes(1), ActivationActor.Startup, CancellationToken)).IsSet);
        var refused = await store.TrySetAsync(otherApplicationId, _name.ToUpperInvariant(), first.Release.Id, [], activatedAt, ActivationActor.Startup, CancellationToken);
        Assert.Equal(new SetActiveReleaseResult(ActiveReleaseConflict.Name), refused);
        Assert.False(refused.IsSet);

        Assert.Equal(new ActiveRelease(_applicationId, _name, second.Release.Id, activatedAt.AddMinutes(1)), await store.FindByApplicationIdAsync(_applicationId, CancellationToken));
        Assert.Null(await store.FindByApplicationIdAsync(otherApplicationId, CancellationToken));
        Assert.Equal(1L, await CountActiveRowsAsync(_applicationId, _name));
    }

    [Fact]
    public async Task Store_lets_other_database_errors_propagate()
    {
        await using var configuration = database.CreateConfigurationContext();
        var store = new ActiveReleaseStore(configuration);

        var exception = await Assert.ThrowsAsync<PostgresException>(
            () => store.TrySetAsync(_applicationId, _name, Guid.NewGuid(), [_sitePath], DateTimeOffset.UtcNow, ActivationActor.Startup, CancellationToken));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, exception.SqlState);
        Assert.Null(await store.FindByApplicationIdAsync(_applicationId, CancellationToken));
        Assert.Equal(0L, await CountActiveSiteRowsAsync(_applicationId, _sitePath));
    }

    [Fact]
    public async Task Site_path_active_for_another_application_returns_one_diagnostic_and_changes_nothing()
    {
        var otherApplicationId = Guid.NewGuid();
        using var other = WithSite(ApplicationFolder(otherApplicationId, $"App{Guid.NewGuid():N}", Guid.NewGuid(), NumberField), _sitePath);
        Assert.Empty((await ActivateAsync(await CompileAsync(other))).Diagnostics);
        var otherActive = await FindActiveAsync(otherApplicationId);
        Assert.NotNull(otherActive);
        var siteId = Guid.NewGuid();
        using var folder = WithSite(ApplicationFolder(_applicationId, _name, _orderId, NumberField), _sitePath, siteId);
        var compiled = await CompileAsync(folder);

        var result = await ActivateAsync(compiled);

        AssertSitePathActiveForOtherApplication(result, siteId);
        Assert.Empty(await ReadTablesAsync(_orderId));
        var records = await ReadRecordsAsync(_applicationId);
        Assert.Empty(records.Entities);
        Assert.Empty(records.EnumValues);
        Assert.Null(await FindActiveAsync(_applicationId));
        Assert.Equal(otherActive, await FindActiveAsync(otherApplicationId));
        Assert.Equal(1L, await CountActiveSiteRowsAsync(otherApplicationId, _sitePath));

        // A store that does not see the other application's path, as when it activates
        // concurrently, is refused the path by the unique index, and provisioning rolls back with it.
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        var hiding = await ReleaseActivator.ActivateAsync(compiled, new ConflictHidingStore(new ActiveReleaseStore(configuration)), data, ActivationBindings.None("default"), OpenInstanceCounter.Instance, CancellationToken);

        AssertSitePathActiveForOtherApplication(hiding, siteId);
        Assert.Empty(await ReadTablesAsync(_orderId));
        Assert.Empty((await ReadRecordsAsync(_applicationId)).Entities);
        Assert.Null(await FindActiveAsync(_applicationId));
        Assert.Equal(0L, await CountActiveSiteRowsAsync(_applicationId, _sitePath));
        Assert.Equal(otherActive, await FindActiveAsync(otherApplicationId));
        Assert.Equal(1L, await CountActiveSiteRowsAsync(otherApplicationId, _sitePath));
    }

    [Fact]
    public async Task Reactivating_keeps_one_site_row_and_a_renamed_path_frees_the_old_one()
    {
        var siteId = Guid.NewGuid();
        var newPath = $"site-{Guid.NewGuid():N}";
        using var folder = WithSite(ApplicationFolder(_applicationId, _name, _orderId, NumberField), _sitePath, siteId);
        var compiled = await CompileAsync(folder);

        Assert.Empty((await ActivateAsync(compiled)).Diagnostics);
        Assert.Empty((await ActivateAsync(compiled)).Diagnostics);

        Assert.Equal(1L, await CountActiveSiteRowsAsync(_applicationId, _sitePath));

        folder.With("sites/main.json", Site(siteId, newPath));
        var renamed = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(renamed)).Diagnostics);

        await using var configuration = database.CreateConfigurationContext();
        var store = new ActiveReleaseStore(configuration);
        Assert.Null(await store.FindBySitePathAsync(_sitePath, CancellationToken));
        var active = await store.FindBySitePathAsync(newPath, CancellationToken);
        Assert.Equal((_applicationId, renamed.Release?.Id), (active?.ApplicationId, active?.ReleaseId));
        Assert.Equal(0L, await CountActiveSiteRowsAsync(_applicationId, _sitePath));
        Assert.Equal(1L, await CountActiveSiteRowsAsync(_applicationId, newPath));

        var otherApplicationId = Guid.NewGuid();
        using var other = WithSite(ApplicationFolder(otherApplicationId, $"App{Guid.NewGuid():N}", Guid.NewGuid(), NumberField), _sitePath);
        Assert.Empty((await ActivateAsync(await CompileAsync(other))).Diagnostics);
        Assert.Equal(otherApplicationId, (await store.FindBySitePathAsync(_sitePath, CancellationToken))?.ApplicationId);
    }

    [Fact]
    public async Task Store_lists_active_releases_and_finds_them_by_site_path()
    {
        using var folder = WithSite(ApplicationFolder(_applicationId, _name, _orderId, NumberField), _sitePath);
        var otherApplicationId = Guid.NewGuid();
        var otherName = $"App{Guid.NewGuid():N}";
        var otherPath = $"site-{Guid.NewGuid():N}";
        using var other = WithSite(ApplicationFolder(otherApplicationId, otherName, Guid.NewGuid(), NumberField), otherPath);
        var compiled = await CompileAsync(folder);
        var otherCompiled = await CompileAsync(other);
        Assert.NotNull(compiled.Release);
        Assert.NotNull(otherCompiled.Release);
        var activatedAt = new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero);
        await using var configuration = database.CreateConfigurationContext();
        var store = new ActiveReleaseStore(configuration);

        Assert.True((await store.TrySetAsync(_applicationId, _name, compiled.Release.Id, [_sitePath], activatedAt, ActivationActor.Startup, CancellationToken)).IsSet);
        Assert.True((await store.TrySetAsync(otherApplicationId, otherName, otherCompiled.Release.Id, [otherPath], activatedAt, ActivationActor.Startup, CancellationToken)).IsSet);
        var refused = await store.TrySetAsync(Guid.NewGuid(), $"App{Guid.NewGuid():N}", compiled.Release.Id, [_sitePath], activatedAt, ActivationActor.Startup, CancellationToken);
        Assert.Equal(new SetActiveReleaseResult(ActiveReleaseConflict.SitePath, _sitePath), refused);

        var active = new ActiveRelease(_applicationId, _name, compiled.Release.Id, activatedAt);
        var otherActive = new ActiveRelease(otherApplicationId, otherName, otherCompiled.Release.Id, activatedAt);
        var listed = await store.ListAsync(CancellationToken);
        Assert.Contains(active, listed);
        Assert.Contains(otherActive, listed);
        Assert.Equal(active, await store.FindBySitePathAsync(_sitePath, CancellationToken));
        Assert.Equal(otherActive, await store.FindBySitePathAsync(otherPath, CancellationToken));
        Assert.Null(await store.FindBySitePathAsync($"site-{Guid.NewGuid():N}", CancellationToken));
    }

    [Fact]
    public async Task Activating_a_result_without_a_release_and_model_of_one_application_throws()
    {
        using var folder = ApplicationFolder(_applicationId, _name, _orderId, NumberField);
        var compiled = await CompileAsync(folder);
        Assert.NotNull(compiled.Model);
        var mismatched = compiled with { Model = compiled.Model with { Manifest = compiled.Model.Manifest with { Id = Guid.NewGuid() } } };

        await Assert.ThrowsAsync<ArgumentException>(() => ActivateAsync(compiled with { Release = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => ActivateAsync(compiled with { Model = null }));
        await Assert.ThrowsAsync<ArgumentException>(() => ActivateAsync(mismatched));
        Assert.Empty(await ReadTablesAsync(_orderId));
        Assert.Null(await FindActiveAsync(_applicationId));
    }

    private void AssertNameActiveForOtherApplication(ActivationResult result)
    {
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.ApplicationNameActiveForOtherApplication, "application.json", "/name", _applicationId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
    }

    private static void AssertSitePathActiveForOtherApplication(ActivationResult result, Guid siteId)
    {
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            (DiagnosticCodes.SitePathActiveForOtherApplication, "sites/main.json", "/path", siteId),
            (diagnostic.Code, diagnostic.File, diagnostic.Path, diagnostic.ResourceId));
    }

    private static string Manifest(Guid id, string name) =>
        $$"""{ "id": "{{id}}", "kind": "application", "name": "{{name}}", "formatVersion": 1 }""";

    private static string Manifest(Guid id, string name, string fileStorage) =>
        $$"""{ "id": "{{id}}", "kind": "application", "name": "{{name}}", "formatVersion": 1, "fileStorage": { "binding": "{{fileStorage}}" } }""";

    private static string OrderFile(Guid id, params string[] fields) =>
        $$"""
        { "id": "{{id}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [ {{string.Join(", ", fields)}} ] }
        """;

    private static string PurchasingConnector(Guid id) =>
        $$"""
        { "id": "{{id}}", "kind": "connector", "name": "PurchasingSystem", "formatVersion": 1,
          "baseUrl": { "binding": "PurchasingSystemUrl" },
          "auth": { "type": "bearer", "token": { "binding": "PurchasingSystemToken" } },
          "operations": [ { "name": "createPurchaseOrder", "method": "POST", "path": "/purchase-orders" } ] }
        """;

    private static TemporaryFolder ApplicationFolder(Guid applicationId, string name, Guid orderId, params string[] fields) =>
        new TemporaryFolder()
            .With("application.json", Manifest(applicationId, name))
            .With("entities/order.json", OrderFile(orderId, fields));

    /// <summary>Gives the folder a site at <paramref name="path"/> whose navigation opens a table page of orders.</summary>
    private static TemporaryFolder WithSite(TemporaryFolder folder, string path, Guid? siteId = null) =>
        folder
            .With("texts/en.json", Texts(Guid.NewGuid()))
            .With("pages/orders.json", OrdersPage(Guid.NewGuid()))
            .With("sites/main.json", Site(siteId ?? Guid.NewGuid(), path));

    private static string Texts(Guid id) =>
        $$"""
        { "id": "{{id}}", "kind": "text", "name": "TextsEn", "formatVersion": 1, "locale": "en",
          "texts": { "site.title": "Sales", "nav.orders": "Orders", "orders.title": "Orders" } }
        """;

    private static string OrdersPage(Guid id) =>
        $$"""
        { "id": "{{id}}", "kind": "page", "name": "Orders", "formatVersion": 1,
          "title": { "textKey": "orders.title" }, "widgets": [ { "type": "table", "entity": "Order" } ] }
        """;

    private static string Site(Guid id, string path) =>
        $$"""
        { "id": "{{id}}", "kind": "site", "name": "Main", "formatVersion": 1, "path": "{{path}}",
          "title": { "textKey": "site.title" }, "locales": { "default": "en", "fallback": "en", "available": ["en"] },
          "navigation": [ { "page": "Orders", "label": { "textKey": "nav.orders" } } ] }
        """;

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        return compiled;
    }

    private Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled) =>
        ActivateAsync(compiled, ActivationBindings.None("default"));

    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled, ActivationBindings bindings)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        await using var configuration = DataDatabaseFixture.CreateConfigurationContext(connection);
        await using var data = DataDatabaseFixture.CreateContext(connection);
        return await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, bindings, OpenInstanceCounter.Instance, CancellationToken);
    }

    /// <summary>The active release read through a fresh store.</summary>
    private async Task<ActiveRelease?> FindActiveAsync(Guid applicationId)
    {
        await using var context = database.CreateConfigurationContext();
        return await new ActiveReleaseStore(context).FindByApplicationIdAsync(applicationId, CancellationToken);
    }

    /// <summary>The active rows of the application or under the name in any letter case.</summary>
    private async Task<long> CountActiveRowsAsync(Guid applicationId, string name)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM axis.active_releases WHERE application_id = @id OR lower(name) = lower(@name)");
        command.Parameters.AddWithValue("id", applicationId);
        command.Parameters.AddWithValue("name", name);
        return Assert.IsType<long>(await command.ExecuteScalarAsync(CancellationToken));
    }

    /// <summary>The active site rows of the application with the path.</summary>
    private async Task<long> CountActiveSiteRowsAsync(Guid applicationId, string path)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand("SELECT count(*) FROM axis.active_sites WHERE application_id = @id AND path = @path");
        command.Parameters.AddWithValue("id", applicationId);
        command.Parameters.AddWithValue("path", path);
        return Assert.IsType<long>(await command.ExecuteScalarAsync(CancellationToken));
    }

    private async Task<Guid> InsertOrderAsync()
    {
        var id = Guid.NewGuid();
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"""INSERT INTO {EntityNaming.QualifiedTable(EntityNaming.Table(_orderId))} ("id", "f_number") VALUES (@id, 'PO-1')""");
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync(CancellationToken);
        return id;
    }

    private async Task<List<Guid>> ReadIdsAsync(Guid entityId)
    {
        await using var dataSource = NpgsqlDataSource.Create(database.ConnectionString);
        await using var command = dataSource.CreateCommand($"""SELECT "id" FROM {EntityNaming.QualifiedTable(EntityNaming.Table(entityId))}""");
        await using var reader = await command.ExecuteReaderAsync(CancellationToken);
        var ids = new List<Guid>();
        while (await reader.ReadAsync(CancellationToken))
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    /// <summary>The catalog table of the entity, if it exists.</summary>
    private async Task<List<CatalogTable>> ReadTablesAsync(Guid entityId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(CancellationToken);
        var catalog = await CatalogReader.ReadAsync(connection, null, CancellationToken);
        return [.. catalog.Tables.Where(table => table.Name == EntityNaming.Table(entityId))];
    }

    /// <summary>The provisioning records of the application and of this test's order entity.</summary>
    private async Task<ProvisioningRecords> ReadRecordsAsync(Guid applicationId)
    {
        await using var context = database.CreateContext();
        var entities = await context.ProvisionedEntities
            .AsNoTracking()
            .Where(entity => entity.ApplicationId == applicationId || entity.EntityId == _orderId)
            .Select(entity => new ProvisionedEntity(entity.EntityId, entity.ApplicationId, entity.TableName))
            .ToListAsync(CancellationToken);
        var enumValues = await context.ProvisionedEnumValues
            .AsNoTracking()
            .Where(value => value.EntityId == _orderId)
            .Select(value => new ProvisionedEnumValue(value.EntityId, value.FieldName, value.Value))
            .ToListAsync(CancellationToken);
        return new ProvisioningRecords(
            [.. entities.OrderBy(entity => entity.EntityId)],
            [.. enumValues.OrderBy(value => value.FieldName, StringComparer.Ordinal).ThenBy(value => value.Value, StringComparer.Ordinal)]);
    }

    private static void AssertSameTables(IReadOnlyList<CatalogTable> expected, IReadOnlyList<CatalogTable> actual)
    {
        Assert.Equal(expected.Select(table => (table.Name, table.HasRows)), actual.Select(table => (table.Name, table.HasRows)));
        foreach (var (expectedTable, actualTable) in expected.Zip(actual))
        {
            Assert.Equal(expectedTable.Columns, actualTable.Columns);
            Assert.Equal(
                expectedTable.Indexes.Select(index => (index.Name, Columns: string.Join(',', index.Columns), index.Unique)),
                actualTable.Indexes.Select(index => (index.Name, Columns: string.Join(',', index.Columns), index.Unique)));
        }
    }
}
