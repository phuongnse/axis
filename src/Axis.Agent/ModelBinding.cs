namespace Axis.Agent;

/// <summary>The kind of a model binding, which names the adapter that calls its model.</summary>
public enum ModelBindingKind
{
    /// <summary>Claude through the Anthropic API, Amazon Bedrock or Google Vertex AI.</summary>
    Anthropic,

    /// <summary>Any endpoint that speaks the OpenAI API.</summary>
    OpenAiCompatible,
}

/// <summary>
/// A model and how to reach it, read from <c>Ai:Bindings:&lt;name&gt;</c>. It is a class and not a record, so that no
/// generated <c>ToString</c> prints the key.
/// </summary>
public sealed class ModelBinding
{
    public const int DefaultMaxOutputTokens = 16000;

    public required string Name { get; init; }

    public required ModelBindingKind Kind { get; init; }

    /// <summary>The API's base URL, or <see langword="null"/> for the provider's own.</summary>
    public string? Endpoint { get; init; }

    public required string Model { get; init; }

    /// <summary>The API key read from <c>KeyFile</c>, or <see langword="null"/> for a model that needs none. Never write it to a log.</summary>
    public string? Key { get; init; }

    public int MaxOutputTokens { get; init; } = DefaultMaxOutputTokens;
}
