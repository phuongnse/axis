using System.Diagnostics;
using System.Reflection;
using Wolverine;

namespace Axis.Shared.Infrastructure.Messaging;

/// <summary>
/// Stamps CloudEvents 1.0 metadata on Wolverine envelopes (ADR-019) for Kafka consumers and observability.
/// </summary>
public sealed class CloudEventsEnvelopeRule : IEnvelopeRule
{
    public const string SpecVersion = "1.0";
    public const string Source = "/axis/modulith";

    public void Modify(Envelope envelope) => ApplyCloudEventHeaders(envelope);

    public void ApplyCorrelation(IMessageContext originator, Envelope outgoing)
    {
        ApplyCloudEventHeaders(outgoing);

        if (originator.Envelope is null)
            return;

        if (originator.Envelope.Headers.TryGetValue("ce-id", out string? parentId) && parentId is not null)
            outgoing.Headers["ce-parentid"] = parentId;

        if (originator.Envelope.Headers.TryGetValue("correlationid", out string? correlationId)
            && correlationId is not null)
        {
            outgoing.Headers["correlationid"] = correlationId;
        }
    }

    private static void ApplyCloudEventHeaders(Envelope envelope)
    {
        if (envelope.Message is null)
            return;

        envelope.Headers["ce-specversion"] = SpecVersion;
        envelope.Headers["ce-id"] = envelope.Id.ToString();
        envelope.Headers["ce-time"] = DateTimeOffset.UtcNow.ToString("O");
        envelope.Headers["ce-type"] = envelope.Message.GetType().FullName;
        envelope.Headers["ce-source"] = Source;

        string? tenantId = ResolveTenantId(envelope.Message);
        if (tenantId is not null)
            envelope.Headers["tenantid"] = tenantId;

        Activity? activity = Activity.Current;
        if (activity is not null)
            envelope.Headers["traceparent"] = activity.Id;
    }

    private static string? ResolveTenantId(object message)
    {
        PropertyInfo? property = message.GetType().GetProperty("teamAccountId");
        if (property?.GetValue(message) is string teamAccountId && !string.IsNullOrWhiteSpace(teamAccountId))
            return teamAccountId;

        return null;
    }
}
