using System.Diagnostics;
using Microsoft.AspNetCore.Http.Features;

namespace Axis.Server.Telemetry;

/// <summary>
/// Sets the <c>traceId</c> member of every problem details answer to the request's 32-hex W3C trace id,
/// the value a user can report and an operator can search for in logs and traces.
/// </summary>
internal static class ProblemTraceId
{
    public const string Member = "traceId";

    public static void Apply(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var activity = Activity.Current ?? context.HttpContext.Features.Get<IHttpActivityFeature>()?.Activity;
        if (activity is null)
        {
            context.ProblemDetails.Extensions.Remove(Member);
            return;
        }

        context.ProblemDetails.Extensions[Member] = activity.TraceId.ToHexString();
    }
}
