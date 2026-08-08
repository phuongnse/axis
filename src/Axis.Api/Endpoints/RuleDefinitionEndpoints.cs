using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Middleware;
using Axis.Authorization.Contracts;
using Axis.Identity.Contracts;
using Axis.Rules.Application;
using Axis.Rules.Application.Commands.ActivateRuleDefinitionVersion;
using Axis.Rules.Application.Commands.ArchiveRuleDefinition;
using Axis.Rules.Application.Commands.CreateRuleDefinition;
using Axis.Rules.Application.Commands.CreateRuleDefinitionVersion;
using Axis.Rules.Application.Commands.DeactivateRuleDefinition;
using Axis.Rules.Application.Commands.SaveRuleDefinitionDraft;
using Axis.Rules.Application.Queries.GetRuleDefinition;
using Axis.Rules.Application.Queries.GetRuleExpressionLanguage;
using Axis.Rules.Application.Queries.ListRuleDefinitions;
using Axis.Rules.Application.Queries.ProjectRuleCondition;
using Axis.Rules.Application.Queries.SearchRuleExpressionGuide;
using Axis.Rules.Application.Queries.SimulateRuleDefinition;
using Axis.Rules.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Axis.Api.Endpoints;

public static class RuleDefinitionEndpoints
{
    public static IEndpointRouteBuilder MapRuleDefinitionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/rules")
            .RequireAuthorization(AxisApiServiceExtensions.WorkspaceAccessPolicy)
            .RequireRateLimiting(AxisApiServiceExtensions.RulesRateLimiterPolicy)
            .WithTags("Rules");

        group.MapGet("", List)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("ListRuleDefinitions")
            .WithSummary("List built-in and workspace rule definitions")
            .Produces<PagedResult<RuleDefinitionSummaryDto>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/actions", Actions)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("GetRuleDefinitionCollectionActions")
            .WithSummary("Get authorized rule definition collection actions")
            .Produces<RuleDefinitionCollectionActionsDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("", Create)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("CreateRuleDefinition")
            .WithSummary("Create a workspace rule draft")
            .Produces<RuleDefinitionDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(409)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/expression-language", GetExpressionLanguage)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("GetRuleExpressionLanguage")
            .WithSummary("Get the versioned typed-expression capabilities available for rule authoring")
            .Produces<RuleExpressionLanguageDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/condition/project", ProjectCondition)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("ProjectRuleCondition")
            .WithSummary("Validate and present a visual rule condition")
            .Produces<RuleConditionProjectionDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/expression-language/guide", SearchExpressionGuide)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("SearchRuleExpressionGuide")
            .WithSummary("Browse and search the contextual rule expression guide")
            .Produces<RuleExpressionGuideDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("/{definitionKey}", Get)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("GetRuleDefinition")
            .WithSummary("Get a built-in or workspace rule definition")
            .Produces<RuleDefinitionDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/{definitionKey}/draft", SaveDraft)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("SaveRuleDefinitionDraft")
            .WithSummary("Save a workspace rule draft")
            .Produces<RuleDefinitionDetailDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{definitionKey}/versions", CreateVersion)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("CreateRuleDefinitionVersion")
            .WithSummary("Create an immutable workspace rule version from the current draft")
            .Produces<RuleDefinitionDetailDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/{definitionKey}/active-version", ActivateVersion)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("ActivateRuleDefinitionVersion")
            .WithSummary("Activate one exact immutable workspace rule version")
            .Produces<RuleDefinitionDetailDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapDelete("/{definitionKey}/active-version", Deactivate)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("DeactivateRuleDefinition")
            .WithSummary("Deactivate the workspace rule definition for new bindings")
            .Produces<RuleDefinitionDetailDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{definitionKey}/archive", Archive)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("ArchiveRuleDefinition")
            .WithSummary("Archive a workspace rule definition")
            .Produces<RuleDefinitionDetailDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{definitionKey}/draft/simulate", SimulateDraft)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("SimulateRuleDefinitionDraft")
            .WithSummary("Simulate the current workspace rule draft")
            .Produces<RuleSimulationResultDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{definitionKey}/versions/{version:int}/simulate", SimulateVersion)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("SimulateRuleDefinitionVersion")
            .WithSummary("Simulate one exact immutable rule version")
            .Produces<RuleSimulationResultDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/authoring/project", ProjectAuthoring)
            .WithName("ProjectRuleAuthoring")
            .WithSummary("Project one rule authoring source to canonical rule logic")
            .Produces<RuleAuthoringProjectionDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403);

        group.MapPost("/authoring/complete", CompleteAuthoring)
            .WithName("CompleteRuleAuthoring")
            .WithSummary("Complete the rule authoring language at a cursor position")
            .Produces<IReadOnlyList<RuleAuthoringCompletionDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        return app;
    }

    private static async Task<IResult> List(
        ISender mediator,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] RuleOrigin? origin = null,
        [FromQuery] RuleLifecycleStatus? status = null,
        [FromQuery] string? query = null,
        [FromQuery] string? language = null)
    {
        Result<PagedResult<RuleDefinitionSummaryDto>> result = await mediator.Send(
            new ListRuleDefinitionsQuery(page, pageSize, origin, status, query, language),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> Create(
        [FromBody] CreateRuleDefinitionRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<RuleDefinitionDetailDto> result = await mediator.Send(
            new CreateRuleDefinitionCommand(
                request.Name,
                request.Description),
            cancellationToken);
        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Created($"/api/rules/{result.Value.DefinitionKey}", result.Value);
    }

    private static async Task<IResult> Actions(
        ICurrentUser currentUser,
        ICurrentSubject currentSubject,
        IProductAuthorizationService authorization,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return Result.Failure<RuleDefinitionCollectionActionsDto>(
                ErrorCodes.Forbidden,
                "Current workspace scope is required.",
                RulesProblemCodes.WorkspaceScopeRequired).ToProblemDetails();

        ProductAuthorizationDecision decision = await RuleAuthorization.AuthorizeAsync(
            authorization,
            workspaceId,
            currentSubject.Subject,
            RuleProductActions.DefinitionManage,
            RuleProductActions.DefinitionResourceType,
            resourceKey: null,
            CorrelationId(context),
            cancellationToken);

        return decision.IsUnavailable
            ? Result.Failure<RuleDefinitionCollectionActionsDto>(
                ErrorCodes.Unavailable,
                "Product authorization is temporarily unavailable.",
                RulesProblemCodes.AuthorizationUnavailable).ToProblemDetails()
            : Results.Ok(new RuleDefinitionCollectionActionsDto(decision.IsAllowed));
    }

    private static async Task<IResult> GetExpressionLanguage(
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<RuleExpressionLanguageDto> result = await mediator.Send(
            new GetRuleExpressionLanguageQuery(),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> ProjectCondition(
        [FromBody] ProjectRuleConditionRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<RuleConditionProjectionDto> result = await mediator.Send(
            new ProjectRuleConditionQuery(request),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> SearchExpressionGuide(
        [FromBody] SearchRuleExpressionGuideRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<RuleExpressionGuideDto> result = await mediator.Send(
            new SearchRuleExpressionGuideQuery(request),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> Get(
        string definitionKey,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<RuleDefinitionDetailDto> result = await mediator.Send(
            new GetRuleDefinitionQuery(definitionKey),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> SaveDraft(
        string definitionKey,
        [FromBody] SaveRuleDefinitionDraftRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<RuleDefinitionDetailDto> result = await mediator.Send(
            new SaveRuleDefinitionDraftCommand(
                definitionKey,
                request.ExpectedRevision,
                request.Name,
                request.Description,
                request.Inputs,
                request.Condition),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> CreateVersion(
        string definitionKey,
        [FromBody] RuleRevisionRequest request,
        ISender mediator,
        CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(
            new CreateRuleDefinitionVersionCommand(definitionKey, request.ExpectedRevision),
            cancellationToken));

    private static async Task<IResult> ActivateVersion(
        string definitionKey,
        [FromBody] ActivateRuleDefinitionVersionRequest request,
        ISender mediator,
        CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(
            new ActivateRuleDefinitionVersionCommand(definitionKey, request.Version, request.ExpectedRevision),
            cancellationToken));

    private static async Task<IResult> Deactivate(
        string definitionKey,
        [FromBody] RuleRevisionRequest request,
        ISender mediator,
        CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(
            new DeactivateRuleDefinitionCommand(definitionKey, request.ExpectedRevision),
            cancellationToken));

    private static async Task<IResult> Archive(
        string definitionKey,
        [FromBody] RuleRevisionRequest request,
        ISender mediator,
        CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(
            new ArchiveRuleDefinitionCommand(definitionKey, request.ExpectedRevision),
            cancellationToken));

    private static async Task<IResult> SimulateDraft(
        string definitionKey,
        [FromBody] SimulateRuleDraftRequest request,
        ISender mediator,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Result<RuleSimulationResultDto> result = await mediator.Send(
            new SimulateRuleDefinitionQuery(
                definitionKey,
                null,
                request.Inputs,
                CorrelationId(context)),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> SimulateVersion(
        string definitionKey,
        int version,
        [FromBody] SimulateRuleVersionRequest request,
        ISender mediator,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        Result<RuleSimulationResultDto> result = await mediator.Send(
            new SimulateRuleDefinitionQuery(
                definitionKey,
                version,
                request.Inputs,
                CorrelationId(context)),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static IResult ProjectAuthoring(
        [FromBody] ProjectRuleAuthoringRequest request,
        RuleAuthoringLanguageService authoring) =>
        Results.Ok(authoring.Project(
            request.Source,
            request.Inputs,
            request.ExpressionLanguageVersion,
            request.Language));

    private static IResult CompleteAuthoring(
        [FromBody] CompleteRuleAuthoringRequest request,
        RuleAuthoringLanguageService authoring) =>
        Results.Ok(authoring.Complete(
            request.Text,
            request.Cursor,
            request.Inputs,
            request.ExpressionLanguageVersion));

    private static string CorrelationId(HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out object? value) && value is string correlationId
            ? correlationId
            : context.TraceIdentifier;

    private static IResult ToResult(Result<RuleDefinitionDetailDto> result) =>
        result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
}
