using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.BusinessObjects.Application;
using Axis.BusinessObjects.Application.Commands.CreateBusinessObjectRecord;
using Axis.BusinessObjects.Application.Commands.SaveBusinessObjectRecord;
using Axis.BusinessObjects.Application.Commands.SubmitBusinessObjectRecord;
using Axis.BusinessObjects.Application.Queries.GetBusinessObjectRecord;
using Axis.BusinessObjects.Application.Queries.ListBusinessObjectRecords;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class BusinessObjectRecordEndpoints
{
    public static IEndpointRouteBuilder MapBusinessObjectRecordEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/business-object-records")
            .RequireAuthorization(AxisApiServiceExtensions.WorkspaceAccessPolicy)
            .WithMetadata(ServiceProductEndpointMetadata.Instance)
            .WithTags("Business Object Records");

        group.MapGet("", List)
            .WithName("ListBusinessObjectRecords")
            .WithSummary("List business object records for the current workspace")
            .Produces<PagedResult<BusinessObjectRecordListItemDto>>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("/{objectKey}", Create)
            .WithName("CreateBusinessObjectRecord")
            .WithSummary("Create a draft record from the latest published business object version")
            .Produces<BusinessObjectRecordDetailDto>(StatusCodes.Status201Created)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(422)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        group.MapGet("/{recordId:guid}", Get)
            .WithName("GetBusinessObjectRecord")
            .WithSummary("Get a business object record")
            .Produces<BusinessObjectRecordDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPut("/{recordId:guid}", Save)
            .WithName("SaveBusinessObjectRecord")
            .WithSummary("Save a draft business object record")
            .Produces<BusinessObjectRecordDetailDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(422)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        group.MapPost("/{recordId:guid}/submit", Submit)
            .WithName("SubmitBusinessObjectRecord")
            .WithSummary("Run published field rules and submit a draft business object record")
            .Produces<BusinessObjectRecordSubmitResultDto>()
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409)
            .ProducesProblem(422)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> List(
        ISender mediator,
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? objectKey = null)
    {
        Result<PagedResult<BusinessObjectRecordListItemDto>> result = await mediator.Send(
            new ListBusinessObjectRecordsQuery(page, pageSize, objectKey),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> Create(
        string objectKey,
        [FromBody] CreateBusinessObjectRecordRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<BusinessObjectRecordDetailDto> result = await mediator.Send(
            new CreateBusinessObjectRecordCommand(
                objectKey,
                request.IdempotencyKey,
                request.Values ?? new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)),
            cancellationToken);
        return result.IsFailure
            ? result.ToProblemDetails()
            : Results.Created($"/api/business-object-records/{result.Value.Id}", result.Value);
    }

    private static async Task<IResult> Get(
        Guid recordId,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<BusinessObjectRecordDetailDto> result = await mediator.Send(
            new GetBusinessObjectRecordQuery(recordId),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> Save(
        Guid recordId,
        [FromBody] SaveBusinessObjectRecordRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<BusinessObjectRecordDetailDto> result = await mediator.Send(
            new SaveBusinessObjectRecordCommand(recordId, request.ExpectedRevision, request.Values),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }

    private static async Task<IResult> Submit(
        Guid recordId,
        [FromBody] SubmitBusinessObjectRecordRequest request,
        ISender mediator,
        CancellationToken cancellationToken)
    {
        Result<BusinessObjectRecordSubmitResultDto> result = await mediator.Send(
            new SubmitBusinessObjectRecordCommand(recordId, request.ExpectedRevision),
            cancellationToken);
        return result.IsFailure ? result.ToProblemDetails() : Results.Ok(result.Value);
    }
}
