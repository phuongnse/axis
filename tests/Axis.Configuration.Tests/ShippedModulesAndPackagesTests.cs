using Axis.Configuration.Compilation;
using Axis.Configuration.Modules;
using Axis.Configuration.Packages;
using Axis.Configuration.Templates;

namespace Axis.Configuration.Tests;

/// <summary>The modules, applications and packages that Axis ships compile and load with no diagnostic, labels included.</summary>
public sealed class ShippedModulesAndPackagesTests
{
    [Fact]
    public void The_sample_package_catalog_loads_with_a_label_on_each_package()
    {
        var catalog = PackageCatalog.Load(SamplePackages.Path);

        Assert.Empty(catalog.Diagnostics);
        var package = Assert.Single(catalog.Packages, package => package.Manifest.Id == "payment-references");
        Assert.False(string.IsNullOrWhiteSpace(package.Manifest.Label));
        Assert.All(catalog.Packages, installed => Assert.InRange(installed.Manifest.Label.Length, 1, 100));
    }

    [Fact]
    public void The_built_in_approvals_application_compiles_with_the_built_in_modules()
    {
        var modules = ModuleCatalog.Load(Path.Combine(AppContext.BaseDirectory, "BuiltInModules")).Modules;

        var catalog = TemplateCatalog.Load(Path.Combine(AppContext.BaseDirectory, "BuiltInApplications"));
        Assert.Empty(catalog.Problems);
        var template = Assert.Single(catalog.Newest);

        var compiled = ApplicationCompiler.CompileFiles(template.Files, null, modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.NotNull(compiled.Model);
    }

    [Fact]
    public void The_purchase_requests_sample_compiles_with_the_sample_modules_and_packages()
    {
        var compiled = ApplicationCompiler.Compile(
            Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"),
            SamplePackages.Versions,
            SampleModules.Modules);

        Assert.Empty(compiled.Diagnostics);
        Assert.NotNull(compiled.Model);
    }
}
