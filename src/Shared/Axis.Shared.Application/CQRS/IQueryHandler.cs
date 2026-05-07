using MediatR;

namespace Axis.Shared.Application.CQRS;

/// <summary>
/// Handler for queries.
/// </summary>
public interface IQueryHandler<TQuery, TResponse> : IRequestHandler<TQuery, TResponse>
    where TQuery : IQuery<TResponse>;
