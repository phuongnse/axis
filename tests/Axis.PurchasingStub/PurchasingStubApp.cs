using System.Net;
using System.Text;

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
/// </summary>
public static class PurchasingStubApp
{
    /// <summary>The client id the stub accepts when <c>Purchasing:ClientId</c> is not set.</summary>
    public const string DefaultClientId = "axis-purchasing";

    /// <summary>The client secret the stub accepts when <c>Purchasing:ClientSecret</c> is not set. A development placeholder.</summary>
    public const string DefaultClientSecret = "axis-development-purchasing-secret";

    private const int DefaultTokenLifetimeSeconds = 120;
    private const string IdempotencyHeader = "Idempotency-Key";

    public static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<PurchaseOrderStore>();
        builder.Services.AddSingleton<TokenStore>();

        var app = builder.Build();

        var clientId = app.Configuration["Purchasing:ClientId"] ?? DefaultClientId;
        var clientSecret = app.Configuration["Purchasing:ClientSecret"] ?? DefaultClientSecret;
        var lifetime = TimeSpan.FromSeconds(app.Configuration.GetValue("Purchasing:TokenLifetime", DefaultTokenLifetimeSeconds));

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

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
