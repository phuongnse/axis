using MediatR;

namespace Axis.Shared.Application.CQRS;

/// <summary>
/// Handler for commands that return a value.
/// </summary>
public interface ICommandHandler<TCommand, TResponse> : IRequestHandler<TCommand, TResponse>
    where TCommand : ICommand<TResponse>;

/// <summary>
/// Handler for commands that return no value.
/// </summary>
public interface ICommandHandler<TCommand> : IRequestHandler<TCommand>
    where TCommand : ICommand;
