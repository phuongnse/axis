using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Authorization.Application;
using Axis.Identity.Application.Queries.ListEligibleWorkspaces;
using Axis.Identity.Contracts;
using MediatR;

namespace Axis.Api.Endpoints;

public static class ModuleNavigationEndpoints
{
    private const string MembershipsContribution = "identity.memberships";
    private const string ServiceIdentitiesContribution = "identity.service-identities";
    private const string ProductRolesContribution = "authorization.product-roles";
    private const string BusinessObjectsContribution = "businessObjects.definitions";
    private const string RulesContribution = "rules.fieldDefinitions";
    private const string SolutionsContribution = "solutions.management";

    public static IEndpointRouteBuilder MapModuleNavigationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/module-navigation", GetAvailability)
            .RequireAuthorization(AxisApiServiceExtensions.WorkspaceAccessPolicy)
            .WithName("GetModuleNavigationAvailability")
            .WithSummary("Get server-authoritative module navigation availability")
            .WithTags("App Shell")
            .Produces<ModuleNavigationAvailabilityDto>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> GetAvailability(
        CurrentUser currentUser,
        ICurrentSubject currentSubject,
        IAuthorizationAdministratorAuthority administrators,
        IWorkspaceProductBuilderAuthorization productBuilderAuthorization,
        ISender sender,
        CancellationToken cancellationToken)
    {
        bool isLifecycleAdministrator = await administrators.IsAdministratorAsync(
            currentUser.WorkspaceId,
            currentSubject.Subject,
            cancellationToken);
        bool isOrganizationAdministrator = false;
        if (isLifecycleAdministrator && currentSubject.Subject.Kind == SubjectKind.Human)
        {
            IReadOnlyList<EligibleWorkspaceDto> workspaces = await sender.Send(
                new ListEligibleWorkspacesQuery(
                    currentSubject.Subject.Id,
                    currentUser.WorkspaceId),
                cancellationToken);
            isOrganizationAdministrator = workspaces
                .SingleOrDefault(workspace => workspace.IsCurrent)
                ?.OrganizationId is not null;
        }

        List<string> availableContributionIds = [];
        if (isOrganizationAdministrator)
            availableContributionIds.Add(MembershipsContribution);
        if (isLifecycleAdministrator)
        {
            availableContributionIds.Add(ServiceIdentitiesContribution);
            availableContributionIds.Add(ProductRolesContribution);
        }

        WorkspaceProductBuilderDecision builderDecision = await AuthorizeProductBuilderAsync(
            productBuilderAuthorization,
            currentUser.WorkspaceId,
            currentSubject.Subject,
            cancellationToken);
        if (builderDecision.IsAllowed)
        {
            availableContributionIds.Add(BusinessObjectsContribution);
            availableContributionIds.Add(RulesContribution);
        }

        if (isLifecycleAdministrator)
            availableContributionIds.Add(SolutionsContribution);

        return Results.Ok(new ModuleNavigationAvailabilityDto(availableContributionIds));
    }

    private static async Task<WorkspaceProductBuilderDecision> AuthorizeProductBuilderAsync(
        IWorkspaceProductBuilderAuthorization authorization,
        Guid workspaceId,
        SubjectReference subject,
        CancellationToken cancellationToken)
    {
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

public sealed record ModuleNavigationAvailabilityDto(
    IReadOnlyList<string> AvailableContributionIds);
