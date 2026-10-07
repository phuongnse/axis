using Axis.Configuration.Compilation;

namespace Axis.Configuration.Tests;

public sealed class SampleApplicationTests
{
    [Fact]
    public void Purchase_request_sample_compiles_without_diagnostics()
    {
        var result = ApplicationCompiler.Compile(Path.Combine(AppContext.BaseDirectory, "Samples", "purchase-requests"));

        Assert.Empty(result.Diagnostics);
        Assert.NotNull(result.Model);
        Assert.Equal(["Department", "PurchaseRequest", "Supplier"], result.Model.Entities.Select(entity => entity.Name));
        Assert.Equal(2, result.Model.Seeds.Count);
        Assert.Equal("purchasing", Assert.Single(result.Model.Sites).Path);
        Assert.Equal(["en", "vi"], result.Model.Texts.Select(texts => texts.Locale));
    }
}
