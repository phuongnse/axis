using System.Diagnostics;
using Axis.Shared.Domain.Primitives;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Axis.Shared.Application.Behaviors;

/// <summary>
/// MediatR pipeline behavior that provides cross-cutting Debug-level entry/exit traces,
/// Warning-level logs for Result failures and validation rejections, and Error-level logs
/// for unhandled exceptions. Registered as the outermost behavior so it wraps
/// ValidationBehavior and the handler itself.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        string requestType = typeof(TRequest).Name;
        Stopwatch sw = Stopwatch.StartNew();

        logger.LogDebug("Handling {RequestType}", requestType);

        try
        {
            TResponse response = await next();

            if (response is Result result && result.IsFailure)
            {
                logger.LogWarning(
                    "Handled {RequestType} in {ElapsedMs}ms — failed: [{ErrorCode}] {Error}",
                    requestType, sw.ElapsedMilliseconds,
                    result.ErrorCode ?? "none", result.Error);
            }
            else
            {
                logger.LogDebug(
                    "Handled {RequestType} in {ElapsedMs}ms",
                    requestType, sw.ElapsedMilliseconds);
            }

            return response;
        }
        catch (ValidationException ex)
        {
            // Expected pipeline rejection — not a system error.
            logger.LogWarning(
                "Handled {RequestType} in {ElapsedMs}ms — validation failed: {Errors}",
                requestType, sw.ElapsedMilliseconds,
                string.Join("; ", ex.Errors.Select(e => e.ErrorMessage)));
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Handler failed for {RequestType} after {ElapsedMs}ms",
                requestType, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
