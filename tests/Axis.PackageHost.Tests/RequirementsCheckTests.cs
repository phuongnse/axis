using Axis.Configuration.Packages;
using Axis.Configuration.Tests;

namespace Axis.PackageHost.Tests;

public sealed class RequirementsCheckTests
{
    private static readonly PackageManifest _manifest = new("p", "1.0.0", "P", PackageLanguage.Python, null, "adapter.py", "requirements.txt", []);

    private static IReadOnlyList<string> Check(string requirements)
    {
        using var folder = new TemporaryFolder().With("requirements.txt", requirements);
        return RequirementsCheck.Check(folder.Path, _manifest);
    }

    [Fact]
    public void Hashed_requirements_with_comments_continuations_and_options_pass()
    {
        var problems = Check("""
            # a comment line
            --index-url https://example.test/simple

            first==1.0 \
                --hash=sha256:aaa \
                --hash=sha256:bbb # trailing comment
            second==2.0 --hash=sha256:ccc
            """);

        Assert.Empty(problems);
    }

    [Fact]
    public void A_requirement_without_a_hash_is_named_with_its_line_number()
    {
        var problems = Check("first==1.0 --hash=sha256:aaa\n\nsecond>=2.0; python_version > '3.8'\n");

        Assert.Equal(["'requirements.txt' line 3 has no --hash: second>=2.0."], problems);
    }

    [Theory]
    [InlineData("-r other.txt")]
    [InlineData("--requirement=other.txt")]
    [InlineData("-c constraints.txt")]
    [InlineData("-e .")]
    [InlineData("-rother.txt")]
    public void A_line_that_includes_other_requirements_is_refused(string line)
    {
        var problems = Check($"{line}\n");

        Assert.Equal(["'requirements.txt' line 1 includes other requirements, which cannot be checked for hashes."], problems);
    }

    [Fact]
    public void The_sample_python_package_locks_its_library_with_a_hash()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "SamplePythonPackage");
        var check = PackageCatalog.CheckFolder(folder);

        Assert.Empty(check.Diagnostics);
        Assert.Empty(RequirementsCheck.Check(folder, check.Package!.Manifest));
        var requirements = Assert.Single(File.ReadAllLines(Path.Combine(folder, "requirements.txt")));
        Assert.Matches(@"^phonenumbers==\S+ --hash=sha256:[0-9a-f]{64}$", requirements);
    }

    [Fact]
    public void A_missing_file_is_refused()
    {
        using var folder = new TemporaryFolder();

        Assert.Equal(["The lock file 'requirements.txt' is missing."], RequirementsCheck.Check(folder.Path, _manifest));
    }
}
