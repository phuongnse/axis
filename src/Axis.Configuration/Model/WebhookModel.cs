namespace Axis.Configuration.Model;

/// <summary>
/// A compiled webhook: it sends an event to an outside system. Its URL and its signing secret are
/// deployment bindings, held only by name, and activation checks that the tenant has them with the
/// right kind.
/// </summary>
public sealed record WebhookModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The webhook's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    /// <summary>The event that is sent. It is an event of the application or of a declared module.</summary>
    public required EventReference Event { get; init; }

    /// <summary>The name of the <c>url</c> binding that holds where the event is sent.</summary>
    public required string UrlBinding { get; init; }

    /// <summary>The name of the <c>secret</c> binding that holds the key that signs the event.</summary>
    public required string SecretBinding { get; init; }

    /// <summary>How long one delivery may take: at most 30 seconds, 10 seconds when not written.</summary>
    public required TimeSpan Timeout { get; init; }
}
