using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Jobs;
using Axis.Server.Http;
using MimeKit;
using Npgsql;

namespace Axis.ControlPlane.SignUp;

/// <summary>The public routes of sign-up.</summary>
internal static class SignUpEndpoints
{
    private const int MaxTextLength = 200;
    private const int MaxEmailLength = 254;

    public static void MapSignUpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // The answer holds only free and the reason, never a tenant, a cell or a host. Without the directory the check does not exist.
        endpoints.MapGet("/api/sign-up/subdomains/{name}", async (string name, IServiceProvider services, CancellationToken cancellationToken) =>
        {
            var check = services.GetService<SubdomainCheck>();
            if (check is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(await check.CheckAsync(name, cancellationToken));
        });

        // See docs/reference/sign-up.md#sending-the-form. It checks the form, stores a pending sign-up that holds its subdomain for 24 hours
        // and queues the confirm email in the same transaction. The answer has no body yet. Without the directory the route answers 503.
        endpoints.MapPost("/api/sign-up", SignUpAsync);

        // The region's built-in applications from the settings, each with its id and its title in both languages.
        endpoints.MapGet("/api/sign-up/applications", (ControlPlaneOptions options) => Results.Ok(new
        {
            applications = options.SignUp.Applications.Select(application => new
            {
                id = application.Id,
                titles = new { en = application.Titles.En, vi = application.Titles.Vi },
            }),
        }));
    }

    private static async Task<IResult> SignUpAsync(
        HttpRequest request,
        IServiceProvider services,
        ControlPlaneOptions options,
        NpgsqlDataSource controlPlane,
        CancellationToken cancellationToken)
    {
        var check = services.GetService<SubdomainCheck>();
        if (check is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        if (!JsonRequest.IsJson(request))
        {
            return JsonRequest.UnsupportedMediaType();
        }

        JsonObject? body;
        try
        {
            body = await JsonNode.ParseAsync(request.Body, cancellationToken: cancellationToken) as JsonObject;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            body = null;
        }

        if (body is null)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The request body must be a JSON object.");
        }

        // Every field is checked in one pass, so the person sees all that is wrong at once.
        var errors = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var fullName = Name(body, "fullName", MaxTextLength, errors);
        var email = Name(body, "email", MaxEmailLength, errors);
        var businessName = Name(body, "businessName", MaxTextLength, errors);
        var subdomain = Required(body, "subdomain");
        var language = Required(body, "language");
        if (subdomain is null)
        {
            errors["/subdomain"] = ["signUp.required"];
        }

        if (language is null)
        {
            errors["/language"] = ["signUp.required"];
        }
        else if (!EmailTemplates.Languages.Contains(language))
        {
            errors["/language"] = ["signUp.language.invalid"];
        }

        if (email is not null && !errors.ContainsKey("/email") && !(MailboxAddress.TryParse(email, out var mailbox) && mailbox.Address == email))
        {
            errors["/email"] = ["signUp.email.invalid"];
        }

        // A blank application is the empty text, and the form must send it, so a form that left the field out is refused.
        var application = body["application"] is JsonValue value && value.TryGetValue<string>(out var applicationId) ? applicationId : null;
        if (application is null || (application.Length > 0 && !options.SignUp.Applications.Any(configured => string.Equals(configured.Id, application, StringComparison.Ordinal))))
        {
            errors["/application"] = ["signUp.application.unknown"];
        }

        if (subdomain is not null)
        {
            var answer = await check.CheckAsync(subdomain, cancellationToken);
            if (!answer.Free)
            {
                errors["/subdomain"] = [$"signUp.subdomain.{answer.Reason}"];
            }
        }

        if (errors.Count > 0)
        {
            return Results.ValidationProblem(errors);
        }

        var id = Guid.CreateVersion7();
        try
        {
            await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            // The hold check ignores an expired sign-up, so one that the expiry job has not reached yet must not block a free name.
            await using (var expire = new NpgsqlCommand(
                "UPDATE control_plane.sign_ups SET state = 'expired' WHERE subdomain = @subdomain AND state = 'pending' AND expires_at <= now()",
                connection,
                transaction))
            {
                expire.Parameters.AddWithValue("subdomain", subdomain!);
                await expire.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var insert = new NpgsqlCommand(
                """
                INSERT INTO control_plane.sign_ups (id, full_name, email, business_name, subdomain, language, application, expires_at)
                VALUES (@id, @fullName, @email, @businessName, @subdomain, @language, @application, now() + interval '24 hours')
                """,
                connection,
                transaction))
            {
                insert.Parameters.AddWithValue("id", id);
                insert.Parameters.AddWithValue("fullName", fullName!);
                insert.Parameters.AddWithValue("email", email!);
                insert.Parameters.AddWithValue("businessName", businessName!);
                insert.Parameters.AddWithValue("subdomain", subdomain!);
                insert.Parameters.AddWithValue("language", language!);
                insert.Parameters.AddWithValue("application", application!);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            // The payload holds the id only: it is stored in the database, and the job reads the rest from the sign-up.
            await JobQueue.EnqueueAsync(connection, transaction, SignUpConfirmJobHandler.JobKind, new JsonObject { ["signUpId"] = id.ToString() }, null, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (exception is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_sign_ups_pending_subdomain" })
        {
            // A parallel sign-up for the same name won.
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["/subdomain"] = [$"signUp.subdomain.{SubdomainCheck.Held}"] });
        }

        return Results.StatusCode(StatusCodes.Status202Accepted);
    }

    // The text of a field that is a string and not blank, or null.
    private static string? Required(JsonObject body, string name) =>
        body[name] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    // The trimmed text of a field of at most maxLength characters. A missing or blank field and a long one are errors under the field's pointer.
    private static string? Name(JsonObject body, string name, int maxLength, SortedDictionary<string, string[]> errors)
    {
        var text = Required(body, name)?.Trim();
        if (text is null)
        {
            errors[$"/{name}"] = ["signUp.required"];
        }
        else if (text.Length > maxLength)
        {
            errors[$"/{name}"] = ["signUp.tooLong"];
        }

        return text;
    }
}
