using System.Text.Json;
using System.Text.Json.Serialization;

namespace Axis.Tenancy;

/// <summary>
/// The value of a <c>secret</c> deployment binding. Its text and JSON forms are <c>[secret]</c>, so logging or
/// serializing it never shows the value. Read the value with <see cref="Reveal"/> only at the moment of a call.
/// </summary>
[JsonConverter(typeof(SecretValueJsonConverter))]
public sealed class SecretValue
{
    /// <summary>The text that stands for the value wherever a secret is shown.</summary>
    public const string Placeholder = "[secret]";

    private readonly Func<string> _reveal;

    public SecretValue(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _reveal = () => value;
    }

    private SecretValue(Func<string> reveal) => _reveal = reveal;

    /// <summary>
    /// A secret that is produced anew on each <see cref="Reveal"/>, so the plain value is never held in memory
    /// between calls. The tenant directory uses it to decrypt a stored secret only at the moment of use.
    /// </summary>
    internal static SecretValue Deferred(Func<string> reveal)
    {
        ArgumentNullException.ThrowIfNull(reveal);
        return new SecretValue(reveal);
    }

    /// <summary>Returns the plain value. Pass it straight to the call that needs it, and never store or log it.</summary>
    public string Reveal() => _reveal();

    public override string ToString() => Placeholder;
}

/// <summary>Writes a secret as <c>[secret]</c> and refuses to read one, so no secret is ever stored as JSON.</summary>
public sealed class SecretValueJsonConverter : JsonConverter<SecretValue>
{
    public override SecretValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("A secret value is never read from JSON.");

    public override void Write(Utf8JsonWriter writer, SecretValue value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(SecretValue.Placeholder);
    }
}
