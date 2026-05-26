using Axis.Shared.Application.Behaviors;
using Axis.Shared.Application.CQRS;
using FluentAssertions;
using FluentAssertions.Specialized;
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
        TestCommandValidator[] validators = new[] { new TestCommandValidator() };
        ValidationBehavior<TestCommand, string> behavior = new ValidationBehavior<TestCommand, string>(validators);
        _next.Invoke().Returns("ok");

        await behavior.Handle(new TestCommand("Alice"), _next, CancellationToken.None);

        await _next.Received(1).Invoke();
    }

    [Fact]
    public async Task ValidationBehavior_WhenCommandIsInvalid_ThrowsValidationException()
    {
        TestCommandValidator[] validators = new[] { new TestCommandValidator() };
        ValidationBehavior<TestCommand, string> behavior = new ValidationBehavior<TestCommand, string>(validators);

        Func<Task<string>> act = async () =>
            await behavior.Handle(new TestCommand(""), _next, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage("*Name is required*");
    }

    [Fact]
    public async Task ValidationBehavior_WhenMultipleViolations_ReportsAllErrors()
    {
        TestCommandValidator[] validators = new[] { new TestCommandValidator() };
        ValidationBehavior<TestCommand, string> behavior = new ValidationBehavior<TestCommand, string>(validators);
        string tooLong = new string('x', 51);

        Func<Task<string>> act = async () =>
            await behavior.Handle(new TestCommand(tooLong), _next, CancellationToken.None);
        ExceptionAssertions<ValidationException> ex = await act.Should().ThrowAsync<ValidationException>();
        ex.Which.Errors.Should().HaveCount(1); // only MaxLength violated
    }

    [Fact]
    public async Task ValidationBehavior_WhenNoValidators_PassesThroughToNext()
    {
        ValidationBehavior<TestCommand, string> behavior = new ValidationBehavior<TestCommand, string>([]);
        _next.Invoke().Returns("ok");
        string result = await behavior.Handle(new TestCommand("Alice"), _next, CancellationToken.None);

        result.Should().Be("ok");
    }
}
