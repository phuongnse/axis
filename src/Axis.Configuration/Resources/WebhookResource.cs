namespace Axis.Configuration.Resources;

/// <summary>
/// A <c>webhook</c> resource: sends an event to an outside system. It names an event of the
/// application, or <c>module/Name</c> of a declared module's event, a <c>url</c> binding for where to
/// send it and a <c>secret</c> binding for signing it. The schema leaves <see cref="Url"/> and
/// <see cref="Secret"/> optional, so <see cref="Compilation.ApplicationCompiler"/> reports a missing
/// one with a code of its own. It also checks the event and the <see cref="Timeout"/>.
/// </summary>
public sealed record WebhookResource : Resource
{
    /// <summary>The name of the event that is sent.</summary>
    public required string Event { get; init; }

    /// <summary>The <c>url</c> binding that holds where the event is sent. The compiler requires it.</summary>
    public BindingReference? Url { get; init; }

    /// <summary>The <c>secret</c> binding that holds the key that signs the event. The compiler requires it.</summary>
    public BindingReference? Secret { get; init; }

    /// <summary>How long one delivery may take, as an ISO 8601 duration of at most <c>PT30S</c>. Null means <c>PT10S</c>.</summary>
    public string? Timeout { get; init; }
}
