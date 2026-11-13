using System.Globalization;
using Axis.Extensions;

namespace Axis.Configuration.Packages;

/// <summary>
/// What the worker gives a package operation. Its <see cref="OperationId"/> is the call's operation
/// identity. It holds only the <c>url</c> and <c>secret</c> bindings the operation's manifest
/// declares, with the current tenant's values. <see cref="GetHttpClient"/> returns a client whose
/// base address is the declared binding's value and whose requests may go only to the scheme, host
/// and port of a declared <c>url</c> binding. Asking for a binding that is not declared, or sending
/// a request to another host, throws. The first such violation is kept in <see cref="Violation"/>
/// whatever the operation does with the exception, so the worker can fail the call. A violation
/// message names the operation, the package and the binding, never a value or the refused host.
/// <see cref="ViolationBinding"/> is the binding alone, which a package runner sends in <c>failure</c>.
/// </summary>
public sealed class PackageOperationContext(
    Guid operationId,
    string package,
    string operation,
    IReadOnlyDictionary<string, Uri> urls,
    IReadOnlyDictionary<string, Func<string>> secrets,
    HttpMessageHandler transport) : IOperationContext
{
    private readonly IReadOnlyDictionary<string, Uri> _urls = urls;
    private Refusal? _violation;

    public Guid OperationId { get; } = operationId;

    /// <summary>The first violation of the declared capabilities, or <see langword="null"/> when there is none.</summary>
    public string? Violation => Volatile.Read(ref _violation)?.Message;

    /// <summary>The binding of the first violation: the one the operation asked for, or the <c>url</c> binding the refused client came from.</summary>
    public string? ViolationBinding => Volatile.Read(ref _violation)?.Binding;

    public HttpClient GetHttpClient(string urlBinding)
    {
        ArgumentNullException.ThrowIfNull(urlBinding);

        if (!_urls.TryGetValue(urlBinding, out var baseAddress))
        {
            throw Refuse(urlBinding, $"asked for the url binding '{urlBinding}', which it does not declare.");
        }

        // A trailing slash makes a relative request path extend the base path instead of replacing its last segment.
        var text = baseAddress.AbsoluteUri;
        return new HttpClient(new DeclaredHostHandler(this, urlBinding) { InnerHandler = transport }, disposeHandler: false)
        {
            BaseAddress = text.EndsWith('/') ? baseAddress : new Uri(text + "/"),
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    public string GetSecret(string secretBinding)
    {
        ArgumentNullException.ThrowIfNull(secretBinding);

        return secrets.TryGetValue(secretBinding, out var secret)
            ? secret()
            : throw Refuse(secretBinding, $"asked for the secret binding '{secretBinding}', which it does not declare.");
    }

    /// <summary>
    /// The sentence of a violation, one shape for a context and for the worker that gets a runner's <c>failure</c>:
    /// the operation and the package, then <paramref name="what"/> it did. It never holds a value.
    /// </summary>
    public static string RefusedMessage(string operation, string package, string what) =>
        string.Create(CultureInfo.InvariantCulture, $"The operation '{operation}' of the package '{package}' {what}");

    /// <summary>Keeps the first violation and returns the exception that refuses it.</summary>
    private InvalidOperationException Refuse(string binding, string what)
    {
        var message = RefusedMessage(operation, package, what);
        Interlocked.CompareExchange(ref _violation, new Refusal(message, binding), null);
        return new InvalidOperationException(message);
    }

    /// <summary>A violation: the message for the worker's history and the binding for a runner's <c>failure</c>.</summary>
    private sealed record Refusal(string Message, string Binding);

    /// <summary>Lets a request through only when its scheme, host and port are those of a declared <c>url</c> binding.</summary>
    private sealed class DeclaredHostHandler(PackageOperationContext context, string binding) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not { IsAbsoluteUri: true } target
                || !context._urls.Values.Any(declared =>
                    Uri.Compare(target, declared, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0))
            {
                throw context.Refuse(binding, $"sent a request through the url binding '{binding}' to a host that none of its url bindings declares.");
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
