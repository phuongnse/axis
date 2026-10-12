using System.Text.Json;
using System.Text.Json.Serialization;

namespace Axis.Tenancy;

/// <summary>
/// The value of a <c>secret</c> deployment binding. Its text and JSON forms are <c>[secret]</c>, so logging or
/// serializing it never shows the value. Read the value with <see cref="Reveal"/> only at the moment of a call.
/// </summary>
[JsonConverter(typeof(SecretValueJsonConverter))]
public sealed class SecretValue(string value)
{
    /// <summary>The text that stands for the value wherever a secret is shown.</summary>
    public const string Placeholder = "[secret]";

    private readonly string _value = value ?? throw new ArgumentNullException(nameof(value));

    /// <summary>Returns the plain value. Pass it straight to the call that needs it, and never store or log it.</summary>
    public string Reveal() => _value;

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
