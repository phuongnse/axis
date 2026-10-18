namespace Axis.PurchasingStub;

/// <summary>
/// A stand-in for the purchasing system that the purchase request sample's connector calls. It
/// creates purchase orders under an idempotency key, finds them by key, and can lose or hold back a
/// response on purpose after it has stored the order. It can also answer <c>503</c> for a request and
/// store no order, or leave a request's call unknown for good, so that an operator has to resolve it.
/// Orders live in memory only.
/// </summary>
public static class PurchasingStubApp
{
    private const string IdempotencyHeader = "Idempotency-Key";

    public static WebApplication Create(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<PurchaseOrderStore>();

        var app = builder.Build();

        app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

        app.MapPost("/purchase-orders", async (HttpContext context, CreatePurchaseOrder? body, PurchaseOrderStore store) =>
        {
            var key = context.Request.Headers[IdempotencyHeader].ToString();
            if (string.IsNullOrWhiteSpace(key))
            {
                return Results.Problem($"The {IdempotencyHeader} header is required.", statusCode: StatusCodes.Status400BadRequest);
            }
            if (string.IsNullOrWhiteSpace(body?.RequestNumber) || body.Amount is null)
            {
                return Results.Problem("requestNumber and amount are required.", statusCode: StatusCodes.Status400BadRequest);
            }

            if (store.IsUnavailable(body.RequestNumber))
            {
                return Results.Problem("The purchasing system is unavailable.", statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var (order, created, drop, misfit, hold) = store.Create(key, body.RequestNumber, body.SupplierCode, body.Amount.Value);
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

        app.MapGet("/purchase-orders/by-key/{idempotencyKey}", (string idempotencyKey, PurchaseOrderStore store) =>
        {
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
}

internal sealed record CreatePurchaseOrder(string? RequestNumber, string? SupplierCode, decimal? Amount);
