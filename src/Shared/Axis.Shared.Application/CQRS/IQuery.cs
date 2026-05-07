using MediatR;

namespace Axis.Shared.Application.CQRS;

/// <summary>
/// Marker interface for queries. Queries must be side-effect-free read operations.
/// </summary>
public interface IQuery<TResponse> : IRequest<TResponse>;
