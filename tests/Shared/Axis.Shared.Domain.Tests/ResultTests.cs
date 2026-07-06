using Axis.Shared.Domain.Primitives;
using FluentAssertions;

namespace Axis.Shared.Domain.Tests;

public class ResultTests
{
    [Fact]
    public void Result_WhenSuccess_IsSuccessful()
    {
        Result result = Result.Success();

        result.IsSuccess.Should().BeTrue();
        result.IsFailure.Should().BeFalse();
    }

    [Fact]
    public void Result_WhenFailure_IsNotSuccessful()
    {
        Result result = Result.Failure("Something went wrong");

        result.IsSuccess.Should().BeFalse();
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Something went wrong");
    }

    [Fact]
    public void Result_WhenFailureHasProblemCode_ExposesProblemCode()
    {
        Result result = Result.Failure(
            ErrorCodes.BusinessRule,
            "Something went wrong",
            "identity.example.problem");

        result.ProblemCode.Should().Be("identity.example.problem");
    }

    [Fact]
    public void ResultT_WhenSuccess_HoldsValue()
    {
        Result<int> result = Result<int>.Success(42);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public void ResultT_WhenFailure_HasError()
    {
        Result<int> result = Result<int>.Failure("Not found");

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be("Not found");
    }

    [Fact]
    public void ResultT_WhenAccessingValueOnFailure_Throws()
    {
        Result<int> result = Result<int>.Failure("error");

        Func<int> act = () => result.Value;

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Result_WhenAccessingErrorOnSuccess_Throws()
    {
        Result result = Result.Success();

        Func<string> act = () => result.Error;

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
