using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.Authorization.Application;
using Axis.Authorization.Contracts;
using Axis.BusinessObjects.Application;
using Axis.Identity.Contracts;
using Axis.Rules.Application;

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
        HttpContext context,
        CurrentUser currentUser,
        ICurrentSubject currentSubject,
        IAuthorizationAdministratorAuthority administrators,
        IProductAuthorizationService authorization,
        CancellationToken cancellationToken)
    {
        bool isWorkspaceAdministrator = await administrators.IsAdministratorAsync(
            currentUser.WorkspaceId,
            currentSubject.Subject,
            cancellationToken);

        List<string> availableContributionIds = [];
        if (isWorkspaceAdministrator)
        {
            availableContributionIds.Add(MembershipsContribution);
            availableContributionIds.Add(ServiceIdentitiesContribution);
            availableContributionIds.Add(ProductRolesContribution);
        }

        if (await CanBrowseBusinessObjectsAsync(
                currentUser.WorkspaceId,
                currentSubject,
                authorization,
                context.TraceIdentifier,
                cancellationToken))
        {
            availableContributionIds.Add(BusinessObjectsContribution);
        }

        ProductAuthorizationDecision rulesDecision = await RuleAuthorization.AuthorizeAsync(
            authorization,
            currentUser.WorkspaceId,
            currentSubject.Subject,
            RuleProductActions.DefinitionRead,
            RuleProductActions.DefinitionResourceType,
            null,
            context.TraceIdentifier,
            cancellationToken);
        if (rulesDecision.IsAllowed)
            availableContributionIds.Add(RulesContribution);

        if (isWorkspaceAdministrator)
            availableContributionIds.Add(SolutionsContribution);

        return Results.Ok(new ModuleNavigationAvailabilityDto(availableContributionIds));
    }

    private static async Task<bool> CanBrowseBusinessObjectsAsync(
        Guid workspaceId,
        ICurrentSubject currentSubject,
        IProductAuthorizationService authorization,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ProductAuthorizationDecision decision = await BusinessObjectAuthorization.AuthorizeAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            BusinessObjectProductActions.DefinitionRead,
            BusinessObjectProductActions.DefinitionResourceType,
            null,
            correlationId,
            cancellationToken);
        if (decision.IsAllowed)
            return true;
        if (decision.IsUnavailable)
            return false;

        decision = await BusinessObjectAuthorization.AuthorizeAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            BusinessObjectProductActions.DefinitionReadPublished,
            BusinessObjectProductActions.DefinitionResourceType,
            null,
            correlationId,
            cancellationToken);
        return decision.IsAllowed;
    }
}

public sealed record ModuleNavigationAvailabilityDto(
    IReadOnlyList<string> AvailableContributionIds);
