using Axis.Api.Extensions;
using Axis.Rules.Application;
using Axis.Rules.Application.Commands.ArchiveRuleDefinition;
using Axis.Rules.Application.Commands.CreateRuleDefinition;
using Axis.Rules.Application.Commands.PublishRuleDefinition;
using Axis.Rules.Application.Commands.SaveRuleDefinitionDraft;
using Axis.Rules.Application.Commands.StartRuleDefinitionDraft;
using Axis.Rules.Application.Queries.GetRuleDefinition;
using Axis.Rules.Application.Queries.GetRuleExpressionLanguage;
using Axis.Rules.Application.Queries.ListRuleContextSchemas;
using Axis.Rules.Application.Queries.ListRuleDefinitions;
using Axis.Rules.Application.Queries.SimulateRuleDefinition;
using Axis.Rules.Contracts;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class RuleDefinitionEndpoints
{
    public static IEndpointRouteBuilder MapRuleDefinitionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/rules")
            .RequireAuthorization()
            .WithTags("Rules");

        group.MapGet("", List)
            .WithName("ListRuleDefinitions")
            .WithSummary("List system and workspace rule definitions")
            .Produces<PagedResult<RuleDefinitionSummaryDto>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403);

        group.MapPost("", Create)
            .WithName("CreateRuleDefinition")
            .WithSummary("Create a workspace rule draft")
            .Produces<RuleDefinitionDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(409);

        group.MapGet("/context-schemas", ListContextSchemas)
            .WithName("ListRuleContextSchemas")
            .WithSummary("List rule context schemas available to the workspace")
            .Produces<IReadOnlyList<RuleContextSchemaDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        group.MapGet("/expression-language", GetExpressionLanguage)
            .WithName("GetRuleExpressionLanguage")
            .WithSummary("Get the versioned typed-expression capabilities available for rule authoring")
            .Produces<RuleExpressionLanguageDto>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        group.MapGet("/{definitionKey}", Get)
            .WithName("GetRuleDefinition")
            .WithSummary("Get a system or workspace rule definition")
            .Produces<RuleDefinitionDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/{definitionKey}/draft", SaveDraft)
            .WithName("SaveRuleDefinitionDraft")
            .WithSummary("Save a workspace rule draft")
            .Produces<RuleDefinitionDetailDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapPost("/{definitionKey}/draft", StartDraft)
            .WithName("StartRuleDefinitionDraft")
            .WithSummary("Start the next draft from a published workspace rule")
            .Produces<RuleDefinitionDetailDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapPost("/{definitionKey}/publish", Publish)
            .WithName("PublishRuleDefinition")
            .WithSummary("Publish an immutable workspace rule version")
            .Produces<RuleDefinitionDetailDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapPost("/{definitionKey}/archive", Archive)
            .WithName("ArchiveRuleDefinition")
            .WithSummary("Archive a workspace rule definition")
            .Produces<RuleDefinitionDetailDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapPost("/{definitionKey}/simulate", Simulate)
            .WithName("SimulateRuleDefinition")
            .WithSummary("Simulate a workspace rule draft or exact published version")
            .Produces<RuleSimulationResultDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    private static async Task<IResult> List(
        ISender mediator,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] RuleScope? scope = null,
        [FromQuery] RuleOrigin? origin = null,
        [FromQuery] RuleLifecycleStatus? status = null)
    {
        Result<PagedResult<RuleDefinitionSummaryDto>> result = await mediator.Send(
            new ListRuleDefinitionsQuery(page, pageSize, scope, origin, status),
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
                request.Description,
                request.Scope,
                request.ContextKey,
                request.ContextSchemaVersion,
                request.OutcomeKind),
            cancellationToken);
        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Created($"/api/rules/{result.Value.DefinitionKey}", result.Value);
    }

    private static async Task<IResult> ListContextSchemas(
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RuleContextSchemaDto>> result = await mediator.Send(
            new ListRuleContextSchemasQuery(),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
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
                request.Scope,
                request.ContextKey,
                request.ContextSchemaVersion,
                request.OutcomeKind,
                request.Parameters,
                request.Condition,
                request.Outcome),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> StartDraft(
        string definitionKey,
        [FromBody] RuleRevisionRequest request,
        ISender mediator,
        CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(
            new StartRuleDefinitionDraftCommand(definitionKey, request.ExpectedRevision),
            cancellationToken));

    private static async Task<IResult> Publish(
        string definitionKey,
        [FromBody] RuleRevisionRequest request,
        ISender mediator,
        CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(
            new PublishRuleDefinitionCommand(definitionKey, request.ExpectedRevision),
            cancellationToken));

    private static async Task<IResult> Archive(
        string definitionKey,
        [FromBody] RuleRevisionRequest request,
        ISender mediator,
        CancellationToken cancellationToken) =>
        ToResult(await mediator.Send(
            new ArchiveRuleDefinitionCommand(definitionKey, request.ExpectedRevision),
            cancellationToken));

    private static async Task<IResult> Simulate(
        string definitionKey,
        [FromBody] SimulateRuleRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<RuleSimulationResultDto> result = await mediator.Send(
            new SimulateRuleDefinitionQuery(
                definitionKey,
                request.DefinitionVersion,
                request.Parameters,
                request.Context,
                request.CorrelationId),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static IResult ToResult(Result<RuleDefinitionDetailDto> result) =>
        result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
}
