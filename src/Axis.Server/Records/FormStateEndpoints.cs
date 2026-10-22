using Axis.Configuration.Model;
using Axis.Data.Forms;
using Axis.Data.Records;
using Axis.Policy;
using Axis.Processes.Records;
using Axis.Server.Applications;
using Axis.Server.Http;
using Axis.Server.Tenancy;
using Axis.Server.Users;

namespace Axis.Server.Records;

/// <summary>
/// The form state endpoint of a record form in the active release of an application. It answers
/// which sections and fields of the form are visible, required and read-only for a draft record,
/// and which messages the fields the user touched earn. It writes no record, no audit record and
/// no sequence number, and it runs no unique or reference-existence check.
/// Every request is checked in order: session (401), then the application and the form (404), then
/// a <c>create</c> or <c>update</c> grant on the form's entity (403), then the content type (415)
/// and the shape of the body (400), then the grant that the body's <c>recordId</c> needs: <c>create</c>
/// for none and <c>update</c> for a record (403). A record that the user's <c>read</c> grants do not
/// reach is a 404, and a <c>read</c> filter on a field that is being recomputed is a 409. The first
/// two grant checks are in this order because the body holds <c>recordId</c>. A value that does not
/// fit its field is not a 400: it is a message in a 200 answer. The answer leaves out the fields
/// the user may not read.
/// </summary>
internal static class FormStateEndpoints
{
    public static void MapFormStateEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost("/api/apps/{app}/forms/{form}/state", StateAsync);

    private static async Task<IResult> StateAsync(
        string app,
        string form,
        HttpRequest request,
        ActiveApplicationResolver resolver,
        TenantDatabase database,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (await CurrentUsers.FindAsync(request.HttpContext) is not { } user)
        {
            return Grants.SignInRequired();
        }

        if (await resolver.ResolveReleaseAsync(app, cancellationToken) is not { } active)
        {
            return NotFound("No application is active under this name.");
        }

        var application = active.Model;
        if (!application.TryGetForm(form, out var model))
        {
            return NotFound("The application has no form with this name.");
        }

        // The form's entity is always in the model; the compiler resolved it.
        var entity = application.FindEntity(model.Entity.Id)
            ?? throw new InvalidOperationException("The application has no entity for the form.");
        var create = PolicyEvaluator.Access(application, user.Roles, entity, PolicyAction.Create);
        var update = PolicyEvaluator.Access(application, user.Roles, entity, PolicyAction.Update);
        if (!create.Granted && !update.Granted)
        {
            return Grants.Forbidden();
        }

        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        var body = FormStateBody.Parse(await RecordEndpoints.ReadBodyAsync(request, cancellationToken), entity, application, allowRecordId: true);
        if (body.Malformed is { } malformed)
        {
            return Results.ValidationProblem(malformed);
        }

        if (!(body.RecordId is null ? create : update).Granted)
        {
            return Grants.Forbidden();
        }

        var connection = await database.GetConnectionAsync(cancellationToken);
        Record? stored = null;
        if (body.RecordId is { } recordId)
        {
            // The draft is read only when the stored record is readable, as an update needs.
            var read = PolicyEvaluator.Access(application, user.Roles, entity, PolicyAction.Read);
            if (!read.Granted)
            {
                return RecordNotFound();
            }

            if (read.Filters is { } filters)
            {
                if (await RecordCommand.ReadsPendingAsync(connection, null, application, entity, filters, loggerFactory, cancellationToken))
                {
                    return Grants.PendingFilter();
                }

                if (await RecordCommand.MatchAsync(connection, null, application, entity, recordId, filters, user.Id, loggerFactory, cancellationToken) is not { Found: true, Matched: true })
                {
                    return RecordNotFound();
                }
            }

            stored = await RecordQueries.GetAsync(connection, application, entity, recordId, cancellationToken);
            if (stored is null)
            {
                return RecordNotFound();
            }
        }

        var now = await RecordQueries.TransactionTimeAsync(connection, cancellationToken);
        var readable = RecordFieldAccess.Read(application, user.Roles, entity).Readable;
        return Results.Ok(FormStateEvaluator.Evaluate(application, model, entity, stored, body.Draft!, body.Touched!, readable, user.Id, now));
    }

    private static IResult RecordNotFound() => NotFound("No record has this id.");

    private static IResult NotFound(string title) =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: title);
}
