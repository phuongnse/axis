using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Axis.Configuration.Packages;
using Axis.Configuration.Tests;

namespace Axis.Integration.Tests;

/// <summary>
/// What the package host answers to a <c>build</c> of the sample Python package <c>phone-numbers</c>, see
/// docs/reference/extension-packages.md#the-runner-protocol. The package goes over a real TCP connection to the host of this
/// process, with its <c>phonenumbers</c> wheel from the wheel cache that <c>scripts/setup.sh</c> fills, so pip installs it
/// offline. The scan is a stub script that reports what the test says. Each test checks that the extensions folder stays empty
/// and that the host leaves no <c>axis-package-build-*</c> folder behind. Each wait ends on the host's answer.
/// The tests run a shell script, so they are skipped on Windows.
/// </summary>
[UnsupportedOSPlatform("windows")]
public sealed class ApplicationPackageBuildTests
{
    private const string Wheel = "phonenumbers-9.0.43-";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_package_with_a_hashed_requirement_is_built_with_its_dependency_and_nothing_is_written_to_the_extensions_folder()
    {
        using var test = new Build(StubTrivy.NoFindings);

        var answer = await test.BuildAsync(Build.Files());

        var built = Assert.IsType<BuiltMessage>(answer);
        Assert.Contains("phonenumbers/__init__.py", built.Files.Keys);
        Assert.Contains("adapter.py", built.Files.Keys);
        Assert.True(test.ScannerRan);
        test.AssertNothingLeft();
    }

    [Fact]
    public async Task A_dependency_with_a_high_vulnerability_is_refused_naming_the_dependency()
    {
        using var test = new Build(StubTrivy.HighFinding);

        var answer = await test.BuildAsync(Build.Files());

        var refused = Assert.IsType<RefusedMessage>(answer);
        var problem = Assert.Single(refused.Problems);
        Assert.Equal("requirements.txt", problem.File);
        Assert.Contains("phonenumbers", problem.Message, StringComparison.Ordinal);
        test.AssertNothingLeft();
    }

    [Fact]
    public async Task A_requirement_without_a_hash_is_refused_naming_the_lock_file_and_nothing_is_scanned()
    {
        using var test = new Build(StubTrivy.NoFindings);
        var files = Build.Files();
        files["requirements.txt"] = Encoding.UTF8.GetBytes("phonenumbers==9.0.43\n");

        var answer = await test.BuildAsync(files);

        var refused = Assert.IsType<RefusedMessage>(answer);
        Assert.All(refused.Problems, problem => Assert.Equal("requirements.txt", problem.File));
        Assert.NotEmpty(refused.Problems);
        Assert.False(test.ScannerRan);
        test.AssertNothingLeft();
    }

    /// <summary>A host on an empty extensions folder with a stub scanner, and the files of the package to send.</summary>
    [UnsupportedOSPlatform("windows")]
    private sealed class Build : IDisposable
    {
        private readonly TemporaryFolder _extensions = new();
        private readonly StubTrivy _trivy;
        private readonly string[] _leftBefore = BuildFolders();

        public Build(string report)
        {
            _trivy = new StubTrivy(report);
        }

        public bool ScannerRan => _trivy.Ran;

        /// <summary>The manifest and adapter of the sample package, the wheel of <c>phonenumbers</c> and a <c>requirements.txt</c> that locks it by hash.</summary>
        public static Dictionary<string, byte[]> Files()
        {
            var wheel = WheelPath();
            var name = Path.GetFileName(wheel);
            var content = File.ReadAllBytes(wheel);
            var sample = Path.Combine(AppContext.BaseDirectory, "SamplePythonPackage");
            return new Dictionary<string, byte[]>
            {
                ["axis-package.json"] = File.ReadAllBytes(Path.Combine(sample, "axis-package.json")),
                ["adapter.py"] = File.ReadAllBytes(Path.Combine(sample, "adapter.py")),
                [$"wheels/{name}"] = content,
                ["requirements.txt"] = Encoding.UTF8.GetBytes($"./wheels/{name} --hash=sha256:{Convert.ToHexStringLower(SHA256.HashData(content))}\n"),
            };
        }

        public async Task<RunnerMessage> BuildAsync(Dictionary<string, byte[]> files)
        {
            await using var host = await TestPackageHost.StartAsync(
                _extensions.Path,
                settings: new Dictionary<string, string?> { ["PackageBuild:Trivy"] = _trivy.Command });
            using var client = new TcpClient();
            await client.ConnectAsync(host.Endpoint, CancellationToken);
            await using var stream = client.GetStream();
            await stream.WriteAsync(Encoding.UTF8.GetBytes(RunnerProtocol.Write(new BuildMessage(files)) + "\n"), CancellationToken);
            using var reader = new StreamReader(stream, new UTF8Encoding(false));
            var line = await reader.ReadLineAsync(CancellationToken) ?? throw new InvalidOperationException("The host closed the connection with no answer.");
            return RunnerProtocol.Read(line);
        }

        /// <summary>Checks that the extensions folder is empty and that the host deleted every temporary copy it made.</summary>
        public void AssertNothingLeft()
        {
            Assert.Empty(Directory.GetFileSystemEntries(_extensions.Path));
            Assert.Equal(_leftBefore, BuildFolders());
        }

        public void Dispose()
        {
            _extensions.Dispose();
            _trivy.Dispose();
        }

        private static string[] BuildFolders() =>
            [.. Directory.GetDirectories(Path.GetTempPath(), "axis-package-build-*").Order(StringComparer.Ordinal)];

        private static string WheelPath()
        {
            var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "axis-tools", "wheels");
            var wheels = Directory.Exists(cache) ? Directory.GetFiles(cache, $"{Wheel}*.whl") : [];
            return wheels.Length > 0
                ? wheels[0]
                : throw new InvalidOperationException($"The wheel '{Wheel}*.whl' is not in '{cache}'. Run scripts/setup.sh to fill the wheel cache.");
        }
    }
}
