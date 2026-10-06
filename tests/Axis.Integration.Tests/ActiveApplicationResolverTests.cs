using Axis.Configuration.Model;
using Axis.Configuration.Releases;
using Axis.Configuration.Tests;
using Axis.Core.Tenancy;
using Axis.Data;
using Axis.Server.Applications;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Integration.Tests;

public sealed class ActiveApplicationResolverTests(DataDatabaseFixture database) : IClassFixture<DataDatabaseFixture>, IAsyncLifetime
{
    // Release ids are generated per database, so only two tenant ids on the same database can share a
    // release id. That is what shows the cache is kept per tenant.
    private const string TenantA = "a";
    private const string TenantB = "b";

    // Each test uses its own ids and name, so tests sharing the database do not see each other's
    // active releases. The name contains a 'K' for the Kelvin sign case.
    private readonly Guid _applicationId = Guid.NewGuid();
    private readonly Guid _orderId = Guid.NewGuid();
    private readonly string _name = $"Kiosk{Guid.NewGuid():N}";

    private const string NumberField = """{ "name": "number", "type": "text", "required": true, "maxLength": 20 }""";
    private const string NoteField = """{ "name": "note", "type": "text" }""";

    private WebApplicationFactory<Program>? _factory;

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    private WebApplicationFactory<Program> Factory => _factory ?? throw new InvalidOperationException("The test host is not started.");

    [Fact]
    public async Task Active_release_resolves_to_its_compiled_model_by_name_in_another_letter_case()
    {
        using var folder = ApplicationFolder(NumberField);
        var compiled = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(compiled)).Diagnostics);

        var resolved = await ResolveAsync(TenantA, _name.ToUpperInvariant());

        Assert.NotNull(compiled.Model);
        Assert.NotNull(resolved);
        ModelAssert.Equal(compiled.Model, resolved);
    }

    [Fact]
    public async Task Model_is_cached_per_tenant_and_release_and_a_new_activation_is_served_on_the_next_resolve()
    {
        using var folder = ApplicationFolder(NumberField);
        var first = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(first)).Diagnostics);

        var inA = await ResolveAsync(TenantA, _name);
        var againInA = await ResolveAsync(TenantA, _name.ToLowerInvariant());
        var inB = await ResolveAsync(TenantB, _name);

        Assert.NotNull(inA);
        Assert.Same(inA, againInA);
        Assert.NotNull(inB);
        Assert.NotSame(inA, inB);
        ModelAssert.Equal(inA, inB);

        folder.With("entities/order.json", OrderFile(NumberField, NoteField));
        var second = await CompileAsync(folder);
        Assert.Empty((await ActivateAsync(second)).Diagnostics);

        var afterActivation = await ResolveAsync(TenantA, _name);

        Assert.NotNull(second.Model);
        Assert.NotNull(afterActivation);
        Assert.NotSame(inA, afterActivation);
        ModelAssert.Equal(second.Model, afterActivation);
        Assert.Equal(["number", "note"], Assert.Single(afterActivation.Entities).Fields.Select(field => field.Name));
    }

    [Fact]
    public async Task Unused_name_or_name_outside_the_manifest_rule_resolves_to_null_and_no_tenant_context_throws()
    {
        using var folder = ApplicationFolder(NumberField);
        Assert.Empty((await ActivateAsync(await CompileAsync(folder))).Diagnostics);
        Assert.NotNull(await ResolveAsync(TenantA, _name));

        Assert.Null(await ResolveAsync(TenantA, $"Unused{Guid.NewGuid():N}"));

        // U+212A KELVIN SIGN lowercases to 'k', so without the name rule it would match the active name.
        var kelvin = _name.Replace('K', 'K');
        Assert.NotEqual(_name, kelvin);
        Assert.Null(await ResolveAsync(TenantA, kelvin));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ResolveAsync(null, _name));
    }

    public ValueTask InitializeAsync()
    {
        // The server migrates tenant databases only when ActivateOnStartup is set, which it is not
        // here. Both tenants use the fixture's database, which the fixture has already migrated.
        var connectionString = database.ConnectionString;
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Platform", connectionString);
            builder.UseSetting("Tenants:a:Hosts:0", "a.example.test");
            builder.UseSetting("Tenants:a:ConnectionString", connectionString);
            builder.UseSetting("Tenants:b:Hosts:0", "b.example.test");
            builder.UseSetting("Tenants:b:ConnectionString", connectionString);
        });
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
        {
            await _factory.DisposeAsync();
        }
    }

    /// <summary>Resolves <paramref name="name"/> in a new request scope of the server, for <paramref name="tenantId"/> or without a tenant.</summary>
    private async Task<ApplicationModel?> ResolveAsync(string? tenantId, string name)
    {
        var accessor = Factory.Services.GetRequiredService<ITenantContextAccessor>();
        accessor.Current = tenantId is null ? null : new TenantContext(tenantId);
        try
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<ActiveApplicationResolver>().ResolveAsync(name, CancellationToken);
        }
        finally
        {
            accessor.Current = null;
        }
    }

    private static string Manifest(Guid id, string name) =>
        $$"""{ "id": "{{id}}", "kind": "application", "name": "{{name}}", "formatVersion": 1 }""";

    private string OrderFile(params string[] fields) =>
        $$"""
        { "id": "{{_orderId}}", "kind": "entity", "name": "Order", "formatVersion": 1, "fields": [ {{string.Join(", ", fields)}} ] }
        """;

    private TemporaryFolder ApplicationFolder(params string[] fields) =>
        new TemporaryFolder()
            .With("application.json", Manifest(_applicationId, _name))
            .With("entities/order.json", OrderFile(fields));

    private async Task<ReleaseCompilationResult> CompileAsync(TemporaryFolder folder)
    {
        await using var context = database.CreateConfigurationContext();
        var compiled = await ReleaseCompiler.CompileAsync(folder.Path, context, CancellationToken);
        Assert.Empty(compiled.Diagnostics);
        return compiled;
    }

    private async Task<ActivationResult> ActivateAsync(ReleaseCompilationResult compiled)
    {
        await using var configuration = database.CreateConfigurationContext();
        await using var data = database.CreateContext();
        return await ReleaseActivator.ActivateAsync(compiled, new ActiveReleaseStore(configuration), data, CancellationToken);
    }
}
