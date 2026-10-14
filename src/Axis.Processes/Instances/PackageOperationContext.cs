using System.Globalization;
using Axis.Extensions;
using Axis.Tenancy;

namespace Axis.Processes.Instances;

/// <summary>
/// What the worker gives a package operation. Its <see cref="OperationId"/> is the call's operation
/// identity. It holds only the <c>url</c> and <c>secret</c> bindings the operation's manifest
/// declares, with the current tenant's values. <see cref="GetHttpClient"/> returns a client whose
/// base address is the declared binding's value and whose requests may go only to the scheme, host
/// and port of a declared <c>url</c> binding. Asking for a binding that is not declared, or sending
/// a request to another host, throws. The first such violation is kept in <see cref="Violation"/>
/// whatever the operation does with the exception, so the worker can fail the call. A violation
/// message names the operation, the package and the binding, never a value or the refused host.
/// </summary>
internal sealed class PackageOperationContext(
    Guid operationId,
    string package,
    string operation,
    IReadOnlyDictionary<string, Uri> urls,
    IReadOnlyDictionary<string, SecretValue> secrets,
    HttpMessageHandler transport) : IOperationContext
{
    private readonly IReadOnlyDictionary<string, Uri> _urls = urls;
    private string? _violation;

    public Guid OperationId { get; } = operationId;

    /// <summary>The first violation of the declared capabilities, or <see langword="null"/> when there is none.</summary>
    public string? Violation => Volatile.Read(ref _violation);

    public HttpClient GetHttpClient(string urlBinding)
    {
        ArgumentNullException.ThrowIfNull(urlBinding);

        if (!_urls.TryGetValue(urlBinding, out var baseAddress))
        {
            throw Refuse($"asked for the url binding '{urlBinding}', which it does not declare.");
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
            ? secret.Reveal()
            : throw Refuse($"asked for the secret binding '{secretBinding}', which it does not declare.");
    }

    /// <summary>Keeps the first violation and returns the exception that refuses it.</summary>
    private InvalidOperationException Refuse(string what)
    {
        var message = string.Create(CultureInfo.InvariantCulture, $"The operation '{operation}' of the package '{package}' {what}");
        Interlocked.CompareExchange(ref _violation, message, null);
        return new InvalidOperationException(message);
    }

    /// <summary>Lets a request through only when its scheme, host and port are those of a declared <c>url</c> binding.</summary>
    private sealed class DeclaredHostHandler(PackageOperationContext context, string binding) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not { IsAbsoluteUri: true } target
                || !context._urls.Values.Any(declared =>
                    Uri.Compare(target, declared, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0))
            {
                throw context.Refuse($"sent a request through the url binding '{binding}' to a host that none of its url bindings declares.");
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
