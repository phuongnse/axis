using Axis.Identity.Contracts;
using FluentAssertions;

namespace Axis.Architecture.Tests;

/// <summary>
/// Keeps <see cref="TenantModuleNames"/> in sync with modules that ship
/// <c>TeamAccountVerifiedHandler</c> (tenant schema provisioning).
/// </summary>
public sealed class TenantProvisioningConventionTests
{
    [Fact]
    public void TenantModuleNames_WhenEnumerated_MatchModulesWithTeamAccountVerifiedHandler()
    {
        string[] expected = DiscoverProvisioningModuleSlugs();
        TenantModuleNames.All.Should().BeEquivalentTo(expected);
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
                .EnumerateFiles(moduleDir, "TeamAccountVerifiedHandler.cs", SearchOption.AllDirectories)
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
