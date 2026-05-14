using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Shared.Domain.Tests;

public class ResultTests
{
    [Fact]
    public void Result_WhenSuccess_IsSuccessful()
    {
        var result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void Result_WhenFailure_IsNotSuccessful()
    {
        var result = Result.Failure("Something went wrong");

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Something went wrong");
    }

    [Fact]
    public void ResultT_WhenSuccess_HoldsValue()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void ResultT_WhenFailure_HasError()
    {
        var result = Result<int>.Failure("Not found");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Not found");
    }

    [Fact]
    public void ResultT_WhenAccessingValueOnFailure_Throws()
    {
        var result = Result<int>.Failure("error");

        var act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Result_WhenAccessingErrorOnSuccess_Throws()
    {
        var result = Result.Success();

        var act = () => result.Error;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ResultT_WhenImplicitlyCreatedFromValue_IsSuccess()
    {
        Result<string> result = "hello";

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("hello");
    }
}
