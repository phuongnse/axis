namespace Axis.Tenancy.Platform;

/// <summary>
/// A row of <c>platform.tenant_token_usage</c>: the tokens that the model calls of one tenant used on one UTC day, for
/// one binding and model, and for tokens paid by the tenant's own key apart from tokens paid by the installation's key. It holds
/// counts only, so it never holds a prompt, an answer or a key.
/// </summary>
public sealed class TenantTokenUsageRow
{
    public string TenantId { get; set; } = "";

    /// <summary>The UTC day.</summary>
    public DateOnly Day { get; set; }

    /// <summary>The name of the model binding.</summary>
    public string Binding { get; set; } = "";

    /// <summary>The model of the binding, as the binding names it.</summary>
    public string Model { get; set; } = "";

    /// <summary>Whether the tenant's own model key paid these tokens.</summary>
    public bool OwnKey { get; set; }

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }
}
