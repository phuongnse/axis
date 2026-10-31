using System.Diagnostics;
using Axis.Core.Telemetry;
using Axis.Core.Tenancy;
using Axis.Server.Tenancy;
using Npgsql;

namespace Axis.Server.Http;

/// <summary>Marks an endpoint that runs under a query timeout, so a test can find the endpoints that do.</summary>
internal sealed record QueryTimeoutMetadata(string Setting);

internal static partial class QueryTimeout
{
    private const string Title = "The query took too long. Try again or narrow it.";
    private const string WriteTitle = "The write took too long. Try again.";

    /// <summary>
    /// Runs the whole request in a read-only transaction whose <c>statement_timeout</c> is the limit of
    /// <paramref name="setting"/>, see <see cref="QueryTimeoutSettings"/>. A read that runs past it is
    /// cancelled by PostgreSQL and answers 503 with a fixed title, a warning that names the subject and the
    /// duration, and a count in <see cref="AxisMetrics.QueryTimeouts"/>. The transaction begins on the
    /// request's first use of the database and is always rolled back.
    /// </summary>
    public static TBuilder WithQueryTimeout<TBuilder>(this TBuilder builder, string setting)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint => endpoint.Metadata.Add(new QueryTimeoutMetadata(setting)));
        builder.AddEndpointFilterFactory((factory, next) =>
        {
            var timeout = factory.ApplicationServices.GetRequiredService<QueryTimeoutSettings>().For(setting);
            return async invocation =>
            {
                var context = invocation.HttpContext;
                var database = context.RequestServices.GetRequiredService<TenantDatabase>();
                database.LimitReads(timeout);
                var start = Stopwatch.GetTimestamp();
                try
                {
                    return await next(invocation);
                }
                catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested && !context.Response.HasStarted && IsTimeout(exception))
                {
                    return Timeout(context, start, write: false);
                }
                finally
                {
                    await database.EndReadsAsync();
                }
            };
        });
        return builder;
    }

    /// <summary>
    /// Marks the endpoint as limited by <see cref="QueryTimeoutSettings.Write"/>, which its handler sets on its
    /// write transaction through <see cref="TenantDatabase.LimitWriteAsync"/>. A write that runs past it is
    /// cancelled by PostgreSQL and rolls back. It answers 503 with a fixed title, a warning that names the
    /// subject and the duration, and a count in <see cref="AxisMetrics.QueryTimeouts"/>.
    /// </summary>
    public static TBuilder WithWriteTimeout<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Add(endpoint => endpoint.Metadata.Add(new QueryTimeoutMetadata(QueryTimeoutSettings.Write)));
        builder.AddEndpointFilterFactory((factory, next) =>
        {
            var timeout = factory.ApplicationServices.GetRequiredService<QueryTimeoutSettings>().For(QueryTimeoutSettings.Write);
            return async invocation =>
            {
                var context = invocation.HttpContext;
                context.RequestServices.GetRequiredService<TenantDatabase>().LimitWrites(timeout);
                var start = Stopwatch.GetTimestamp();
                try
                {
                    return await next(invocation);
                }
                catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested && !context.Response.HasStarted && IsTimeout(exception))
                {
                    return Timeout(context, start, write: true);
                }
            };
        });
        return builder;
    }

    private static IResult Timeout(HttpContext context, long start, bool write)
    {
        var tenant = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current?.TenantId;
        var application = context.GetRouteValue("app") as string;
        var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(QueryTimeout).FullName!);
        var elapsed = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (write)
        {
            LogWrite(logger, Subject(context), elapsed);
        }
        else
        {
            Log(logger, Subject(context), elapsed);
        }

        if (tenant is not null)
        {
            AxisMetrics.RecordQueryTimeout(tenant, application);
        }

        return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: write ? WriteTitle : Title);
    }

    // SQLSTATE 57014 is query_canceled, which statement_timeout raises. The exception may be wrapped,
    // for example by EF Core.
    private static bool IsTimeout(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.QueryCanceled })
            {
                return true;
            }
        }

        return false;
    }

    // The name as the route spells it, never a query string, a task id or a value.
    private static string Subject(HttpContext context)
    {
        var values = context.Request.RouteValues;
        if ((values["entity"] ?? values["dataSource"]) is { } name)
        {
            return values["module"] is string module ? $"{module}/{name}" : $"{name}";
        }

        if (values["process"] is string process)
        {
            return values["module"] is string processModule ? $"process {processModule}/{process}" : $"process {process}";
        }

        if (values["name"] is string endpoint)
        {
            return $"endpoint {endpoint}";
        }

        if (values["page"] is string page)
        {
            return values["module"] is string pageModule ? $"page {pageModule}/{page}" : $"page {page}";
        }

        return values.ContainsKey("id") ? "task" : "page";
    }

    [LoggerMessage(LogLevel.Warning, "A read of {Subject} ran past the query timeout after {ElapsedMilliseconds} ms.")]
    private static partial void Log(ILogger logger, string subject, long elapsedMilliseconds);

    [LoggerMessage(LogLevel.Warning, "A write of {Subject} ran past the write timeout after {ElapsedMilliseconds} ms.")]
    private static partial void LogWrite(ILogger logger, string subject, long elapsedMilliseconds);
}
