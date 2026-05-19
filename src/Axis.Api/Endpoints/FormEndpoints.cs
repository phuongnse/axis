using Axis.Api.Authorization;
using Axis.Api.Extensions;
using Axis.Api.Infrastructure;
using Axis.FormBuilder.Application.Commands.AddFieldToForm;
using Axis.FormBuilder.Application.Commands.CreateForm;
using Axis.FormBuilder.Application.Commands.DeleteForm;
using Axis.FormBuilder.Application.Commands.RemoveFieldFromForm;
using Axis.FormBuilder.Application.Commands.ReorderFormFields;
using Axis.FormBuilder.Application.Commands.UpdateForm;
using Axis.FormBuilder.Application.Queries.GetFormById;
using Axis.FormBuilder.Application.Queries.GetFormPicker;
using Axis.FormBuilder.Application.Queries.GetForms;
using Axis.Shared.Application;
using Axis.Shared.Domain.Primitives;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Axis.Api.Endpoints;

public static class FormEndpoints
{
    public static IEndpointRouteBuilder MapFormEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/forms")
            .RequireAuthorization();

        // ── Form CRUD ──────────────────────────────────────────────────────────

        group.MapGet("/", GetForms)
            .RequireAuthorization(Permissions.Form.DefinitionRead)
            .WithName("GetForms")
            .WithSummary("List all forms for the organization (paginated)")
            .WithTags("FormBuilder")
            .Produces<PagedResult<FormSummaryDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        group.MapPost("/", CreateForm)
            .RequireAuthorization(Permissions.Form.DefinitionWrite)
            .WithName("CreateForm")
            .WithSummary("Create a new form definition")
            .WithTags("FormBuilder")
            .Produces<object>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(409);

        group.MapGet("/picker", GetFormPicker)
            .RequireAuthorization(Permissions.Form.DefinitionRead)
            .WithName("GetFormPicker")
            .WithSummary("Return a flat list of forms for workflow step picker dropdowns")
            .WithTags("FormBuilder")
            .Produces<IReadOnlyList<GetFormPickerDto>>()
            .ProducesProblem(401)
            .ProducesProblem(403);

        group.MapGet("/{formId:guid}", GetFormById)
            .RequireAuthorization(Permissions.Form.DefinitionRead)
            .WithName("GetFormById")
            .WithSummary("Get a form definition by ID")
            .WithTags("FormBuilder")
            .Produces<FormDetailDto>()
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/{formId:guid}", UpdateForm)
            .RequireAuthorization(Permissions.Form.DefinitionWrite)
            .WithName("UpdateForm")
            .WithSummary("Update a form's name and description")
            .WithTags("FormBuilder")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409);

        group.MapDelete("/{formId:guid}", DeleteForm)
            .RequireAuthorization(Permissions.Form.DefinitionWrite)
            .WithName("DeleteForm")
            .WithSummary("Soft-delete a form (rejected if referenced by workflow steps)")
            .WithTags("FormBuilder")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404)
            .ProducesProblem(409);

        // ── Field management ───────────────────────────────────────────────────

        group.MapPost("/{formId:guid}/fields", AddField)
            .RequireAuthorization(Permissions.Form.DefinitionWrite)
            .WithName("AddFieldToForm")
            .WithSummary("Add a field (or section divider) to a form")
            .WithTags("FormBuilder")
            .Produces<object>(201)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapDelete("/{formId:guid}/fields/{fieldId:guid}", RemoveField)
            .RequireAuthorization(Permissions.Form.DefinitionWrite)
            .WithName("RemoveFieldFromForm")
            .WithSummary("Remove a field from a form")
            .WithTags("FormBuilder")
            .Produces(204)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        group.MapPut("/{formId:guid}/fields/reorder", ReorderFields)
            .RequireAuthorization(Permissions.Form.DefinitionWrite)
            .WithName("ReorderFormFields")
            .WithSummary("Reorder fields in a form")
            .WithTags("FormBuilder")
            .Produces(204)
            .ProducesProblem(400)
            .ProducesProblem(401)
            .ProducesProblem(403)
            .ProducesProblem(404);

        return app;
    }

    private static async Task<IResult> GetForms(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        PagedResult<FormSummaryDto> result = await mediator.Send(
            new GetFormsQuery(currentUser.OrgId, page, pageSize), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateForm(
        [FromBody] CreateFormRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(new CreateFormCommand(
            request.Name,
            request.Description,
            currentUser.OrgId,
            currentUser.UserId.ToString()), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/forms/{result.Value}", new { id = result.Value });
    }

    private static async Task<IResult> GetFormPicker(
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        IReadOnlyList<GetFormPickerDto> result = await mediator.Send(
            new GetFormPickerQuery(currentUser.OrgId), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetFormById(
        Guid formId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        FormDetailDto? result = await mediator.Send(
            new GetFormByIdQuery(formId, currentUser.OrgId), ct);
        if (result is null) return Results.NotFound();
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateForm(
        Guid formId,
        [FromBody] UpdateFormRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new UpdateFormCommand(
            formId,
            currentUser.OrgId,
            request.Name,
            request.Description), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteForm(
        Guid formId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(new DeleteFormCommand(formId, currentUser.OrgId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> AddField(
        Guid formId,
        [FromBody] AddFormFieldRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result<Guid> result = await mediator.Send(new AddFieldToFormCommand(
            formId,
            currentUser.OrgId,
            request.Key,
            request.Label,
            request.Type,
            request.Required,
            request.Config), ct);

        if (result.IsFailure) return result.ToProblemDetails();
        return Results.Created($"/api/forms/{formId}/fields/{result.Value}", new { id = result.Value });
    }

    private static async Task<IResult> RemoveField(
        Guid formId,
        Guid fieldId,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new RemoveFieldFromFormCommand(formId, currentUser.OrgId, fieldId), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }

    private static async Task<IResult> ReorderFields(
        Guid formId,
        [FromBody] ReorderFormFieldsRequest request,
        CurrentUser currentUser,
        ISender mediator,
        CancellationToken ct)
    {
        Result result = await mediator.Send(
            new ReorderFormFieldsCommand(formId, currentUser.OrgId, request.FieldIds), ct);
        if (result.IsFailure) return result.ToProblemDetails();
        return Results.NoContent();
    }
}
