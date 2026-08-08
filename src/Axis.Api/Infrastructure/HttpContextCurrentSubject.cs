using Axis.Identity.Contracts;
using OpenIddict.Abstractions;

namespace Axis.Api.Infrastructure;

public sealed class HttpContextCurrentSubject(IHttpContextAccessor accessor) : ICurrentSubject
{
    public SubjectReference Subject
    {
        get
        {
            string? sub = accessor.HttpContext?.User.GetClaim(OpenIddictConstants.Claims.Subject);
            if (!Guid.TryParse(sub, out Guid id))
                throw new InvalidOperationException("No valid subject claim is available.");

            string? kind = accessor.HttpContext?.User.FindFirst("subject_kind")?.Value;
            if (string.IsNullOrEmpty(kind)
                || string.Equals(kind, "human", StringComparison.Ordinal))
                return SubjectReference.Human(id);
            if (string.Equals(kind, "service", StringComparison.Ordinal))
                return SubjectReference.Service(id);
            throw new InvalidOperationException("The authenticated subject kind is invalid.");
        }
    }
}
