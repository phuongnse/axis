using Axis.Configuration.Compilation;
using Axis.Configuration.Modules;

namespace Axis.Configuration.Tests;

public sealed class ModuleNamesTests
{
    [Fact]
    public void Module_processes_pages_and_policies_carry_their_module_and_the_applications_own_carry_none()
    {
        var modules = ModuleCatalog.Load(Path.Combine(AppContext.BaseDirectory, "FixtureModules")).Modules;

        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "module-app"), null, modules);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [("checklists/ChecklistReview", "checklists"), ("ItemReview", null)],
            result.Names.Processes.Select(process => (process.Name, process.Module)));
        Assert.Equal(
            [("checklists/Checklists", "checklists"), ("Items", null)],
            result.Names.Pages.Select(page => (page.Name, page.Module)));
        Assert.Equal(
            [("checklists/ChecklistAccess", "checklists"), ("ItemAccess", null)],
            result.Names.Policies.Select(policy => (policy.Name, policy.Module)));
    }
}
