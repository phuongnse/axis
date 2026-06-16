using Axis.Identity.Domain.ValueObjects;
using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Identity.Domain.Tests.ValueObjects;

public class WorkspaceSlugTests
{
    [Theory]
    [InlineData("acme")]
    [InlineData("acme-corp")]
    [InlineData("my-workspace-123")]
    [InlineData("a")]
    public void WorkspaceSlug_WhenValueIsValid_IsCreatedSuccessfully(string value)
    {
        Result<WorkspaceSlug> result = WorkspaceSlug.Create(value);

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
    public void WorkspaceSlug_WhenValueIsInvalid_ReturnsFailure(string value)
    {
        Result<WorkspaceSlug> result = WorkspaceSlug.Create(value);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void WorkspaceSlug_WhenExceedsMaxLength_ReturnsFailure()
    {
        string tooLong = new string('a', 64);
        Result<WorkspaceSlug> result = WorkspaceSlug.Create(tooLong);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Contain("63");
    }

    [Fact]
    public void WorkspaceSlug_WhenValuesAreIdentical_AreEqual()
    {
        WorkspaceSlug a = WorkspaceSlug.Create("acme-corp").Value;
        WorkspaceSlug b = WorkspaceSlug.Create("acme-corp").Value;

        a.Should().Be(b);
    }
}
