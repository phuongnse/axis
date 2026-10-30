using System.Net;
using System.Text.Json;
using Axis.PurchasingStub;
using Axis.Server.Tests;
using static Axis.Integration.Tests.SampleAppFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// An approved purchase request sends its line items to the purchasing system with the order. The connector signs in with
/// the OAuth2 client credentials of the sample, so the stub holds the order only after a token request with them.
/// </summary>
public sealed class PurchaseRequestOrderLinesTests(SampleAppFixture fixture) : IClassFixture<SampleAppFixture>
{
    private const string App = "/api/apps/PurchaseRequests";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_approved_request_creates_one_order_with_both_line_items_after_a_token_request_with_the_client_credentials()
    {
        using var requester = fixture.CreateClient().SignedInAs("requester");
        using var manager2 = fixture.CreateClient().SignedInAs("manager-2");
        var request = await CreateRequestAsync(requester, Operations, ("Desk", 2, 150m), ("Chair", 3, 80m));
        await StartAsync(requester, request);
        await fixture.RunUntilIdleAsync(CancellationToken);

        await ApproveAsync(manager2, request);
        await fixture.RunUntilIdleAsync(CancellationToken);

        var (number, purchaseOrderNumber) = await NumbersAsync(requester, request);
        var order = Assert.Single(await fixture.PurchaseOrdersAsync(number));
        Assert.Equal(PurchasingStubApp.DefaultClientId, order.GetProperty("clientId").GetString());
        Assert.Equal(purchaseOrderNumber, order.GetProperty("orderNumber").GetString());
        Assert.Equal(540m, order.GetProperty("amount").GetDecimal());
        Assert.Equal(
            [("Desk", 2, 150m, 300m), ("Chair", 3, 80m, 240m)],
            order.GetProperty("lines").EnumerateArray().Select(line => (
                line.GetProperty("description").GetString()!,
                line.GetProperty("quantity").GetInt32(),
                line.GetProperty("unitPrice").GetDecimal(),
                line.GetProperty("amount").GetDecimal())));
    }

    /// <summary>Completes the open task of <paramref name="subject"/> with the approve outcome.</summary>
    private static async Task ApproveAsync(HttpClient client, Guid subject)
    {
        Guid task;
        using var request = RecordApiFixture.Request(HttpMethod.Get, $"{App}/tasks?pageSize=100", Host);
        using (var list = await client.SendAsync(request, CancellationToken))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            using var body = JsonDocument.Parse(await list.Content.ReadAsStringAsync(CancellationToken));
            task = Assert.Single(
                body.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("subject").GetProperty("id").GetGuid() == subject).GetProperty("id").GetGuid();
        }

        using var completed = await CompleteAsync(client, task, """{ "outcome": "approve" }""");
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
    }
}
