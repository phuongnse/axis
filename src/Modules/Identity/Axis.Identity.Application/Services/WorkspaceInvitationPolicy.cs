namespace Axis.Identity.Application.Services;

public sealed record WorkspaceInvitationPolicy(
    TimeSpan InvitationLifetime,
    TimeSpan HandoffLifetime,
    int DefaultPageSize,
    int MaximumPageSize)
{
    public WorkspaceInvitationPolicy Validate()
    {
        if (InvitationLifetime <= TimeSpan.Zero || HandoffLifetime <= TimeSpan.Zero)
            throw new InvalidOperationException("Invitation and handoff lifetimes must be positive.");
        if (DefaultPageSize <= 0 || MaximumPageSize < DefaultPageSize)
            throw new InvalidOperationException("Invitation page sizes are invalid.");
        return this;
    }
}
