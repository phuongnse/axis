using System.Net;
using System.Text;
using System.Text.Json;

namespace Axis.PurchasingStub;

/// <summary>
/// A stand-in for the purchasing system that the purchase request sample's connector calls. It
/// creates purchase orders under an idempotency key, finds them by key, and can lose or hold back a
/// response on purpose after it has stored the order. It can also answer <c>503</c> for a request and
/// store no order, or leave a request's call unknown for good, so that an operator has to resolve it.
/// Orders live in memory only.
/// <para>
/// It also signs clients in the way an OAuth2 authorization server does for the client credentials
/// grant, at <c>POST /oauth/token</c>. Creating an order and looking one up need the token it issues,
/// and each order records the client id of the token that made it. It knows one client, set by
/// <c>Purchasing:ClientId</c> and <c>Purchasing:ClientSecret</c>. A token lives
/// <c>Purchasing:TokenLifetime</c> seconds. The list of orders, the health route and the test
/// switches need no token.
/// </para>
/// <para>
/// It receives the purchase request approval webhook at <c>POST /webhooks/purchase-request-approved</c>
/// and lists the deliveries it accepted. The webhook routes need no token, but a delivery must carry a
/// Standard Webhooks signature made with <c>Webhook:Secret</c>, or the stub answers <c>401</c>.
/// </para>
/// </summary>
public static class PurchasingStubApp
{
    /// <summary>The client id the stub accepts when <c>Purchasing:ClientId</c> is not set.</summary>
    public const string DefaultClientId = "axis-purchasing";

    /// <summary>The client secret the stub accepts when <c>Purchasing:ClientSecret</c> is not set. A development placeholder.</summary>
    public const string DefaultClientSecret = "axis-development-purchasing-secret";

    /// <summary>The webhook secret the stub accepts when <c>Webhook:Secret</c> is not set. A development placeholder.</summary>
    public const string DefaultWebhookSecret = "whsec_YXhpcy1kZXZlbG9wbWVudC13ZWJob29rLXNlY3JldA==";

    private const int DefaultTokenLifetimeSeconds = 120;
    private const string IdempotencyHeader = "Idempotency-Key";

    public static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<PurchaseOrderStore>();
        builder.Services.AddSingleton<TokenStore>();
        builder.Services.AddSingleton<WebhookDeliveryStore>();

        var app = builder.Build();

        var clientId = app.Configuration["Purchasing:ClientId"] ?? DefaultClientId;
        var clientSecret = app.Configuration["Purchasing:ClientSecret"] ?? DefaultClientSecret;
        var lifetime = TimeSpan.FromSeconds(app.Configuration.GetValue("Purchasing:TokenLifetime", DefaultTokenLifetimeSeconds));
        var webhookKey = WebhookVerifier.Key(app.Configuration["Webhook:Secret"] ?? DefaultWebhookSecret);

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapPost("/webhooks/purchase-request-approved", async (HttpContext context, WebhookDeliveryStore store) =>
        {
            // The signature is over the raw bytes, so the body is read as text and never bound to a model.
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(context.RequestAborted);
            var headers = context.Request.Headers;
            var webhookId = headers["webhook-id"].ToString();
            var now = DateTimeOffset.UtcNow;

            if (!WebhookVerifier.IsValid(webhookId, headers["webhook-timestamp"].ToString(), headers["webhook-signature"].ToString(), body, webhookKey, now))
            {
                return Results.Problem("The webhook signature or timestamp is not valid.", statusCode: StatusCodes.Status401Unauthorized);
            }

            using var document = TryParse(body);
            if (document is null || RequestNumberOf(document.RootElement) is not { } requestNumber)
            {
                return Results.Problem("The body must be JSON with a string at data.fields.number.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (store.IsFailing(requestNumber))
            {
                return Results.Problem("The stub is set to fail this request's webhook.", statusCode: StatusCodes.Status500InternalServerError);
            }

            store.Add(new WebhookDelivery(webhookId, TypeOf(document.RootElement), requestNumber, now));
            return Results.NoContent();
        });

        app.MapGet("/webhooks/purchase-request-approved", (WebhookDeliveryStore store) => Results.Ok(store.List()));

        app.MapPost("/test/fail-webhook/{requestNumber}", (string requestNumber, WebhookDeliveryStore store) =>
        {
            store.Fail(requestNumber);
            return Results.NoContent();
        });

        app.MapPost("/oauth/token", async (HttpContext context, TokenStore tokens) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);
            if (form["grant_type"] != "client_credentials")
            {
                return Results.Json(new { error = "unsupported_grant_type" }, statusCode: StatusCodes.Status400BadRequest);
            }

            if (!IsClient(context.Request.Headers.Authorization.ToString(), clientId, clientSecret))
            {
                context.Response.Headers.WWWAuthenticate = "Basic";
                return Results.Json(new { error = "invalid_client" }, statusCode: StatusCodes.Status401Unauthorized);
            }

            context.Response.Headers.CacheControl = "no-store";
            var token = tokens.Issue(clientId, lifetime);
            return Results.Ok(new { access_token = token, token_type = "Bearer", expires_in = (int)lifetime.TotalSeconds });
        });

        app.MapPost("/purchase-orders", async (HttpContext context, CreatePurchaseOrder? body, PurchaseOrderStore store, TokenStore tokens) =>
        {
            if (tokens.Find(context.Request.Headers.Authorization.ToString()) is not { } tokenClientId)
            {
                return Unauthorized(context);
            }

            var key = context.Request.Headers[IdempotencyHeader].ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                return Results.Problem($"The {IdempotencyHeader} header is required.", statusCode: StatusCodes.Status400BadRequest);
            }
            if (string.IsNullOrWhiteSpace(body?.RequestNumber) || body.Amount is null || ReadLines(body.Lines) is not { } lines)
            {
                return Results.Problem(
                    "requestNumber, amount and lines are required, and each line needs description, quantity, unitPrice and amount.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            if (store.IsUnavailable(body.RequestNumber))
            {
                return Results.Problem("The purchasing system is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var (order, created, drop, misfit, hold) = store.Create(key, body.RequestNumber, body.SupplierCode, body.Amount.Value, tokenClientId, lines);
            if (hold is not null)
            {
                // The order is stored. The caller waits until a test releases the response, or hangs up.
                try
                {
                    await hold.WaitAsync(context.RequestAborted);
                }
                catch (OperationCanceledException)
                {
                    return Results.Empty;
                }
            }

            if (drop)
            {
                // The order is stored. Closing the connection loses the response, as a network
                // failure after the purchasing system has done its work would.
                context.Abort();
                return Results.Empty;
            }

            if (misfit)
            {
                // The order is stored, but the answer does not fit the declared output. The worker
                // treats that as an unknown outcome and does not send the call again.
                return Results.Json(new { accepted = true }, statusCode: StatusCodes.Status201Created);
            }

            var result = new { orderNumber = order.OrderNumber };
            return created ? Results.Json(result, statusCode: StatusCodes.Status201Created) : Results.Ok(result);
        });

        app.MapGet("/purchase-orders/by-key/{idempotencyKey}", (HttpContext context, string idempotencyKey, PurchaseOrderStore store, TokenStore tokens) =>
        {
            if (tokens.Find(context.Request.Headers.Authorization.ToString()) is null)
            {
                return Unauthorized(context);
            }

            if (store.Find(idempotencyKey) is not { } order)
            {
                return Results.NotFound();
            }

            return store.IsLookupUnavailable(order)
                ? Results.Problem("The purchasing system is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(new { orderNumber = order.OrderNumber });
        });

        app.MapGet("/purchase-orders", (PurchaseOrderStore store) => Results.Ok(store.List()));

        app.MapPost("/test/drop-next", (PurchaseOrderStore store) =>
        {
            store.DropNext();
            return Results.NoContent();
        });

        app.MapPost("/test/drop/{idempotencyKey}", (string idempotencyKey, PurchaseOrderStore store) =>
        {
            store.DropKey(idempotencyKey);
            return Results.NoContent();
        });

        app.MapPost("/test/drop-request/{requestNumber}", (string requestNumber, PurchaseOrderStore store) =>
        {
            store.DropRequest(requestNumber);
            return Results.NoContent();
        });

        app.MapPost("/test/unavailable-request/{requestNumber}", (string requestNumber, PurchaseOrderStore store) =>
        {
            store.MakeUnavailable(requestNumber);
            return Results.NoContent();
        });

        app.MapPost("/test/available-request/{requestNumber}", (string requestNumber, PurchaseOrderStore store) =>
        {
            store.MakeAvailable(requestNumber);
            return Results.NoContent();
        });

        app.MapPost("/test/unknown-request/{requestNumber}", (string requestNumber, PurchaseOrderStore store) =>
        {
            store.MakeUnknown(requestNumber);
            return Results.NoContent();
        });

        app.MapPost("/test/hold-request/{requestNumber}", (string requestNumber, PurchaseOrderStore store) =>
        {
            store.HoldRequest(requestNumber);
            return Results.NoContent();
        });

        app.MapPost("/test/release-request/{requestNumber}", (string requestNumber, PurchaseOrderStore store) =>
        {
            store.Release(requestNumber);
            return Results.NoContent();
        });

        return app;
    }

    private static IResult Unauthorized(HttpContext context)
    {
        context.Response.Headers.WWWAuthenticate = "Bearer error=\"invalid_token\"";
        return Results.Problem("A valid access token is required.", statusCode: StatusCodes.Status401Unauthorized);
    }

    private static JsonDocument? TryParse(string body)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? RequestNumberOf(JsonElement root) =>
        Property(Property(Property(root, "data"), "fields"), "number") is { ValueKind: JsonValueKind.String } number ? number.GetString() : null;

    private static string? TypeOf(JsonElement root) =>
        Property(root, "type") is { ValueKind: JsonValueKind.String } type ? type.GetString() : null;

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    /// <summary>Whether the Basic header holds the client. The worker form-encodes both parts before it encodes the pair.</summary>
    private static bool IsClient(string header, string clientId, string clientSecret)
    {
        const string scheme = "Basic ";
        if (!header.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string pair;
        try
        {
            pair = Encoding.UTF8.GetString(Convert.FromBase64String(header[scheme.Length..].Trim()));
        }
        catch (FormatException)
        {
            return false;
        }

        var colon = pair.IndexOf(':', StringComparison.Ordinal);
        return colon >= 0
            && WebUtility.UrlDecode(pair[..colon]) == clientId
            && WebUtility.UrlDecode(pair[(colon + 1)..]) == clientSecret;
    }

    /// <summary>The lines of the body, or <see langword="null"/> when there are none or a line lacks a field.</summary>
    private static List<PurchaseOrderLine>? ReadLines(List<CreatePurchaseOrderLine>? lines)
    {
        if (lines is not { Count: > 0 })
        {
            return null;
        }

        var result = new List<PurchaseOrderLine>(lines.Count);
        foreach (var line in lines)
        {
            if (line is not { Quantity: { } quantity, UnitPrice: { } unitPrice, Amount: { } amount } || string.IsNullOrWhiteSpace(line.Description))
            {
                return null;
            }

            result.Add(new PurchaseOrderLine(line.Description, quantity, unitPrice, amount));
        }

        return result;
    }
}

internal sealed record CreatePurchaseOrder(string? RequestNumber, string? SupplierCode, decimal? Amount, List<CreatePurchaseOrderLine>? Lines);

internal sealed record CreatePurchaseOrderLine(string? Description, int? Quantity, decimal? UnitPrice, decimal? Amount);
