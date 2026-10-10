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
        Assert.Equal(["Department", "LineItem", "PurchaseRequest", "Supplier"], result.Model.Entities.Select(entity => entity.Name));
        Assert.Equal(2, result.Model.Seeds.Count);
        Assert.Equal("purchasing", Assert.Single(result.Model.Sites).Path);
        Assert.Equal(["en", "vi"], result.Model.Texts.Select(texts => texts.Locale));

        Assert.True(result.Model.TryGetEntity("PurchaseRequest", out var purchaseRequest));
        Assert.True(purchaseRequest.TryGetField("number", out var number));
        Assert.NotNull(number.Sequence);
        Assert.Equal("PurchaseRequestNumber", number.Sequence.Name);
        Assert.Equal("PR-{yyyy}-{n:5}", number.Sequence.Format);
    }
}
