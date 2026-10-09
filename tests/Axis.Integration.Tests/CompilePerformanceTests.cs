using System.Diagnostics;
using Axis.Configuration.Diagnostics;
using Axis.Configuration.Releases;

namespace Axis.Integration.Tests;

/// <summary>
/// An application at the top of the typical size compiles and is stored as a release within the
/// time limit, and one with planted errors reports all of them in a single compile.
/// </summary>
public sealed class CompilePerformanceTests(ConfigurationDatabaseFixture database) : IClassFixture<ConfigurationDatabaseFixture>
{
    // Well above the expected time, so shared CI runners do not make the test flaky, and still low
    // enough to catch a slowdown of an order of magnitude.
    private static readonly TimeSpan _compileTime = TimeSpan.FromSeconds(10);

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_typical_application_compiles_and_is_stored_within_the_time_limit()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: false, out _);

        var (result, elapsed) = await CompileAsync(folder.Path);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Release);
        Assert.Equal(1 + 2 + TypicalApplication.Entities + TypicalApplication.DataSources + TypicalApplication.Rules, result.Release.Resources.Count);
        Assert.NotNull(result.Model);
        Assert.Equal(TypicalApplication.Entities, result.Model.Entities.Count);
        Assert.True(result.Model.Entities.Sum(entity => entity.Fields.Count) >= 2_000);
        Assert.Equal(TypicalApplication.DataSources, result.Model.DataSources.Count);
        Assert.True(elapsed < _compileTime, $"The compile took {elapsed}.");
    }

    [Fact]
    public async Task A_typical_application_with_errors_reports_every_one_in_one_compile_within_the_time_limit()
    {
        using var folder = TypicalApplication.Write(Guid.NewGuid(), withErrors: true, out var erroneousFiles);

        var (result, elapsed) = await CompileAsync(folder.Path);

        Assert.Null(result.Release);
        Assert.All(result.Diagnostics, diagnostic => Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity));
        Assert.Equal(
            erroneousFiles.Order(StringComparer.Ordinal),
            result.Diagnostics.Select(diagnostic => diagnostic.File).Distinct().Order(StringComparer.Ordinal));
        Assert.True(elapsed < _compileTime, $"The compile took {elapsed}.");
    }

    /// <summary>Times the whole compile: loading the folder, checking it and storing the release.</summary>
    private async Task<(ReleaseCompilationResult Result, TimeSpan Elapsed)> CompileAsync(string path)
    {
        await using var context = database.CreateContext();
        var stopwatch = Stopwatch.StartNew();
        var result = await ReleaseCompiler.CompileAsync(path, context, CancellationToken);
        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"The compile took {stopwatch.Elapsed}.");
        return (result, stopwatch.Elapsed);
    }
}
