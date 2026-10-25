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
                catch (Exception exception) when (!context.RequestAborted.IsCancellationRequested && IsTimeout(exception))
                {
                    var tenant = context.RequestServices.GetRequiredService<ITenantContextAccessor>().Current?.TenantId;
                    var application = context.GetRouteValue("app") as string;
                    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(QueryTimeout).FullName!);
                    Log(logger, Subject(context), (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                    if (tenant is not null)
                    {
                        AxisMetrics.RecordQueryTimeout(tenant, application);
                    }

                    return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: Title);
                }
                finally
                {
                    await database.EndReadsAsync();
                }
            };
        });
        return builder;
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

    // The name as the route spells it, never a query string or a value.
    private static string Subject(HttpContext context)
    {
        var values = context.Request.RouteValues;
        var name = values["entity"] ?? values["dataSource"];
        if (name is null)
        {
            return values["page"] is string page ? $"page {page}" : "page";
        }

        return values["module"] is string module ? $"{module}/{name}" : $"{name}";
    }

    [LoggerMessage(LogLevel.Warning, "A read of {Subject} ran past the query timeout after {ElapsedMilliseconds} ms.")]
    private static partial void Log(ILogger logger, string subject, long elapsedMilliseconds);
}
