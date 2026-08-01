using Axis.Api.Extensions;
using Axis.Rules.Application.Commands.CreateRuleBinding;
using Axis.Rules.Application.Commands.DeleteRuleBinding;
using Axis.Rules.Application.Commands.UpdateRuleBinding;
using Axis.Rules.Application.Queries.ListRuleBindingUsage;
using Axis.Rules.Contracts;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class RuleBindingEndpoints
{
    public static IEndpointRouteBuilder MapRuleBindingEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder bindings = app.MapGroup("/api/rule-bindings")
            .RequireAuthorization()
            .WithTags("Rules");

        bindings.MapPost("", Create)
            .WithName("CreateRuleBinding")
            .WithSummary("Bind an exact rule version to a consumer target")
            .Produces<RuleBindingDto>(StatusCodes.Status201Created)
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        bindings.MapPut("/{bindingId:guid}", Update)
            .WithName("UpdateRuleBinding")
            .WithSummary("Update a rule binding without changing the rule definition")
            .Produces<RuleBindingDto>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(409);

        bindings.MapDelete("/{bindingId:guid}", Delete)
            .WithName("DeleteRuleBinding")
            .WithSummary("Remove a rule binding without removing its rule definition")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(401).ProducesProblem(403).ProducesProblem(404);

        app.MapGet("/api/rules/{definitionKey}/bindings", ListUsage)
            .RequireAuthorization()
            .WithName("ListRuleBindingUsage")
            .WithTags("Rules")
            .WithSummary("Show where an exact rule version is currently used")
            .Produces<IReadOnlyList<RuleBindingUsageDto>>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403);

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

    private static async Task<IResult> Delete(
        Guid bindingId,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result result = await mediator.Send(new DeleteRuleBindingCommand(bindingId), cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.NoContent();
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
}
