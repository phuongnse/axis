using Axis.Shared.Application.Behaviors;
using Axis.Shared.Application.CQRS;
using FluentAssertions;
using FluentValidation;
using MediatR;
using NSubstitute;

namespace Axis.Shared.Application.Tests;

public class ValidationBehaviorTests
{
    private record TestCommand(string Name) : ICommand<string>;

    private class TestCommandValidator : AbstractValidator<TestCommand>
    {
        public TestCommandValidator()
        {
            RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
            RuleFor(x => x.Name).MaximumLength(50).WithMessage("Name must be 50 characters or fewer.");
        }
    }

    private readonly RequestHandlerDelegate<string> _next =
        Substitute.For<RequestHandlerDelegate<string>>();

    [Fact]
    public async Task ValidationBehavior_WhenNoValidationErrors_PassesThroughToNext()
    {
        var validators = new[] { new TestCommandValidator() };
        var behavior = new ValidationBehavior<TestCommand, string>(validators);
        _next.Invoke().Returns("ok");

        await behavior.Handle(new TestCommand("Alice"), _next, CancellationToken.None);

        await _next.Received(1).Invoke();
    }

    [Fact]
    public async Task ValidationBehavior_WhenCommandIsInvalid_ThrowsValidationException()
    {
        var validators = new[] { new TestCommandValidator() };
        var behavior = new ValidationBehavior<TestCommand, string>(validators);

        var act = async () =>
            await behavior.Handle(new TestCommand(""), _next, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Name is required*");
    }

    [Fact]
    public async Task ValidationBehavior_WhenMultipleViolations_ReportsAllErrors()
    {
        var validators = new[] { new TestCommandValidator() };
        var behavior = new ValidationBehavior<TestCommand, string>(validators);
        var tooLong = new string('x', 51);

        var act = async () =>
            await behavior.Handle(new TestCommand(tooLong), _next, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().HaveCount(1); // only MaxLength violated
    }

    [Fact]
    public async Task ValidationBehavior_WhenNoValidators_PassesThroughToNext()
    {
        var behavior = new ValidationBehavior<TestCommand, string>([]);
        _next.Invoke().Returns("ok");

        var result = await behavior.Handle(new TestCommand("Alice"), _next, CancellationToken.None);

        result.Should().Be("ok");
    }
}
