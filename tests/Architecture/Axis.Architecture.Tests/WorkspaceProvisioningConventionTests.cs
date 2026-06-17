using Axis.Identity.Contracts;
using FluentAssertions;

namespace Axis.Architecture.Tests;

/// <summary>
/// Keeps <see cref="WorkspaceModuleNames"/> in sync with modules that ship
/// <c>WorkspaceVerifiedHandler</c> (workspace schema provisioning).
/// </summary>
public sealed class WorkspaceProvisioningConventionTests
{
    [Fact]
    public void WorkspaceModuleNames_WhenEnumerated_MatchModulesWithWorkspaceVerifiedHandler()
    {
        string[] expected = DiscoverProvisioningModuleSlugs();
        WorkspaceModuleNames.All.Should().BeEquivalentTo(expected);
    }

    private static string[] DiscoverProvisioningModuleSlugs()
    {
        string modulesDir = Path.Combine(RepositoryRoot, "src", "Modules");
        List<string> slugs = [];

        foreach (string moduleDir in Directory.GetDirectories(modulesDir))
        {
            string moduleName = Path.GetFileName(moduleDir);
            if (string.Equals(moduleName, "Identity", StringComparison.Ordinal))
            {
                continue;
            }

            bool hasHandler = Directory
                .EnumerateFiles(moduleDir, "WorkspaceVerifiedHandler.cs", SearchOption.AllDirectories)
                .Any();

            if (hasHandler)
            {
                slugs.Add(moduleName.ToLowerInvariant());
            }
        }

        return slugs.OrderBy(s => s, StringComparer.Ordinal).ToArray();
    }

    private static string RepositoryRoot
    {
        get
        {
            DirectoryInfo? dir = new(AppContext.BaseDirectory);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "Axis.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate repository root (Axis.sln) from test bin directory.");
        }
    }
}
