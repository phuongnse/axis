using System.Collections.Concurrent;

namespace Axis.Agent.Models;

/// <summary>Replays recorded answers in order, for tests. Register it with <c>AddScriptedModelAdapter</c>, which refuses it outside Development and Testing.</summary>
public sealed class ScriptedModelAdapter(IEnumerable<ModelAnswer> answers) : IModelAdapter
{
    private readonly ConcurrentQueue<ModelAnswer> _answers = new(answers);
    private readonly ConcurrentQueue<ModelCall> _calls = new();

    /// <summary>Every call the adapter got, in order.</summary>
    public IReadOnlyList<ModelCall> Calls => [.. _calls];

    public Task<ModelAnswer> CallAsync(ModelCall request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _calls.Enqueue(request);
        return _answers.TryDequeue(out var answer)
            ? Task.FromResult(answer)
            : throw new ModelCallException("The scripted adapter has no answer left.");
    }
}
