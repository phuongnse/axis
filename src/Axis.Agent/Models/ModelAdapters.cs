namespace Axis.Agent.Models;

/// <summary>Hands out the adapter of a binding, by the binding's kind.</summary>
public sealed class ModelAdapters
{
    /// <summary>The name of the HTTP client that the adapters use.</summary>
    public const string HttpClientName = "axis-model";

    private readonly Dictionary<string, IModelAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);
    private readonly AiSettings _settings;
    private readonly IHttpClientFactory _httpClients;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
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

        _settings = settings;
        _httpClients = httpClients;
        _timeProvider = timeProvider;
        _scripted = scripted;
        _logger = loggerFactory.CreateLogger("Axis.Agent.Models");
        foreach (var binding in settings.Bindings.Values)
        {
            _adapters[binding.Name] = Create(binding);
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

    /// <summary>
    /// The adapter of the binding with <paramref name="key"/> in place of the binding's own key, for a tenant's own key, see
    /// <c>docs/reference/model-bindings.md#a-tenants-own-key</c>. With a scripted adapter registered, it returns that adapter.
    /// </summary>
    /// <exception cref="InvalidOperationException">No binding has this name. The message names the setting and never the name or the key.</exception>
    internal IModelAdapter WithKey(string bindingName, string key)
    {
        if (_scripted is not null)
        {
            return _scripted;
        }

        if (!_settings.Bindings.TryGetValue(bindingName, out var binding))
        {
            throw new InvalidOperationException("The binding names no binding under 'Ai:Bindings'.");
        }

        return Create(new ModelBinding
        {
            Name = binding.Name,
            Kind = binding.Kind,
            Endpoint = binding.Endpoint,
            Model = binding.Model,
            Key = key,
            MaxOutputTokens = binding.MaxOutputTokens,
        });
    }

    private IModelAdapter Create(ModelBinding binding)
    {
        var client = _httpClients.CreateClient(HttpClientName);
        return binding.Kind switch
        {
            ModelBindingKind.Anthropic => new AnthropicModelAdapter(binding, client, _timeProvider, _logger),
            ModelBindingKind.OpenAiCompatible => new OpenAiCompatibleModelAdapter(binding, client, _timeProvider, _logger),
            _ => throw new ArgumentOutOfRangeException(nameof(binding), binding.Kind, null),
        };
    }
}
