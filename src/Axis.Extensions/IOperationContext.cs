namespace Axis.Extensions;

/// <summary>What the platform gives an operation. It hands out only what the operation's manifest declares.</summary>
public interface IOperationContext
{
    /// <summary>The operation identity of the call: the idempotency key, the same on every attempt.</summary>
    Guid OperationId { get; }

    /// <summary>A client whose base address is the value of the declared <c>url</c> binding <paramref name="urlBinding"/>.</summary>
    HttpClient GetHttpClient(string urlBinding);

    /// <summary>The value of the declared <c>secret</c> binding <paramref name="secretBinding"/>.</summary>
    string GetSecret(string secretBinding);
}
