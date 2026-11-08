using System.Text.Json;

namespace Axis.Agent.Models;

/// <summary>
/// Calls the model of a binding. The request loop talks to this interface and does not know which provider is behind it,
/// see <c>docs/reference/model-bindings.md#bindings</c>.
/// </summary>
public interface IModelAdapter
{
    /// <summary>Sends one call and returns the model's answer.</summary>
    /// <exception cref="ModelCallException">The provider answered with an error, or could not be reached. The message never holds the key.</exception>
    Task<ModelAnswer> CallAsync(ModelCall request, CancellationToken cancellationToken);
}

/// <summary>One call: the system text, the conversation so far, the tools the model may use and an output limit.</summary>
/// <param name="MaxOutputTokens">The largest output of this call, or <see langword="null"/> for the binding's limit. The binding's limit is never exceeded.</param>
public sealed record ModelCall(string System, IReadOnlyList<ModelMessage> Messages, IReadOnlyList<ModelTool> Tools, int? MaxOutputTokens = null);

public enum ModelRole
{
    User,
    Assistant,
}

public sealed record ModelMessage(ModelRole Role, IReadOnlyList<ModelContent> Content);

public abstract record ModelContent;

public sealed record ModelText(string Text) : ModelContent;

public sealed record ModelToolCall(string Id, string Name, JsonElement Input) : ModelContent;

public sealed record ModelToolResult(string CallId, string Content, bool IsError) : ModelContent;

public sealed record ModelTool(string Name, string Description, JsonElement InputSchema);

public enum ModelStopReason
{
    EndTurn,
    ToolUse,
    MaxTokens,
}

/// <summary>The tokens of one call, as the provider counts them.</summary>
public sealed record ModelTokens(int Input, int Output);

public sealed record ModelAnswer(IReadOnlyList<ModelContent> Content, ModelStopReason StopReason, ModelTokens Tokens);

/// <summary>A model call failed. The message names the binding and, for a provider's error, the status code, and never a key, a body or a header.</summary>
public sealed class ModelCallException(string message) : Exception(message);
