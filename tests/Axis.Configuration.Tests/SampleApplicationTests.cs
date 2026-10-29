using Axis.Configuration.Compilation;
using Axis.Configuration.Releases;

namespace Axis.Configuration.Tests;

public sealed class SampleApplicationTests
{
    [Fact]
    public void Purchase_request_sample_compiles_without_diagnostics()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), SamplePackages.Versions);

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal(["Department", "LineItem", "PurchaseRequest", "Supplier"], result.Model.Entities.Select(entity => entity.Name));
        Assert.Equal(2, result.Model.Seeds.Count);
        Assert.Equal("purchasing", Assert.Single(result.Model.Sites).Path);
        Assert.Equal(["en", "vi"], result.Model.Texts.Select(texts => texts.Locale));
        Assert.Equal("PurchaseRequestApproval", Assert.Single(result.Model.Processes).Name);
        Assert.Equal("PurchaseRequestReview", Assert.Single(result.Model.Forms).Name);
        Assert.Equal(["department-manager", "employee", "finance-reviewer", "operator", "purchasing-system"], result.Model.Roles.Select(role => role.Name));
        Assert.Equal("SubmitPurchaseRequest", Assert.Single(result.Model.Endpoints).Name);

        Assert.True(result.Model.TryGetEntity("PurchaseRequest", out var purchaseRequest));
        Assert.True(purchaseRequest.TryGetField("number", out var number));
        Assert.NotNull(number.Sequence);
        Assert.Equal("PurchaseRequestNumber", number.Sequence.Name);
        Assert.Equal("PR-{yyyy}-{n:5}", number.Sequence.Format);
    }

    [Fact]
    public void Purchase_request_sample_held_in_memory_compiles_to_the_content_hash_of_its_folder()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests");
        var files = Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
            .Select(file => new ResourceContent(Path.GetRelativePath(folder, file).Replace('\\', '/'), File.ReadAllText(file)))
            .ToList();

        var fromFolder = ApplicationCompiler.Compile(folder, SamplePackages.Versions);
        var fromFiles = ApplicationCompiler.CompileFiles(files, SamplePackages.Versions, null);

        Assert.Empty(fromFiles.Diagnostics);
        Assert.NotNull(fromFolder.ContentHash);
        Assert.Equal(fromFolder.ContentHash, fromFiles.ContentHash);
    }

    [Fact]
    public void E2e_fixture_application_compiles_without_diagnostics_and_declares_the_sample_roles()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "e2e-app"));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal(["department-manager", "employee", "finance-reviewer"], result.Model.Roles.Select(role => role.Name));
    }

    [Fact]
    public void Subprocess_fixture_application_compiles_without_diagnostics_with_a_parent_that_starts_a_child()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "subprocess-app"));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal(["ChildJob", "ParentJob"], result.Model.Processes.Select(process => process.Name).Order());
    }

    [Fact]
    public void Host_site_fixture_application_compiles_without_diagnostics_with_one_site()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Fixtures", "host-site-app"));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal("host-site", Assert.Single(result.Model.Sites).Path);
    }

    [Fact]
    public void Purchase_request_start_condition_message_has_an_English_and_a_Vietnamese_text()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"), SamplePackages.Versions);

        Assert.NotNull(result.Model);
        var process = Assert.Single(result.Model.Processes);
        Assert.NotNull(process.StartCondition);
        Assert.Equal("purchaseRequest.cannotSubmit", process.StartCondition.Message.TextKey);
        foreach (var locale in new[] { "en", "vi" })
        {
            var texts = Assert.Single(result.Model.Texts, texts => texts.Locale == locale);
            Assert.True(texts.Texts.TryGetValue("purchaseRequest.cannotSubmit", out var text), $"The {locale} texts have no purchaseRequest.cannotSubmit.");
            Assert.False(string.IsNullOrWhiteSpace(text));
        }
    }
}
