using Axis.Rules.Contracts;
using Axis.Rules.Application.Queries.ListFieldRuleDefinitions;
using MediatR;

namespace Axis.Api.Endpoints;

public static class FieldRuleDefinitionEndpoints
{
    public static IEndpointRouteBuilder MapFieldRuleDefinitionEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/rules")
            .RequireAuthorization()
            .WithTags("Rules");

        group.MapGet("/field-rule-definitions", List)
            .WithName("ListFieldRuleDefinitions")
            .WithSummary("List system field rule definitions")
            .Produces<IReadOnlyList<FieldRuleDefinitionDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        return app;
    }

    private static async Task<IResult> List(ISender mediator, CancellationToken ct)
    {
        IReadOnlyList<FieldRuleDefinitionDto> result = await mediator.Send(
            new ListFieldRuleDefinitionsQuery(),
            ct);

        return Results.Ok(result);
    }
}
