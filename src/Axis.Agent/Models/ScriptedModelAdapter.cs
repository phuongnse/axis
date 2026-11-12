using System.Collections.Concurrent;
using System.Text.Json;

namespace Axis.Agent.Models;

/// <summary>
/// Replays recorded answers in order, for tests. Register it with <c>AddScriptedModelAdapter</c>, which refuses it outside Development and Testing,
/// or name a file of answers in <c>Ai:ScriptedAnswersFile</c>, see <c>docs/reference/model-bindings.md#bindings</c>.
/// </summary>
public sealed class ScriptedModelAdapter(IEnumerable<ModelAnswer> answers) : IModelAdapter
{
    private readonly ConcurrentQueue<ModelAnswer> _answers = new(answers);
    private readonly ConcurrentQueue<ModelCall> _calls = new();

    /// <summary>Every call the adapter got, in order.</summary>
    public IReadOnlyList<ModelCall> Calls => [.. _calls];

    /// <summary>Reads a JSON array of answers, each in the shape of a Messages API response.</summary>
    /// <exception cref="InvalidOperationException">The file cannot be read or does not hold an array of answers. The message names <paramref name="setting"/> and never the path.</exception>
    internal static IReadOnlyList<ModelAnswer> ReadAnswers(string file, string setting)
    {
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exception is not kept: its message holds the path.
            throw new InvalidOperationException($"'{setting}' names a file that cannot be read.");
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("The file holds no array.");
            }

            return [.. document.RootElement.EnumerateArray().Select(AnthropicModelAdapter.Answer)];
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            // The exception is not kept: its message can quote the content.
            throw new InvalidOperationException($"'{setting}' must hold a JSON array of Messages API answers.");
        }
    }

    public Task<ModelAnswer> CallAsync(ModelCall request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _calls.Enqueue(request);
        return _answers.TryDequeue(out var answer)
            ? Task.FromResult(answer)
            : throw new ModelCallException("The scripted adapter has no answer left.");
    }
}
