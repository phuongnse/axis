using System.Diagnostics;
using System.Net;
using System.Text.Json;
using static Axis.Integration.Tests.PerformanceServerFixture;
using static Axis.Integration.Tests.RecordApiFixture;

namespace Axis.Integration.Tests;

/// <summary>
/// The form state endpoint answers a typical form within the time limit: the <c>ClaimEdit</c> form of the
/// FormPerformanceApp fixture has 25 fields, each with a visible, required or read-only condition, five
/// validations and a collection of 50 line items. Each draft sends all 25 values and all the line items.
/// <see cref="FormStatePerformanceTests"/> times a small form. The class runs alone, so no other test competes for
/// CPU with the timed calls.
/// </summary>
[Collection(PerformanceTiming.Name)]
public sealed class TypicalFormStatePerformanceTests(TypicalFormStatePerformanceTests.FormFixture fixture) : IClassFixture<TypicalFormStatePerformanceTests.FormFixture>
{
    // The limit is the one the user feels while typing: the SPA asks for the states after each change.
    private static readonly TimeSpan _callTime = TimeSpan.FromMilliseconds(300);

    private const int WarmUpCalls = 10;
    private const int Attempts = 3;
    private const int Calls = 100;
    private const int LineItems = 50;
    private const int DiscountErrorCall = 50;
    private const string State = "/api/apps/FormPerformanceApp/forms/ClaimEdit/state";

    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_hundred_calls_on_a_form_of_25_fields_and_50_line_items_each_answer_within_the_time_limit()
    {
        for (var warmUp = 0; warmUp < WarmUpCalls; warmUp++)
        {
            using var response = await SendAsync(Body(warmUp));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var slowest = TimeSpan.Zero;
        for (var call = 0; call < Calls; call++)
        {
            // A shared runner can pause the process for a garbage collection or another job, so a call over the
            // limit is timed again. Only a call that stays over the limit on every attempt fails.
            var (elapsed, body) = await TimedCallAsync(call);
            for (var attempt = 1; attempt < Attempts && elapsed >= _callTime; attempt++)
            {
                var retry = await TimedCallAsync(call);
                (elapsed, body) = retry.Elapsed < elapsed ? retry : (elapsed, body);
            }

            Assert.True(elapsed < _callTime, $"Call {call} took {elapsed} at best of {Attempts} attempts.");
            slowest = elapsed > slowest ? elapsed : slowest;

            using var state = JsonDocument.Parse(body);
            AssertStates(state, call);
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"The slowest call took {slowest}.");
    }

    /// <summary>
    /// The draft is a special, submitted claim of more than 1,000, so its project, address and approval parts
    /// show and its cost center is required. Call <see cref="DiscountErrorCall"/> has a discount above its amount.
    /// </summary>
    private static void AssertStates(JsonDocument state, int call)
    {
        var root = state.RootElement;
        var fields = root.GetProperty("fields");

        // The 25 fields and the line items.
        Assert.Equal(26, fields.EnumerateObject().Count());
        Assert.Equal(
            [("general", true), ("money", true), ("approval", true), ("contact", true), ("address", true), ("notes", true)],
            root.GetProperty("sections").EnumerateArray().Select(section => (section.GetProperty("name").GetString()!, section.GetProperty("visible").GetBoolean())));

        // The draft is submitted, not approved and not rejected.
        Assert.True(Flag(fields, "project", "visible"));
        Assert.False(Flag(fields, "approvalNote", "visible"));
        Assert.False(Flag(fields, "rejectionReason", "visible"));
        Assert.True(Flag(fields, "urgent", "visible"));
        Assert.True(Flag(fields, "lineTotal", "visible"));
        Assert.True(Flag(fields, "kind", "required"));
        Assert.False(Flag(fields, "contactName", "required"));
        Assert.True(Flag(fields, "total", "readOnly"));
        Assert.False(Flag(fields, "priority", "readOnly"));
        Assert.True(Flag(fields, "reference", "readOnly"));
        Assert.Equal(call != DiscountErrorCall, Flag(fields, "costCenter", "required"));

        var errors = root.GetProperty("errors");
        if (call == DiscountErrorCall)
        {
            Assert.Equal(["claim.discountTooHigh"], errors.GetProperty("/values/discount").EnumerateArray().Select(message => message.GetString()));
            Assert.Single(errors.EnumerateObject());
        }
        else
        {
            Assert.Equal(["Required."], errors.GetProperty("/values/costCenter").EnumerateArray().Select(message => message.GetString()));
            Assert.Single(errors.EnumerateObject());
        }
    }

    private static bool Flag(JsonElement fields, string field, string state) => fields.GetProperty(field).GetProperty(state).GetBoolean();

    private async Task<(TimeSpan Elapsed, string Body)> TimedCallAsync(int call)
    {
        var body = Body(call);
        var stopwatch = Stopwatch.StartNew();
        using var response = await SendAsync(body);
        var answer = await response.Content.ReadAsStringAsync(CancellationToken);
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (stopwatch.Elapsed, answer);
    }

    private async Task<HttpResponseMessage> SendAsync(string body)
    {
        using var request = Request(HttpMethod.Post, State, Host, body);
        return await fixture.Client.SendAsync(request, CancellationToken);
    }

    /// <summary>
    /// A draft of a new claim with every writable field and 50 line items. The amount changes with the call, so
    /// no two calls send the same draft. Call <see cref="DiscountErrorCall"/> has a discount above its amount.
    /// </summary>
    private static string Body(int call)
    {
        var amount = call == DiscountErrorCall ? 10 : 1_001 + call;
        var lines = string.Join(
            ", ",
            Enumerable.Range(1, LineItems).Select(line => $$"""{ "description": "Line {{line}}", "quantity": {{line}}, "unitPrice": {{line}}.25 }"""));
        return $$"""
            {
              "recordId": null,
              "values": {
                "title": "Claim {{call}}", "kind": "special", "status": "submitted", "priority": "high", "urgent": true,
                "amount": {{amount}}, "discount": 50, "costCenter": null, "project": "Axis", "reference": "REF-{{call}}",
                "approver": "Binh", "approvalNote": null, "rejectionReason": null,
                "contactName": "Contact User", "contactEmail": "contact@example.test", "contactPhone": "0123456789",
                "addressLine": "1 Main Street", "city": "Hanoi", "country": "VN", "postalCode": "100000",
                "dueOn": "2026-12-31", "notes": "A typical claim.",
                "lines": [{{lines}}]
              },
              "touched": ["amount", "discount", "costCenter", "contactEmail", "lines"]
            }
            """;
    }

    /// <summary>The FormPerformanceApp fixture, served to a clerk who may create and update claims.</summary>
    public sealed class FormFixture : PerformanceServerFixture
    {
        public FormFixture()
            : base("FormPerformanceApp", "clerk")
        {
        }
    }
}
