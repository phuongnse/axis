using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
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
    private const int TokenBytes = 32;

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

        // See docs/reference/sign-up.md#the-verify-page. It reports the state of a link and changes nothing. An unknown, malformed or
        // expired token answers expired, so a caller learns nothing about the sign-ups it does not hold.
        endpoints.MapPost("/api/sign-up/verification", VerifyAsync);

        // See docs/reference/sign-up.md#the-verify-page. One conditional update marks a pending, unexpired sign-up verified, so a link
        // works once and two parallel confirms cannot both succeed. A used or expired link answers 409 with its state.
        endpoints.MapPost("/api/sign-up/confirm", ConfirmAsync);

        // The region's built-in applications from the settings, each with its id, its title and its description in both languages.
        endpoints.MapGet("/api/sign-up/applications", (ControlPlaneOptions options) => Results.Ok(new
        {
            applications = options.SignUp.Applications.Select(application => new
            {
                id = application.Id,
                titles = new { en = application.Titles.En, vi = application.Titles.Vi },
                descriptions = new { en = application.Descriptions.En, vi = application.Descriptions.Vi },
            }),
        }));

        // The region's legal documents, see docs/reference/sign-up.md#the-documents. Without the settings the route cannot answer.
        endpoints.MapGet("/api/sign-up/documents", (ControlPlaneOptions options) =>
        {
            if (!options.SignUp.HasDocuments)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            return Results.Ok(new
            {
                terms = Document(options.SignUp.Terms),
                dpa = Document(options.SignUp.Dpa),
                privacy = Document(options.SignUp.Privacy),
                licencesUrl = options.SignUp.LicencesUrl,
            });
        });

        // The tenant domain that the sign-up page shows after the subdomain, see docs/reference/sign-up.md#the-subdomain.
        endpoints.MapGet("/api/sign-up/region", (ControlPlaneOptions options) => Results.Ok(new { tenantDomain = options.TenantDomain }));
    }

    private static object Document(SignUpDocumentOptions document) => new
    {
        version = document.Version,
        urls = new { en = document.Urls.En, vi = document.Urls.Vi },
    };

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

        var (body, refused) = await ReadBodyAsync(request, cancellationToken);
        if (body is null)
        {
            return refused!;
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

    private static async Task<IResult> VerifyAsync(HttpRequest request, ControlPlaneOptions options, NpgsqlDataSource controlPlane, CancellationToken cancellationToken)
    {
        var (body, refused) = await ReadBodyAsync(request, cancellationToken);
        if (body is null)
        {
            return refused!;
        }

        var found = await FindAsync(controlPlane, TokenHash(body), cancellationToken);
        return found.State switch
        {
            "pending" => Results.Ok(new { state = "pending", email = found.Email, businessName = found.BusinessName, address = SignUpAddress.Of(options, found.Subdomain) }),
            _ => Results.Ok(new { state = found.State }),
        };
    }

    private static async Task<IResult> ConfirmAsync(HttpRequest request, NpgsqlDataSource controlPlane, CancellationToken cancellationToken)
    {
        var (body, refused) = await ReadBodyAsync(request, cancellationToken);
        if (body is null)
        {
            return refused!;
        }

        var hash = TokenHash(body);
        if (hash is not null)
        {
            await using var confirm = controlPlane.CreateCommand(
                "UPDATE control_plane.sign_ups SET state = 'verified' WHERE token_hash = @hash AND state = 'pending' AND expires_at > now() RETURNING id");
            confirm.Parameters.AddWithValue("hash", hash);
            if (await confirm.ExecuteScalarAsync(cancellationToken) is Guid id)
            {
                return Results.Ok(new { id });
            }
        }

        // The update found nothing to confirm: the link was used, ran out or was never issued. A pending answer means the link ran out in between.
        var found = await FindAsync(controlPlane, hash, cancellationToken);
        return Results.Json(new { state = found.State is "confirmed" ? "confirmed" : "expired" }, statusCode: StatusCodes.Status409Conflict);
    }

    // The state of a link: pending within its 24 hours, confirmed or expired. No row, or a link that ran out before the expiry job marked it, is expired.
    private static async Task<(string State, string Email, string BusinessName, string Subdomain)> FindAsync(NpgsqlDataSource controlPlane, byte[]? hash, CancellationToken cancellationToken)
    {
        if (hash is not null)
        {
            await using var read = controlPlane.CreateCommand(
                "SELECT state, expires_at > now(), email, business_name, subdomain FROM control_plane.sign_ups WHERE token_hash = @hash");
            read.Parameters.AddWithValue("hash", hash);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                var state = reader.GetString(0) switch
                {
                    "verified" => "confirmed",
                    "pending" when reader.GetBoolean(1) => "pending",
                    _ => "expired",
                };
                return (state, reader.GetString(2), reader.GetString(3), reader.GetString(4));
            }
        }

        return ("expired", "", "", "");
    }

    // The SHA-256 of the token of a body, or null when the token is missing, not a string or not 32 bytes of base64url.
    private static byte[]? TokenHash(JsonObject body)
    {
        if (body["token"] is not JsonValue value || !value.TryGetValue<string>(out var token))
        {
            return null;
        }

        var bytes = new byte[TokenBytes];
        var status = Base64Url.DecodeFromChars(token, bytes, out _, out var written);
        return status == OperationStatus.Done && written == TokenBytes ? SHA256.HashData(bytes) : null;
    }

    // The body as a JSON object, or the answer that refuses it: 415 for a body that is not JSON and 400 for one that is not an object.
    private static async Task<(JsonObject? Body, IResult? Refused)> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (!JsonRequest.IsJson(request))
        {
            return (null, JsonRequest.UnsupportedMediaType());
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

        return body is null
            ? (null, Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "The request body must be a JSON object."))
            : (body, null);
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
