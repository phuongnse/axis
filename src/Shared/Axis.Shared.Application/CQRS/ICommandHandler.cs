using Axis.Shared.Domain.Primitives;
using MediatR;

namespace Axis.Shared.Application.CQRS;

/// <summary>
/// Handler for commands that return a value wrapped in Result.
/// </summary>
public interface ICommandHandler<TCommand, TValue> : IRequestHandler<TCommand, Result<TValue>>
    where TCommand : ICommand<TValue>;

/// <summary>
/// Handler for commands that return Result (success/failure with no value).
/// </summary>
public interface ICommandHandler<TCommand> : IRequestHandler<TCommand, Result>
    where TCommand : ICommand;
