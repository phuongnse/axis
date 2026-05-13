using Axis.Shared.Domain.Primitives;
using MediatR;

namespace Axis.Shared.Application.CQRS;

/// <summary>
/// Marker interface for commands that return a value wrapped in Result.
/// Use for write operations with a meaningful return (e.g. created resource Id).
/// </summary>
public interface ICommand<TValue> : IRequest<Result<TValue>>;

/// <summary>
/// Marker interface for commands that return no value.
/// Handlers return Result (success/failure) — never throw for expected business failures.
/// </summary>
public interface ICommand : IRequest<Result>;
