namespace Axis.Shared.Application.Identity;

/// <summary>
/// Application-layer accessor for the current request's authenticated user.
/// Returns null fields when the request is anonymous (e.g. token-based form submission).
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? tenantId { get; }
}
