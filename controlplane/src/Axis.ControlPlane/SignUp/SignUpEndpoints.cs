using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Emails;
using Axis.ControlPlane.Jobs;
using Axis.ControlPlane.Subscriptions;
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

        // The proof-of-work challenge, see docs/reference/sign-up.md#bots-and-abuse. Without the key the route cannot sign one.
        endpoints.MapGet("/api/sign-up/challenge", (ControlPlaneOptions options, TimeProvider clock) =>
            string.IsNullOrWhiteSpace(options.SignUp.ChallengeKey)
                ? Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(SignUpChallenge.Create(Convert.FromBase64String(options.SignUp.ChallengeKey), clock.GetUtcNow())));

        // See docs/reference/sign-up.md#sending-the-form. It checks, in this order, the settings (the challenge key and the directory),
        // the body, the proof-of-work solution in altcha, the client address limit, then the form's fields, the email's domain limit, a disposable
        // email domain and the subdomain, see docs/reference/sign-up.md#bots-and-abuse. A missing, malformed, wrong, expired or used solution is refused
        // under /altcha, and an accepted one is stored so it works once. The route stores a pending sign-up that holds its subdomain for
        // 24 hours and queues the confirm email in the same transaction. An address that already verified a sign-up gets the trial-used
        // email instead: nothing is stored and no subdomain is held, and the answer is the same. The answer has no body yet. Without the
        // key or the directory the route answers 503.
        endpoints.MapPost("/api/sign-up", SignUpAsync);

        // See docs/reference/sign-up.md#the-verify-page. It reports the state of a link and changes nothing. An unknown, malformed or
        // expired token answers expired, so a caller learns nothing about the sign-ups it does not hold.
        endpoints.MapPost("/api/sign-up/verification", VerifyAsync);

        // See docs/reference/sign-up.md#the-verify-page. One conditional update marks a pending, unexpired sign-up verified, so a link
        // works once and two parallel confirms cannot both succeed. The same transaction starts the tenant's trialing subscription,
        // see docs/reference/subscriptions.md#one-clock. A used or expired link answers 409 with its state. So does a link whose address
        // another sign-up verified first: the unique index of the verified email refuses it, nothing changes and it answers confirmed.
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
        HttpContext context,
        IServiceProvider services,
        ControlPlaneOptions options,
        EmailTemplates templates,
        SignUpLimits limits,
        NpgsqlDataSource controlPlane,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        var request = context.Request;
        if (string.IsNullOrWhiteSpace(options.SignUp.ChallengeKey))
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable);
        }

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

        // One answer for every failed solution, so a caller learns nothing about which check it failed.
        var solution = SignUpChallenge.Verify(Convert.FromBase64String(options.SignUp.ChallengeKey), Required(body, "altcha"), clock.GetUtcNow());
        if (solution is null || !await UseChallengeAsync(controlPlane, solution.Value, cancellationToken))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["/altcha"] = ["signUp.altcha.refused"] });
        }

        if (limits.RefuseAddress(context) is { } addressLimit)
        {
            return addressLimit;
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

        if (email is not null && !errors.ContainsKey("/email") && limits.RefuseDomain(context, email) is { } domainLimit)
        {
            return domainLimit;
        }

        if (email is not null && !errors.ContainsKey("/email") && await DisposableDomains.IsListedAsync(controlPlane, email, cancellationToken))
        {
            errors["/email"] = ["signUp.email.disposable"];
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
        var normalisedEmail = SignUpEmail.Normalise(email!);
        try
        {
            await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);

            // An address that already verified a sign-up gets the trial-used email, as the answer is alike for every address.
            if (await VerifiedSubdomainAsync(connection, normalisedEmail, cancellationToken) is { } tenantId)
            {
                await QueueTrialUsedAsync(services, connection, options, templates, tenantId, fullName!, email!, language!, cancellationToken);
                return Results.StatusCode(StatusCodes.Status202Accepted);
            }

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
                INSERT INTO control_plane.sign_ups (id, full_name, email, normalised_email, business_name, subdomain, language, application, expires_at)
                VALUES (@id, @fullName, @email, @normalisedEmail, @businessName, @subdomain, @language, @application, now() + interval '24 hours')
                """,
                connection,
                transaction))
            {
                insert.Parameters.AddWithValue("id", id);
                insert.Parameters.AddWithValue("fullName", fullName!);
                insert.Parameters.AddWithValue("email", email!);
                insert.Parameters.AddWithValue("normalisedEmail", normalisedEmail);
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

    // The subdomain of the verified sign-up of a normalised email, or null when the address has none.
    private static async Task<string?> VerifiedSubdomainAsync(NpgsqlConnection connection, string normalisedEmail, CancellationToken cancellationToken)
    {
        await using var read = new NpgsqlCommand("SELECT subdomain FROM control_plane.sign_ups WHERE normalised_email = @email AND state = 'verified'", connection);
        read.Parameters.AddWithValue("email", normalisedEmail);
        return await read.ExecuteScalarAsync(cancellationToken) as string;
    }

    // Queues the trial-used email to the address of the form: the sign-in address while the region directory holds the tenant of the
    // earlier sign-up, else the billing portal link. See docs/reference/sign-up.md#the-emails.
    private static async Task QueueTrialUsedAsync(
        IServiceProvider services,
        NpgsqlConnection connection,
        ControlPlaneOptions options,
        EmailTemplates templates,
        string tenantId,
        string fullName,
        string email,
        string language,
        CancellationToken cancellationToken)
    {
        var directory = services.GetRequiredKeyedService<NpgsqlDataSource>("directory");
        await using var exists = directory.CreateCommand("SELECT EXISTS (SELECT 1 FROM directory.tenants WHERE id = @id)");
        exists.Parameters.AddWithValue("id", tenantId);
        var running = (bool)(await exists.ExecuteScalarAsync(cancellationToken))!;

        var (template, values) = running
            ? ("signUpTrialUsedSignIn", new Dictionary<string, string> { ["name"] = fullName, ["tenantAddress"] = SignUpAddress.Of(options, tenantId) })
            : ("signUpTrialUsedBillingPortal", new Dictionary<string, string> { ["name"] = fullName, ["billingPortalUrl"] = SignUpAddress.BillingPortalOf(options, tenantId) });
        await EmailJobs.EnqueueAsync(connection, null, templates, template, language, email, values, cancellationToken);
    }

    // Stores a solution as used. The conditional insert is atomic, so of two parallel sign-ups with one solution, on any replica, only one inserts.
    private static async Task<bool> UseChallengeAsync(NpgsqlDataSource controlPlane, (byte[] Challenge, DateTimeOffset ExpiresAt) solution, CancellationToken cancellationToken)
    {
        await using var insert = controlPlane.CreateCommand(
            "INSERT INTO control_plane.sign_up_challenges (challenge, expires_at) VALUES (@challenge, @expiresAt) ON CONFLICT DO NOTHING");
        insert.Parameters.AddWithValue("challenge", solution.Challenge);
        insert.Parameters.AddWithValue("expiresAt", solution.ExpiresAt);
        return await insert.ExecuteNonQueryAsync(cancellationToken) == 1;
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

    private static async Task<IResult> ConfirmAsync(HttpRequest request, NpgsqlDataSource controlPlane, TimeProvider clock, CancellationToken cancellationToken)
    {
        var (body, refused) = await ReadBodyAsync(request, cancellationToken);
        if (body is null)
        {
            return refused!;
        }

        var hash = TokenHash(body);
        if (hash is not null)
        {
            await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            Guid id;
            string subdomain;
            try
            {
                await using (var confirm = new NpgsqlCommand(
                    "UPDATE control_plane.sign_ups SET state = 'verified' WHERE token_hash = @hash AND state = 'pending' AND expires_at > now() RETURNING id, subdomain",
                    connection,
                    transaction))
                {
                    confirm.Parameters.AddWithValue("hash", hash);
                    await using var reader = await confirm.ExecuteReaderAsync(cancellationToken);
                    if (await reader.ReadAsync(cancellationToken))
                    {
                        id = reader.GetGuid(0);
                        subdomain = reader.GetString(1);
                    }
                    else
                    {
                        id = Guid.Empty;
                        subdomain = "";
                    }
                }
            }
            catch (PostgresException exception) when (exception is { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_sign_ups_verified_email" })
            {
                // Another sign-up of the address was verified first, so there is no second trial. The transaction rolls back when it is
                // disposed and this sign-up stays pending until it expires.
                return Results.Json(new { state = "confirmed" }, statusCode: StatusCodes.Status409Conflict);
            }

            if (id != Guid.Empty)
            {
                var subscriptionId = Guid.CreateVersion7();
                var trialEndsAt = clock.GetUtcNow() + SubscriptionClock.TrialLength;
                await using (var insert = new NpgsqlCommand(
                    """
                    INSERT INTO control_plane.subscriptions (id, tenant_id, sign_up_id, state, trial_ends_at, next_at, step_id)
                    VALUES (@id, @tenantId, @signUpId, 'trialing', @trialEndsAt, @trialEndsAt, @stepId)
                    """,
                    connection,
                    transaction))
                {
                    insert.Parameters.AddWithValue("id", subscriptionId);
                    insert.Parameters.AddWithValue("tenantId", subdomain);
                    insert.Parameters.AddWithValue("signUpId", id);
                    insert.Parameters.AddWithValue("trialEndsAt", trialEndsAt);
                    insert.Parameters.AddWithValue("stepId", Guid.CreateVersion7());
                    await insert.ExecuteNonQueryAsync(cancellationToken);
                }

                // The sign-up is the person who confirmed: billing portal accounts do not exist yet.
                await AuditRecords.AppendAsync(transaction, AuditActorKind.Account, id.ToString(), "subscription.trialing", subscriptionId.ToString(), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
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
