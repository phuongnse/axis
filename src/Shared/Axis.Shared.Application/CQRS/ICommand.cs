using MediatR;

namespace Axis.Shared.Application.CQRS;

/// <summary>
/// Marker interface for commands that return a value.
/// Use for write operations with a meaningful return (e.g. created resource Id).
/// </summary>
public interface ICommand<TResponse> : IRequest<TResponse>;

/// <summary>
/// Marker interface for commands that return no value (fire-and-forget writes).
/// </summary>
public interface ICommand : IRequest;
