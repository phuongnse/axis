using System.Security.Claims;
using Axis.Identity.Contracts;
using OpenIddict.Abstractions;

namespace Axis.Api.Infrastructure;

public sealed class HttpContextCurrentSubject(IHttpContextAccessor accessor) : ICurrentSubject
{
    private ClaimsPrincipal? Principal => accessor.HttpContext?.User;

    public SubjectReference Subject
    {
        get
        {
            string? sub = Principal?.GetClaim(OpenIddictConstants.Claims.Subject);
            if (!Guid.TryParse(sub, out Guid id))
                throw new InvalidOperationException("No valid subject claim is available.");

            string? kind = Principal?.FindFirst("subject_kind")?.Value;
            if (string.IsNullOrEmpty(kind)
                || string.Equals(kind, "human", StringComparison.Ordinal))
                return SubjectReference.Human(id);
            if (string.Equals(kind, "service", StringComparison.Ordinal))
                return SubjectReference.Service(id);
            throw new InvalidOperationException("The authenticated subject kind is invalid.");
        }
    }

    public string DisplayName =>
        Principal?.GetClaim(OpenIddictConstants.Claims.Name) is { Length: > 0 } displayName
            ? displayName
            : throw new InvalidOperationException("No actor display-name claim is available.");
}
