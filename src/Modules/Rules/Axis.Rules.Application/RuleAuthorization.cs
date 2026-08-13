using Axis.Identity.Contracts;

namespace Axis.Rules.Application;

public static class RuleAuthorization
{
    public static async Task<WorkspaceProductBuilderDecision> AuthorizeAsync(
        IWorkspaceProductBuilderAuthorization authorization,
        Guid workspaceId,
        SubjectReference subject,
        CancellationToken cancellationToken)
    {
        if (subject.Id == Guid.Empty || subject.Kind != SubjectKind.Human)
            return WorkspaceProductBuilderDecision.Denied;

        try
        {
            return await authorization.AuthorizeAsync(workspaceId, subject, cancellationToken);
        }
        catch
        {
            return WorkspaceProductBuilderDecision.Unavailable;
        }
    }
}
