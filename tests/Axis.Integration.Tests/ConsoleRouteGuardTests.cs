using System.Net;
using System.Text.Json;
using Axis.Server.Tests;
using Microsoft.AspNetCore.Mvc.Testing;
using static Axis.Integration.Tests.TaskApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// Holds every route under <c>/api/console/</c> of the real server to the operator console's rules, see
/// <see cref="ConsoleRouteGuard"/>. It has its own fixture, because the release that adds a sensitive field leaves
/// its <c>bytea</c> column in the tenant, and a later release without the field would be refused.
/// </summary>
public sealed class ConsoleRouteGuardTests(TaskApiFixture fixture) : IClassFixture<TaskApiFixture>
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Every_console_route_refuses_a_signed_out_caller_a_caller_without_the_role_and_a_write_without_the_csrf_header()
    {
        await using var factory = WithBindings(out _);
        var routes = ConsoleRouteGuard.Routes(factory.Services);

        Assert.NotEmpty(routes);
        Assert.Contains(routes, route => route.Method == "POST");
        var failures = await ConsoleRouteGuard.AccessFailuresAsync(routes, factory.CreateClient, Host(TenantA), "pia", "otto", CancellationToken);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task No_console_read_answers_a_sensitive_value_a_secret_a_binding_value_or_a_webhook_body()
    {
        var sensitive = $"guard-sensitive-{Guid.NewGuid():N}";
        var body = $"guard-body-{Guid.NewGuid():N}";
        await using var factory = WithBindings(out var bindings);
        var release = await fixture.ActivateEditedAsync(TenantA, AddSalary);
        var subject = await fixture.InsertRequestAsync(TenantA, null);
        var instance = await fixture.StartAsync(TenantA, "DepartmentOrder", subject, release);
        await fixture.ExecuteAsync(TenantA, "UPDATE axis.process_instances SET state = 'failed', ended_at = now() WHERE id = @id", ("id", instance));
        await fixture.InsertConnectorCallAsync(TenantA, instance, "createOrder", $$"""{"reference":"Sales","amount":"{{sensitive}}"}""");
        var delivery = await StageWebhookDeliveryAsync(body);

        var routes = ConsoleRouteGuard.Routes(factory.Services);
        var failures = await ConsoleRouteGuard.LeakFailuresAsync(
            routes,
            factory.CreateClient,
            Host(TenantA),
            "pia",
            new Dictionary<string, string> { ["id"] = instance.ToString("D") },
            [sensitive, body, .. bindings],
            CancellationToken);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));

        // The seeds are live: the console reads the call and the delivery, and holds back what it must.
        using var detail = await GetAsync(factory, $"/api/console/instances/{instance:D}");
        var call = Assert.Single(detail.RootElement.GetProperty("calls").EnumerateArray());
        Assert.True(call.GetProperty("inputHidden").GetBoolean());
        using var deliveries = await GetAsync(factory, "/api/console/webhook-deliveries?pageSize=100");
        Assert.Contains(
            deliveries.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == delivery);
    }

    // The values the server holds for the Purchasing secret and URL bindings of the tenant.
    private WebApplicationFactory<Program> WithBindings(out string[] values)
    {
        var secret = $"guard-secret-{Guid.NewGuid():N}";
        var url = $"https://guard-binding-{Guid.NewGuid():N}.example.test/api";
        values = [secret, url];
        return fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Tenants:a:Bindings:PurchasingToken:Kind", "secret");
            builder.UseSetting("Tenants:a:Bindings:PurchasingToken:Value", secret);
            builder.UseSetting("Tenants:a:Bindings:PurchasingUrl:Kind", "url");
            builder.UseSetting("Tenants:a:Bindings:PurchasingUrl:Value", url);
        });
    }

    private static async Task<JsonDocument> GetAsync(WebApplicationFactory<Program> factory, string path)
    {
        using var client = factory.CreateClient().SignedInAs("pia");
        using var request = RecordApiFixture.Request(path, Host(TenantA));
        using var response = await client.SendAsync(request, CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(CancellationToken));
    }

    // Stages a pending delivery with an open dead letter, and the event whose payload holds the body. Returns the delivery id.
    private async Task<Guid> StageWebhookDeliveryAsync(string body)
    {
        var applicationId = fixture.Model.Manifest.Id;
        var release = fixture.ReleaseId(TenantA);
        var webhook = Guid.NewGuid();
        var eventId = Guid.CreateVersion7();
        var delivery = Guid.CreateVersion7();
        var created = DateTimeOffset.UtcNow.AddDays(1);
        await fixture.ExecuteAsync(
            TenantA,
            """
            INSERT INTO axis.events (id, application_id, event, release_id, entity_id, subject_id, payload, process_instance_id, raised_at)
            VALUES (@event, @application, 'NoteAnnounced', @release, @entity, @subject, @payload::jsonb, NULL, @created)
            """,
            ("event", eventId),
            ("application", applicationId),
            ("release", release),
            ("entity", Guid.NewGuid()),
            ("subject", Guid.NewGuid()),
            ("payload", $$"""{"title":"{{body}}"}"""),
            ("created", created));
        await fixture.ExecuteAsync(
            TenantA,
            """
            INSERT INTO axis.webhook_deliveries (id, application_id, release_id, webhook_id, event_id, state, attempts, status_code, created_at)
            VALUES (@id, @application, @release, @webhook, @event, 'pending', 5, 503, @created)
            """,
            ("id", delivery),
            ("application", applicationId),
            ("release", release),
            ("webhook", webhook),
            ("event", eventId),
            ("created", created));
        await fixture.ExecuteAsync(
            TenantA,
            """
            INSERT INTO axis.dead_letters (id, kind, application_id, event_id, webhook_id, attempts, last_error, state, created_at)
            VALUES (@id, 'webhook.deliver', @application, @event, @webhook, 5, 'failed', 'open', @created)
            """,
            ("id", Guid.CreateVersion7()),
            ("application", applicationId),
            ("event", eventId),
            ("webhook", webhook),
            ("created", created));
        return delivery;
    }

    // Edits the Request entity to add the decimal field "salary", sensitive from the release that adds it. The
    // DepartmentOrder call maps "salary" as its amount.
    private static string? AddSalary(string path, string content)
    {
        if (path == System.IO.Path.Combine("entities", "request.json"))
        {
            return content.Replace(
                "\"fields\": [",
                "\"fields\": [\n    { \"name\": \"salary\", \"type\": \"decimal\", \"precision\": 20, \"scale\": 2, \"sensitive\": true },",
                StringComparison.Ordinal);
        }

        return path == System.IO.Path.Combine("processes", "department-order.json")
            ? content.Replace("\"amount\": \"amount\"", "\"amount\": \"salary\"", StringComparison.Ordinal)
            : content;
    }
}
