namespace Axis.Agent.Models;

/// <summary>Hands out the adapter of a binding, by the binding's kind.</summary>
public sealed class ModelAdapters
{
    /// <summary>The name of the HTTP client that the adapters use.</summary>
    public const string HttpClientName = "axis-model";

    private readonly Dictionary<string, IModelAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ScriptedModelAdapter? _scripted;

    public ModelAdapters(
        AiSettings settings,
        IHttpClientFactory httpClients,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        ScriptedModelAdapter? scripted = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(httpClients);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _scripted = scripted;
        var logger = loggerFactory.CreateLogger("Axis.Agent.Models");
        foreach (var binding in settings.Bindings.Values)
        {
            var client = httpClients.CreateClient(HttpClientName);
            _adapters[binding.Name] = binding.Kind switch
            {
                ModelBindingKind.Anthropic => new AnthropicModelAdapter(binding, client, timeProvider, logger),
                ModelBindingKind.OpenAiCompatible => new OpenAiCompatibleModelAdapter(binding, client, timeProvider, logger),
                _ => throw new ArgumentOutOfRangeException(nameof(settings), binding.Kind, null),
            };
        }
    }

    /// <summary>The adapter of the binding. With a scripted adapter registered, every binding gets it.</summary>
    /// <exception cref="InvalidOperationException">No binding has this name. The message names the setting and never the name.</exception>
    public IModelAdapter For(string bindingName)
    {
        if (_scripted is not null)
        {
            return _scripted;
        }

        return _adapters.TryGetValue(bindingName, out var adapter)
            ? adapter
            : throw new InvalidOperationException("The binding names no binding under 'Ai:Bindings'.");
    }
}
