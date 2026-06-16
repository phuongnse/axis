using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.ValueObjects;

public class TenantSlugTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-corp")]
    [InlineData("my-tenant-123")]
    [InlineData("a")]
    public void TenantSlug_WhenValueIsValid_IsCreatedSuccessfully(string value)
    {
        Result<TenantSlug> result = TenantSlug.Create(value);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Uppercase")]
    [InlineData("has space")]
    [InlineData("has_underscore")]
    [InlineData("has.dot")]
    [InlineData("-starts-with-dash")]
    [InlineData("ends-with-dash-")]
    public void TenantSlug_WhenValueIsInvalid_ReturnsFailure(string value)
    {
        Result<TenantSlug> result = TenantSlug.Create(value);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void TenantSlug_WhenExceedsMaxLength_ReturnsFailure()
    {
        string tooLong = new string('a', 64);
        Result<TenantSlug> result = TenantSlug.Create(tooLong);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("63");
    }

    [Fact]
    public void TenantSlug_WhenValuesAreIdentical_AreEqual()
    {
        TenantSlug a = TenantSlug.Create("acme-corp").Value;
        TenantSlug b = TenantSlug.Create("acme-corp").Value;

        a.Should().Be(b);
    }
}
