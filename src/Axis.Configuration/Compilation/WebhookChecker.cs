using Axis.Configuration.Diagnostics;
using Axis.Configuration.Model;
using Axis.Configuration.Resources;

namespace Axis.Configuration.Compilation;

/// <summary>
/// Checks the webhooks of an application and builds the checked ones. A webhook names a loaded
/// event, which may be an event of a module that the application declares, as <c>module/Name</c>.
/// A name under a module that the application does not declare is <c>AXC0207</c>. An event whose
/// file did not load is not reported again. A webhook needs a <c>url</c> and a <c>secret</c> binding,
/// and its <c>timeout</c> is a positive duration of at most 30 seconds, 10 seconds when not written.
/// Every problem of a webhook is reported in one pass. Its bindings are checked at activation,
/// against the tenant's.
/// </summary>
internal static class WebhookChecker
{
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _maxTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Checks <paramref name="webhooks"/>, which come in path order.</summary>
    public static void Check(
        IReadOnlyList<WebhookResource> webhooks,
        Func<string, EventResource?> findEvent,
        IReadOnlySet<string> unloadedEventNames,
        ModuleReferences moduleReferences,
        ReferenceIndexBuilder references,
        List<Diagnostic> diagnostics)
    {
        foreach (var webhook in webhooks)
        {
            void Report(string code, string message, string path) =>
                diagnostics.Add(new Diagnostic(code, message, webhook.File, path, webhook.Id));

            if (moduleReferences.Find(webhook.Event, findEvent) is { } @event)
            {
                references.Add(webhook, "/event", ReferenceRelations.WebhookEvent, ResourceNode.Of(@event));
            }
            else if (!moduleReferences.ReportUndeclared(webhook.Event, "/event", Report) && !unloadedEventNames.Contains(webhook.Event))
            {
                // An event file that was not loaded because of its own errors is not reported again.
                Report(
                    DiagnosticCodes.UnknownWebhookEvent,
                    $"The event '{webhook.Event}' was not found. No loaded event has that name.",
                    "/event");
            }

            if (webhook.Url is null)
            {
                Report(
                    DiagnosticCodes.MissingWebhookBinding,
                    "A webhook needs a 'url' binding. Write it as { \"binding\": \"<name>\" }.",
                    "/url");
            }

            if (webhook.Secret is null)
            {
                Report(
                    DiagnosticCodes.MissingWebhookBinding,
                    "A webhook needs a 'secret' binding to sign what it sends. Write it as { \"binding\": \"<name>\" }.",
                    "/secret");
            }

            if (webhook.Timeout is { } timeout && !TryParseTimeout(timeout, out _))
            {
                Report(
                    DiagnosticCodes.InvalidWebhookTimeout,
                    $"The timeout '{timeout}' is not allowed. Use a positive ISO 8601 duration of at most PT30S, such as 'PT10S'.",
                    "/timeout");
            }
        }
    }

    /// <summary>Builds a checked webhook.</summary>
    public static WebhookModel Build(WebhookResource webhook, Func<string, EventResource?> findEvent)
    {
        if (findEvent(webhook.Event) is not { } @event
            || webhook.Url is not { } url
            || webhook.Secret is not { } secret
            || !TryParseTimeout(webhook.Timeout, out var timeout))
        {
            throw new ArgumentException("Only a checked webhook can be built.", nameof(webhook));
        }

        return new WebhookModel
        {
            Id = webhook.Id,
            Name = webhook.Name,
            File = webhook.File,
            Event = new EventReference(@event.Id, @event.Name),
            UrlBinding = url.Binding,
            SecretBinding = secret.Binding,
            Timeout = timeout,
        };
    }

    /// <summary>Parses a timeout, which is 10 seconds when not written.</summary>
    private static bool TryParseTimeout(string? text, out TimeSpan timeout)
    {
        if (text is null)
        {
            timeout = _defaultTimeout;
            return true;
        }

        return IsoDuration.TryParse(text, out timeout) && timeout <= _maxTimeout;
    }
}
