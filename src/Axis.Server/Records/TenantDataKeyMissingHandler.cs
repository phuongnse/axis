using Axis.Data.Records;
using Microsoft.AspNetCore.Diagnostics;

namespace Axis.Server.Records;

/// <summary>
/// Answers a write of a sensitive value for a tenant with no data key or no lookup key with a 500 problem and an error log line that
/// names the tenant. The problem holds no tenant, key or exception text. Any other exception goes on to the next handler.
/// </summary>
internal sealed partial class TenantDataKeyMissingHandler(IProblemDetailsService problems, ILogger<TenantDataKeyMissingHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not TenantDataKeyMissingException missing)
        {
            return false;
        }

        LogMissingKey(logger, missing.TenantId, missing.Kind.ToString().ToLowerInvariant());
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = StatusCodes.Status500InternalServerError, Title = "A sensitive value could not be stored." },
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Tenant {TenantId} has no {Kind} key, so a sensitive value was not written.")]
    private static partial void LogMissingKey(ILogger logger, string tenantId, string kind);
}
