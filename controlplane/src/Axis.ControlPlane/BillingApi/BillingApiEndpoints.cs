using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Axis.ControlPlane.Audit;
using Axis.ControlPlane.Plans;
using Axis.ControlPlane.Subscriptions;
using Npgsql;

namespace Axis.ControlPlane.BillingApi;

/// <summary>
/// The routes of the billing API under <c>/api/billing/v1/</c>, see <c>docs/reference/billing-api.md</c>. The group needs the billing client's token and its
/// scope, see <see cref="BillingApiClient"/>. Without complete <c>ControlPlane:Billing</c> settings every route answers <c>401</c> with
/// <c>WWW-Authenticate: Bearer</c>. This part holds the call that sets the paid-through date and the call that sets the plan and the add-ons.
/// </summary>
internal static class BillingApiEndpoints
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private const int MaxIdempotencyKeyLength = 255;

    private const string PaidThroughKind = "paid-through";

    private const string PlanKind = "plan";

    private const string DateFormat = "yyyy-MM-dd";

    public static void MapBillingApiEndpoints(this IEndpointRouteBuilder endpoints, ControlPlaneBillingOptions billing)
    {
        ArgumentNullException.ThrowIfNull(billing);
        var group = endpoints.MapGroup("/api/billing/v1");
        if (billing.IsComplete)
        {
            group.RequireAuthorization(BillingApiClient.Policy);
        }
        else
        {
            group.AddEndpointFilter((_, _) => ValueTask.FromResult<object?>(new UnauthorizedBearerResult()));
        }

        group.MapPut("/tenants/{tenantId}/subscription/paid-through", SetPaidThroughAsync);
        group.MapPut("/tenants/{tenantId}/subscription/plan", SetPlanAsync);
    }

    private static Task<IResult> SetPaidThroughAsync(
        string tenantId,
        HttpContext httpContext,
        NpgsqlDataSource controlPlane,
        TimeProvider clock,
        CancellationToken cancellationToken) =>
        HandleAsync(
            PaidThroughKind,
            tenantId,
            httpContext,
            controlPlane,
            ReadPaidThrough,
            async move =>
            {
                if (move.Body.PaidThrough < DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime))
                {
                    return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The paid-through date must not be before today in UTC.");
                }

                if (move.State is "deleting" or "deleted")
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The subscription is being deleted or is deleted.");
                }

                if (move.PlanCode is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The subscription pins no plan. Set the plan first.");
                }

                return await SubscriptionPaidThrough.SetAsync(move.Transaction, move.SubscriptionId, move.Body.PaidThrough, AuditActorKind.Billing, move.Client, cancellationToken)
                    ? null
                    : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The subscription's state does not take a paid-through date.");
            },
            cancellationToken);

    private static Task<IResult> SetPlanAsync(
        string tenantId,
        HttpContext httpContext,
        NpgsqlDataSource controlPlane,
        PlanCatalogue catalogue,
        CancellationToken cancellationToken) =>
        HandleAsync(
            PlanKind,
            tenantId,
            httpContext,
            controlPlane,
            ReadPlan,
            async move =>
            {
                if (catalogue.Find(move.Body.Code, move.Body.Version) is not { Trial: false } plan)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: "The setting ControlPlane:Plans lists no paid plan with this code and version.");
                }

                var addOns = new List<(PlanAddOn AddOn, int Quantity)>();
                foreach (var request in move.Body.AddOns)
                {
                    if (plan.AddOns.FirstOrDefault(candidate => string.Equals(candidate.Code, request.Code, StringComparison.Ordinal)) is not { } addOn)
                    {
                        return Results.Problem(
                            statusCode: StatusCodes.Status422UnprocessableEntity,
                            title: "The plan version lists no add-on with the code of one of the packs.");
                    }

                    if (request.Quantity < 1)
                    {
                        return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The quantity of each pack must be at least 1.");
                    }

                    if (addOns.Any(held => held.AddOn == addOn))
                    {
                        return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Each add-on code may appear once in addOns.");
                    }

                    addOns.Add((addOn, request.Quantity));
                }

                if (move.State is "deleting" or "deleted")
                {
                    return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The subscription is being deleted or is deleted.");
                }

                return await SubscriptionPlanChange.SetAsync(move.Transaction, move.SubscriptionId, plan, addOns, AuditActorKind.Billing, move.Client, cancellationToken)
                    ? null
                    : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "The subscription's state does not take a plan.");
            },
            cancellationToken);

    // The flow that every call that changes a subscription shares, see docs/reference/billing-api.md#answers-every-call-shares and #idempotency. The checks run in
    // this order, and the first that fails answers: the key, the tenant, the body, the replay, then the subscription and the call's own checks. The call's
    // move returns null when it moved the subscription, and else the refusal. A refusal commits nothing and keeps nothing under the key.
    private static async Task<IResult> HandleAsync<TBody>(
        string kind,
        string tenantId,
        HttpContext httpContext,
        NpgsqlDataSource controlPlane,
        Func<byte[], (TBody? Body, IResult? Refusal)> read,
        Func<BillingMove<TBody>, Task<IResult?>> move,
        CancellationToken cancellationToken)
        where TBody : class
    {
        var values = httpContext.Request.Headers[IdempotencyKeyHeader];
        if (values.Count != 1 || string.IsNullOrWhiteSpace(values[0]) || values[0]!.Length > MaxIdempotencyKeyLength)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: $"The request must carry an {IdempotencyKeyHeader} header of 1 to {MaxIdempotencyKeyLength} characters.");
        }

        var key = values[0]!;
        using var bodyStream = new MemoryStream();
        await httpContext.Request.Body.CopyToAsync(bodyStream, cancellationToken);
        var bytes = bodyStream.ToArray();

        if (await FindSubscriptionAsync(controlPlane, tenantId, cancellationToken) is not { } subscriptionId)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The region holds no subscription for the tenant.");
        }

        var (body, refusal) = read(bytes);
        if (body is null)
        {
            return refusal!;
        }

        await using var connection = await controlPlane.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // The key index cannot be unique, as a key may be used again after the window, so the lock keeps two parallel retries from both acting.
        await using (var @lock = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended('axis-billing:' || @key, 0))", connection, transaction))
        {
            @lock.Parameters.AddWithValue("key", key);
            await @lock.ExecuteNonQueryAsync(cancellationToken);
        }

        var hash = HashRequest(kind, tenantId, bytes);
        await using (var replay = new NpgsqlCommand(
            """
            SELECT request_hash, answer FROM control_plane.billing_requests
            WHERE idempotency_key = @key AND created_at > now() - interval '24 hours'
            ORDER BY created_at DESC LIMIT 1
            """,
            connection,
            transaction))
        {
            replay.Parameters.AddWithValue("key", key);
            await using var reader = await replay.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                return reader.GetString(0) == hash
                    ? Results.Text(reader.GetString(1), "application/json")
                    : Results.Problem(
                        statusCode: StatusCodes.Status422UnprocessableEntity,
                        title: $"The {IdempotencyKeyHeader} was used before with another request.");
            }
        }

        string state;
        string? planCode;
        await using (var select = new NpgsqlCommand(
            "SELECT state, plan_code FROM control_plane.subscriptions WHERE id = @id FOR UPDATE",
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("id", subscriptionId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "The region holds no subscription for the tenant.");
            }

            state = reader.GetString(0);
            planCode = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        var client = httpContext.User.FindFirst(BillingApiClient.ClientClaim)!.Value;
        if (await move(new BillingMove<TBody>(transaction, subscriptionId, state, planCode, body, client)) is { } refused)
        {
            return refused;
        }

        var answer = await ReadAnswerAsync(connection, transaction, subscriptionId, cancellationToken);
        await using (var insert = new NpgsqlCommand(
            "INSERT INTO control_plane.billing_requests (id, idempotency_key, request_hash, answer) VALUES (@id, @key, @hash, @answer)",
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue("id", Guid.CreateVersion7());
            insert.Parameters.AddWithValue("key", key);
            insert.Parameters.AddWithValue("hash", hash);
            insert.Parameters.AddWithValue("answer", answer);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return Results.Text(answer, "application/json");
    }

    // The tenant's subscription that is not deleted, or else its latest deleted one.
    private static async Task<Guid?> FindSubscriptionAsync(NpgsqlDataSource controlPlane, string tenantId, CancellationToken cancellationToken)
    {
        await using var command = controlPlane.CreateCommand(
            "SELECT id FROM control_plane.subscriptions WHERE tenant_id = $1 ORDER BY (state = 'deleted'), created_at DESC, id DESC LIMIT 1");
        command.Parameters.AddWithValue(tenantId);
        return await command.ExecuteScalarAsync(cancellationToken) as Guid?;
    }

    private static (PaidThroughBody? Body, IResult? Refusal) ReadPaidThrough(byte[] body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("paidThrough", out var value)
                && value.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(value.GetString(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                return (new PaidThroughBody(date), null);
            }
        }
        catch (JsonException)
        {
        }

        return (null, Invalid("paidThrough", $"The body must be a JSON object with a paidThrough date as {DateFormat}."));
    }

    // A field the call does not know is ignored, as docs/reference/billing-api.md#the-version says.
    private static (PlanBody? Body, IResult? Refusal) ReadPlan(byte[] body)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return (null, Invalid("code", "The body must be a JSON object with a code, a version and addOns."));
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return (null, Invalid("code", "The body must be a JSON object with a code, a version and addOns."));
            }

            if (!root.TryGetProperty("code", out var code) || code.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(code.GetString()))
            {
                return (null, Invalid("code", "The code must be a non-empty string."));
            }

            if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var versionNumber))
            {
                return (null, Invalid("version", "The version must be an integer."));
            }

            const string addOnsMessage = "The addOns must be an array of objects, each with a code string and a quantity integer.";
            if (!root.TryGetProperty("addOns", out var addOns) || addOns.ValueKind != JsonValueKind.Array)
            {
                return (null, Invalid("addOns", addOnsMessage));
            }

            var requests = new List<AddOnRequest>();
            foreach (var item in addOns.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("code", out var addOnCode)
                    || addOnCode.ValueKind != JsonValueKind.String
                    || !item.TryGetProperty("quantity", out var quantity)
                    || quantity.ValueKind != JsonValueKind.Number
                    || !quantity.TryGetInt32(out var quantityNumber))
                {
                    return (null, Invalid("addOns", addOnsMessage));
                }

                requests.Add(new AddOnRequest(addOnCode.GetString()!, quantityNumber));
            }

            return (new PlanBody(code.GetString()!, versionNumber, requests), null);
        }
    }

    private static IResult Invalid(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    // The route, the tenant and the body, as SHA-256 in lowercase hex, as the cell's provisioning API hashes a request.
    private static string HashRequest(string kind, string tenantId, byte[] body) =>
        Convert.ToHexStringLower(SHA256.HashData([.. Encoding.UTF8.GetBytes($"{kind}\n{tenantId}\n"), .. body]));

    // The subscription as the page shows it when it reads a tenant, without the usage, see docs/reference/billing-api.md#answers-every-call-shares.
    private static async Task<string> ReadAnswerAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid subscriptionId, CancellationToken cancellationToken)
    {
        string tenantId;
        string state;
        DateTime? nextAt;
        DateTime trialEndsAt;
        DateOnly? paidThrough;
        BillingPlan? plan = null;
        await using (var select = new NpgsqlCommand(
            "SELECT tenant_id, state, next_at, trial_ends_at, paid_through, plan_code, plan_version FROM control_plane.subscriptions WHERE id = @id",
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("id", subscriptionId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            tenantId = reader.GetString(0);
            state = reader.GetString(1);
            nextAt = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2).UtcDateTime;
            trialEndsAt = reader.GetFieldValue<DateTimeOffset>(3).UtcDateTime;
            paidThrough = reader.IsDBNull(4) ? null : reader.GetFieldValue<DateOnly>(4);
            if (!reader.IsDBNull(5))
            {
                plan = new BillingPlan(reader.GetString(5), reader.GetInt32(6));
            }
        }

        var addOns = new List<BillingAddOn>();
        await using (var select = new NpgsqlCommand(
            "SELECT add_on_code, quantity FROM control_plane.subscription_add_ons WHERE subscription_id = @id ORDER BY add_on_code",
            connection,
            transaction))
        {
            select.Parameters.AddWithValue("id", subscriptionId);
            await using var reader = await select.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                addOns.Add(new BillingAddOn(reader.GetString(0), reader.GetInt32(1)));
            }
        }

        return JsonSerializer.Serialize(new BillingSubscription(tenantId, state, nextAt, trialEndsAt, paidThrough, plan, addOns), JsonSerializerOptions.Web);
    }

    private sealed record PaidThroughBody(DateOnly PaidThrough);

    private sealed record PlanBody(string Code, int Version, IReadOnlyList<AddOnRequest> AddOns);

    private sealed record AddOnRequest(string Code, int Quantity);

    // What a call's own move gets: the transaction that holds the subscription's row locked, and what the call read of it.
    private sealed record BillingMove<TBody>(NpgsqlTransaction Transaction, Guid SubscriptionId, string State, string? PlanCode, TBody Body, string Client);

    private sealed record BillingPlan(string Code, int Version);

    private sealed record BillingAddOn(string Code, int Quantity);

    private sealed record BillingSubscription(
        string TenantId,
        string State,
        DateTime? NextAt,
        DateTime TrialEndsAt,
        DateOnly? PaidThrough,
        BillingPlan? Plan,
        IReadOnlyList<BillingAddOn> AddOns);

    // A 401 that carries the challenge, for the routes that no settings serve. The authentication handler adds the same header on its own 401.
    private sealed class UnauthorizedBearerResult : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            httpContext.Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }
    }
}
