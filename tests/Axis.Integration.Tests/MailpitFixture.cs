using System.Net.Http.Json;
using System.Text.Json.Nodes;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

[assembly: AssemblyFixture(typeof(Axis.Integration.Tests.MailpitFixture))]

namespace Axis.Integration.Tests;

/// <summary>
/// One real Mailpit container, a mail server that keeps every email it receives, shared by the whole run. It starts the
/// first time a test needs it, so a run that needs no mail server never starts one. The fixture removes the container
/// when the run ends. Every test uses addresses of its own and reads only the emails sent to them.
/// </summary>
public sealed class MailpitFixture : IAsyncDisposable
{
    public const string Image = "axllent/mailpit:v1.32.0";

    private const int SmtpContainerPort = 1025;
    private const int ApiContainerPort = 8025;

    // The start takes no test's cancellation token, so cancelling one test does not fail the other.
    private readonly Lazy<Task<IContainer>> _container = new(StartContainerAsync);
    private readonly HttpClient _client = new();

    /// <summary>Starts the container if no test has yet, and returns the host that the SMTP port is published on.</summary>
    public async Task<string> SmtpHostAsync() => (await _container.Value).Hostname;

    /// <summary>Starts the container if no test has yet, and returns the published SMTP port.</summary>
    public async Task<int> SmtpPortAsync() => (await _container.Value).GetMappedPublicPort(SmtpContainerPort);

    /// <summary>
    /// Every email sent to an address that contains <paramref name="addressPart"/>, with its plain text, <c>Message-ID</c>
    /// without angle brackets, subject and <c>Content-Type</c> header, oldest first.
    /// </summary>
    public async Task<IReadOnlyList<MailpitMessage>> MessagesToAsync(string addressPart, CancellationToken cancellationToken)
    {
        var container = await _container.Value;
        var api = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(ApiContainerPort)}/");
        var list = await _client.GetFromJsonAsync<JsonObject>(new Uri(api, "api/v1/messages?limit=1000"), cancellationToken);
        var messages = new List<MailpitMessage>();
        foreach (var summary in (list!["messages"]!.AsArray()).OrderBy(message => message!["Created"]!.GetValue<DateTimeOffset>()))
        {
            var id = summary!["ID"]!.GetValue<string>();
            var message = (await _client.GetFromJsonAsync<JsonObject>(new Uri(api, $"api/v1/message/{id}"), cancellationToken))!;
            var recipients = message["To"]!.AsArray().Select(recipient => recipient!["Address"]!.GetValue<string>()).ToList();
            if (!recipients.Any(address => address.Contains(addressPart, StringComparison.Ordinal)))
            {
                continue;
            }

            var headers = (await _client.GetFromJsonAsync<JsonObject>(new Uri(api, $"api/v1/message/{id}/headers"), cancellationToken))!;
            messages.Add(new MailpitMessage(
                recipients,
                message["Subject"]!.GetValue<string>(),
                message["Text"]!.GetValue<string>(),
                message["MessageID"]!.GetValue<string>(),
                headers["Content-Type"]!.AsArray()[0]!.GetValue<string>()));
        }

        return messages;
    }

    public async ValueTask DisposeAsync()
    {
        _client.Dispose();

        // A failed start already removed its container.
        if (_container.IsValueCreated && _container.Value.IsCompletedSuccessfully)
        {
            await _container.Value.Result.DisposeAsync();
        }
    }

    private static async Task<IContainer> StartContainerAsync()
    {
        var container = new ContainerBuilder(Image)
            .WithPortBinding(SmtpContainerPort, assignRandomHostPort: true)
            .WithPortBinding(ApiContainerPort, assignRandomHostPort: true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(ApiContainerPort).ForPath("/api/v1/messages")))
            .Build();
        try
        {
            await container.StartAsync();
            return container;
        }
        catch
        {
            await container.DisposeAsync();
            throw;
        }
    }
}

/// <summary>An email that Mailpit received.</summary>
/// <param name="To">The recipient addresses.</param>
/// <param name="Subject">The decoded subject.</param>
/// <param name="Text">The decoded plain text body.</param>
/// <param name="MessageId">The <c>Message-ID</c> without angle brackets.</param>
/// <param name="ContentType">The <c>Content-Type</c> header of the message.</param>
public sealed record MailpitMessage(IReadOnlyList<string> To, string Subject, string Text, string MessageId, string ContentType);
