using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.Configuration.Compilation;
using Axis.Configuration.Modules;
using Axis.Configuration.Tests;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Axis.Integration.Tests;

/// <summary>
/// An application of typical size keeps the M8 authoring work fast: its compile with the reference
/// index, the share of the compile time that the index adds, the Studio diagnostics and patch answers,
/// the where-used answers, and the compile of an application that declares three modules.
/// </summary>
public sealed class AuthoringPerformanceTests
{
    // Each answer is one full compile of the folder, so it has the limit of CompilePerformanceTests.
    // Locally, every answer took 0.17 to 0.55 seconds, so the limit is about 18 times the slowest.
    // It is well above that, so shared CI runners do not make the test flaky, and it still catches
    // a slowdown of an order of magnitude.
    private static readonly TimeSpan _answerTime = TimeSpan.FromSeconds(10);

    // The index may add 30% of the compile time. A floor of 300 ms keeps timer noise on a short
    // compile from failing the test. Locally, the fastest run with the index was not slower than the
    // fastest run without it, so the index adds no measurable time.
    private const double IndexShare = 0.30;
    private static readonly TimeSpan _indexFloor = TimeSpan.FromMilliseconds(300);

    private const string Application = "Typical";
    private const string EntityFile = "entities/entity-00.json";

    // Port 1 on loopback refuses connections immediately. The authoring API reads no database.
    private const string UnreachableDatabase = "Host=127.0.0.1;Port=1;Database=axis;Username=axis;Password=axis;Timeout=2";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public void A_typical_application_compiles_with_the_reference_index_within_the_time_limit()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);
        ApplicationCompiler.Compile(folder.Path);

        var (result, elapsed) = Time(() => ApplicationCompiler.Compile(folder.Path));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal(TypicalApplication.Entities, result.Model.Entities.Count);
        Assert.True(result.Model.Entities.Sum(entity => entity.Fields.Count) >= 2_000);
        Assert.Equal(TypicalApplication.DataSources, result.Model.DataSources.Count);
        Assert.Equal(1 + 2 + TypicalApplication.Entities + TypicalApplication.DataSources + TypicalApplication.Rules, result.Resources.Count);
        Assert.Equal(2, result.Model.Texts.Count);
        Assert.All(result.Model.Texts, text => Assert.Equal(TypicalApplication.TextKeys, text.Texts.Count));
        Assert.NotEmpty(result.References.Edges);
        Assert.True(elapsed < _answerTime, $"The compile took {elapsed}.");
    }

    [Fact]
    public void The_reference_index_adds_no_more_than_its_share_of_the_compile_time()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);
        ApplicationCompiler.Compile(folder.Path, null, null, collectReferences: false);
        ApplicationCompiler.Compile(folder.Path);

        var withoutIndex = TimeSpan.MaxValue;
        var withIndex = TimeSpan.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            var (uncollected, uncollectedElapsed) = Time(() => ApplicationCompiler.Compile(folder.Path, null, null, collectReferences: false));
            Assert.Empty(uncollected.References.Edges);
            withoutIndex = uncollectedElapsed < withoutIndex ? uncollectedElapsed : withoutIndex;

            var (collected, collectedElapsed) = Time(() => ApplicationCompiler.Compile(folder.Path));
            Assert.NotEmpty(collected.References.Edges);
            withIndex = collectedElapsed < withIndex ? collectedElapsed : withIndex;
        }

        var allowance = TimeSpan.FromTicks(Math.Max((long)(withoutIndex.Ticks * IndexShare), _indexFloor.Ticks));
        var added = withIndex - withoutIndex;
        Assert.True(added <= allowance, $"The index added {added} to a compile that took {withoutIndex} without it.");
    }

    [Fact]
    public async Task The_studio_diagnostics_of_a_typical_application_answer_within_the_time_limit()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);
        await using var factory = CreateFactory(folder.Path);
        using var client = factory.CreateClient().SignedInAs("hoa");

        var (status, body, elapsed) = await Answer(
            "The diagnostics",
            () => client.GetAsync(new Uri($"/api/studio/apps/{Application}/diagnostics", UriKind.Relative), CancellationToken));

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.DoesNotContain(json.RootElement.GetProperty("diagnostics").EnumerateArray(), item => item.GetProperty("severity").GetString() == "error");
        Assert.True(elapsed < _answerTime, $"The diagnostics took {elapsed}.");
    }

    [Fact]
    public async Task A_patch_of_an_entity_in_a_typical_application_answers_within_the_time_limit()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);
        var path = Path.Combine(folder.Path, EntityFile);
        var url = $"/api/studio/apps/{Application}/resources/{EntityId(folder.Path)}";
        await using var factory = CreateFactory(folder.Path);
        using var client = factory.CreateClient().SignedInAs("hoa");

        // The version is read for each send, so the timed patch matches the file the warm-up patch left.
        var (status, _, elapsed) = await Answer(
            "The patch",
            () => PatchAsync(client, url, """[{"op":"replace","path":"/fields/0/maxLength","value":120}]""", Sha256(path)));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(elapsed < _answerTime, $"The patch took {elapsed}.");
    }

    [Fact]
    public async Task Where_the_most_used_entity_is_used_answers_within_the_time_limit()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);
        var url = $"/api/studio/apps/{Application}/resources/{EntityId(folder.Path)}/references";
        await using var factory = CreateFactory(folder.Path);
        using var client = factory.CreateClient().SignedInAs("hoa");

        var (status, body, elapsed) = await Answer(
            "The where-used answer for the most used entity",
            () => client.GetAsync(new Uri(url, UriKind.Relative), CancellationToken));

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.GetProperty("usedBy").GetArrayLength() >= 89);
        Assert.True(elapsed < _answerTime, $"The where-used answer took {elapsed}.");
    }

    [Fact]
    public async Task Where_the_shared_text_key_is_used_answers_within_the_time_limit()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);
        var url = $"/api/studio/apps/{Application}/texts/{TypicalApplication.SharedTextKey}/references";
        await using var factory = CreateFactory(folder.Path);
        using var client = factory.CreateClient().SignedInAs("hoa");

        var (status, body, elapsed) = await Answer(
            "The where-used answer for the shared text key",
            () => client.GetAsync(new Uri(url, UriKind.Relative), CancellationToken));

        Assert.Equal(HttpStatusCode.OK, status);
        using var json = JsonDocument.Parse(body);
        // One name label per entity, and one text entry in each of the two locales.
        Assert.Equal(TypicalApplication.Entities + 2, json.RootElement.GetProperty("usedBy").GetArrayLength());
        Assert.True(elapsed < _answerTime, $"The where-used answer took {elapsed}.");
    }

    [Fact]
    public void A_typical_application_with_three_modules_compiles_within_the_time_limit()
    {
        string[] ids = ["module-a", "module-b", "module-c"];
        using var modules = TypicalApplication.WriteModules(ids);
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _, modules: ids);
        var installed = ModuleCatalog.Load(modules.Path).Modules;
        ApplicationCompiler.Compile(folder.Path, null, installed);

        var (result, elapsed) = Time(() => ApplicationCompiler.Compile(folder.Path, null, installed));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal(ids.Length, result.Model.Modules.Count);
        Assert.Equal(TypicalApplication.Entities + (ids.Length * 10), result.Model.Entities.Count);
        Assert.True(elapsed < _answerTime, $"The compile took {elapsed}.");
    }

    private static (CompilationResult Result, TimeSpan Elapsed) Time(Func<CompilationResult> compile)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = compile();
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"The compile took {stopwatch.Elapsed}.");
        return (result, stopwatch.Elapsed);
    }

    /// <summary>Sends the request once to warm up, then times the next answer, body included.</summary>
    private static async Task<(HttpStatusCode Status, string Body, TimeSpan Elapsed)> Answer(string name, Func<Task<HttpResponseMessage>> send)
    {
        (await send()).Dispose();

        var stopwatch = Stopwatch.StartNew();
        using var response = await send();
        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"{name} took {stopwatch.Elapsed}.");
        return (response.StatusCode, body, stopwatch.Elapsed);
    }

    private static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string url, string patch, string ifMatch)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, new Uri(url, UriKind.Relative))
        {
            Content = new StringContent(patch, Encoding.UTF8, "application/json-patch+json"),
        };
        request.Headers.Add("X-Axis-Csrf", "1");
        request.Headers.TryAddWithoutValidation("If-Match", ifMatch);
        return await client.SendAsync(request, CancellationToken);
    }

    private static WebApplicationFactory<Program> CreateFactory(string folder) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseTestPlatform(UnreachableDatabase);
            builder.UseSetting("Tenants:default:Hosts:0", "localhost");
            builder.UseSetting("Tenants:default:ConnectionString", UnreachableDatabase);
            builder.UseSetting("Tenants:default:SchemaConnectionString", UnreachableDatabase);
            builder.UseSetting("Extensions:Path", SamplePackages.Path);
            builder.UseSetting("Studio:Folders:0", folder);
            builder.UseSetting("Studio:AuthorRole", "author");
            builder.UseTestOidc("default");
            builder.UseTestAuthentication(new TestIdentity("hoa", "Hoa Author", "author"));
        });

    private static string EntityId(string folder)
    {
        using var file = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(folder, EntityFile)));
        return file.RootElement.GetProperty("id").GetString()!;
    }

    private static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
}
