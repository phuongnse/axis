using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Middleware;
using Axis.Rules.Application;
using Axis.Rules.Application.Commands.CreateRuleBinding;
using Axis.Rules.Application.Commands.DeleteRuleBinding;
using Axis.Rules.Application.Commands.UpdateRuleBinding;
using Axis.Rules.Application.Queries.GetRuleBinding;
using Axis.Rules.Application.Queries.ListRuleBindingUsage;
using Axis.Rules.Contracts;
using Axis.Shared.Application.Identity;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Axis.Api.Endpoints;

public static class RuleBindingEndpoints
{
    public static IEndpointRouteBuilder MapRuleBindingEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder bindings = app.MapGroup("/api/rule-bindings")
            .RequireAuthorization(AxisApiServiceExtensions.WorkspaceAccessPolicy)
            .RequireRateLimiting(AxisApiServiceExtensions.RulesRateLimiterPolicy)
            .WithTags("Rules");

        bindings.MapPost("", Create)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("CreateRuleBinding")
            .WithSummary("Bind an exact rule version to a consumer target")
            .Produces<RuleBindingDto>(StatusCodes.Status201Created)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        bindings.MapGet("/{bindingId:guid}", Get)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("GetRuleBinding")
            .WithSummary("Get a rule binding")
            .Produces<RuleBindingDto>()
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        bindings.MapPut("/{bindingId:guid}", Update)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("UpdateRuleBinding")
            .WithSummary("Update a rule binding without changing the rule definition")
            .Produces<RuleBindingDto>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        bindings.MapDelete("/{bindingId:guid}", Delete)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithName("DeleteRuleBinding")
            .WithSummary("Remove a rule binding without removing its rule definition")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        bindings.MapPost("/{bindingId:guid}/evaluate", Evaluate)
            .WithName("EvaluateRuleBinding")
            .WithSummary("Evaluate one rule binding against a transient consumer context")
            .Produces<RuleEvaluationResult>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(422);

        app.MapGet("/api/rules/{definitionKey}/bindings", ListUsage)
            .RequireAuthorization(AxisApiServiceExtensions.WorkspaceAccessPolicy)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .RequireRateLimiting(AxisApiServiceExtensions.RulesRateLimiterPolicy)
            .WithName("ListRuleBindingUsage")
            .WithTags("Rules")
            .WithSummary("Show where an exact rule version is currently used")
            .Produces<IReadOnlyList<RuleBindingUsageDto>>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }

    private static async Task<IResult> Create(
        [FromBody] CreateRuleBindingRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<RuleBindingDto> result = await mediator.Send(
            new CreateRuleBindingCommand(request), cancellationToken);
        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Created($"/api/rule-bindings/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> Update(
        Guid bindingId,
        [FromBody] UpdateRuleBindingRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<RuleBindingDto> result = await mediator.Send(
            new UpdateRuleBindingCommand(bindingId, request), cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> Get(
        Guid bindingId,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<RuleBindingDto> result = await mediator.Send(
            new GetRuleBindingQuery(bindingId), cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> Delete(
        Guid bindingId,
        [FromBody] DeleteRuleBindingRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result result = await mediator.Send(
            new DeleteRuleBindingCommand(bindingId, request.ExpectedRevision), cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.NoContent();
    }

    private static async Task<IResult> Evaluate(
        Guid bindingId,
        [FromBody] EvaluateRuleBindingRequest request,
        ICurrentUser currentUser,
        IRuleBindingEvaluator evaluator,
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (currentUser.workspaceId is not Guid workspaceId)
            return Result.Failure<RuleEvaluationResult>(
                ErrorCodes.Forbidden,
                "Current workspace scope is required.",
                RulesProblemCodes.WorkspaceScopeRequired).ToProblemDetails();

        RuleEvaluationResult result = await evaluator.EvaluateBindingAsync(
            new RuleBindingEvaluationRequest(
                workspaceId,
                bindingId,
                request.Context,
                CorrelationId(context),
                request.BindingRevision),
            cancellationToken);
        return result.IsSuccess ? Results.Ok(result) : ToEvaluationProblem(result);
    }

    private static async Task<IResult> ListUsage(
        string definitionKey,
        [FromQuery] int version,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<IReadOnlyList<RuleBindingUsageDto>> result = await mediator.Send(
            new ListRuleBindingUsageQuery(definitionKey, version), cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static IResult ToEvaluationProblem(RuleEvaluationResult result)
    {
        (string errorCode, string problemCode) = result.ErrorCode switch
        {
            "binding_invalid" => (ErrorCodes.InvalidInput, "rules.binding_invalid"),
            "binding_not_found" => (ErrorCodes.NotFound, "rules.binding_not_found"),
            "binding_revision_not_found" => (ErrorCodes.NotFound, "rules.binding_revision_not_found"),
            "binding_revision_disabled" => (ErrorCodes.BusinessRule, "rules.binding_revision_disabled"),
            _ => (ErrorCodes.BusinessRule, RulesProblemCodes.EvaluationFailed),
        };
        return Result.Failure<RuleEvaluationResult>(errorCode, result.Error ?? "Rule binding evaluation failed.", problemCode)
            .ToProblemDetails();
    }

    private static string CorrelationId(HttpContext context) =>
        context.Items.TryGetValue(CorrelationIdMiddleware.HttpContextItemKey, out object? value) && value is string correlationId
            ? correlationId
            : context.TraceIdentifier;
}
