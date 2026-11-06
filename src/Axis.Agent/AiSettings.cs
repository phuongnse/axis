using System.Globalization;

namespace Axis.Agent;

/// <summary>The model bindings of the <c>Ai</c> settings, and the names of the default and review bindings.</summary>
public sealed class AiSettings
{
    private const string BindingsSection = "Ai:Bindings";
    private const string DefaultBindingSetting = "Ai:DefaultBinding";
    private const string ReviewBindingSetting = "Ai:ReviewBinding";

    /// <summary>The settings this class reads, as the model-bindings page names them.</summary>
    public static IReadOnlyList<string> SettingNames { get; } =
    [
        $"{BindingsSection}:<name>:Kind",
        $"{BindingsSection}:<name>:Endpoint",
        $"{BindingsSection}:<name>:Model",
        $"{BindingsSection}:<name>:KeyFile",
        $"{BindingsSection}:<name>:MaxOutputTokens",
        DefaultBindingSetting,
        ReviewBindingSetting,
    ];

    /// <summary>The bindings by name.</summary>
    public required IReadOnlyDictionary<string, ModelBinding> Bindings { get; init; }

    /// <summary>The binding that requests use, or <see langword="null"/> when AI authoring is off.</summary>
    public string? DefaultBinding { get; init; }

    /// <summary>The binding of the review session, or <see langword="null"/> to use the default binding.</summary>
    public string? ReviewBinding { get; init; }

    /// <summary>
    /// Reads every value by hand and never binds, because a conversion error of the binder quotes the value.
    /// Each error names its setting and never a value, a path or a key.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A binding has an unknown kind, no model, a key file that cannot be read or a <c>MaxOutputTokens</c> that is not a whole number of at least 1,
    /// or the default or review binding names no binding.
    /// </exception>
    public static AiSettings Load(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var bindings = new Dictionary<string, ModelBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var section in configuration.GetSection(BindingsSection).GetChildren())
        {
            bindings[section.Key] = LoadBinding(configuration, section.Key);
        }

        return new AiSettings
        {
            Bindings = bindings,
            DefaultBinding = NamedBinding(configuration, DefaultBindingSetting, bindings),
            ReviewBinding = NamedBinding(configuration, ReviewBindingSetting, bindings),
        };
    }

    private static ModelBinding LoadBinding(IConfiguration configuration, string name)
    {
        var prefix = $"{BindingsSection}:{name}";

        var kind = configuration[$"{prefix}:Kind"] switch
        {
            "anthropic" => ModelBindingKind.Anthropic,
            "openai-compatible" => ModelBindingKind.OpenAiCompatible,
            _ => throw new InvalidOperationException($"'{prefix}:Kind' must be 'anthropic' or 'openai-compatible'."),
        };

        if (string.IsNullOrWhiteSpace(configuration[$"{prefix}:Model"]))
        {
            throw new InvalidOperationException($"'{prefix}:Model' must not be empty.");
        }

        return new ModelBinding
        {
            Name = name,
            Kind = kind,
            Endpoint = Text(configuration[$"{prefix}:Endpoint"]),
            Model = configuration[$"{prefix}:Model"]!.Trim(),
            Key = ReadKey(configuration[$"{prefix}:KeyFile"], $"{prefix}:KeyFile"),
            MaxOutputTokens = MaxOutputTokens(configuration[$"{prefix}:MaxOutputTokens"], $"{prefix}:MaxOutputTokens"),
        };
    }

    private static string? ReadKey(string? file, string setting)
    {
        if (string.IsNullOrWhiteSpace(file))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(file).Trim();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The exception is not kept: its message holds the path.
            throw new InvalidOperationException($"'{setting}' names a file that cannot be read.");
        }
    }

    private static int MaxOutputTokens(string? text, string setting)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ModelBinding.DefaultMaxOutputTokens;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var tokens) && tokens >= 1
            ? tokens
            : throw new InvalidOperationException($"'{setting}' must be a whole number of at least 1.");
    }

    private static string? NamedBinding(IConfiguration configuration, string setting, Dictionary<string, ModelBinding> bindings)
    {
        var name = Text(configuration[setting]);
        return name is null || bindings.ContainsKey(name)
            ? name
            : throw new InvalidOperationException($"'{setting}' names no binding under '{BindingsSection}'.");
    }

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
