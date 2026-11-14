using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Axis.Configuration.Packages;

/// <summary>
/// A message of the runner protocol, one line of JSON that a <c>type</c> property names, see
/// docs/reference/extension-packages.md#the-runner-protocol. The exact shapes are in
/// <c>sdk/package-runner-protocol.schema.json</c>. No message carries an exception message.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(StartMessage), "start")]
[JsonDerivedType(typeof(ReadyMessage), "ready")]
[JsonDerivedType(typeof(RunMessage), "run")]
[JsonDerivedType(typeof(ResultMessage), "result")]
[JsonDerivedType(typeof(RetryableMessage), "retryable")]
[JsonDerivedType(typeof(FailureMessage), "failure")]
[JsonDerivedType(typeof(StopMessage), "stop")]
[JsonDerivedType(typeof(SandboxMessage), "sandbox")]
public abstract record RunnerMessage;

/// <summary>
/// The worker asks the host to start a runner for one installed package version. <paramref name="Proxy"/> is the
/// <c>host:port</c> of the worker's egress proxy, and <paramref name="ProxyToken"/> is the runner's credential for it.
/// </summary>
public sealed record StartMessage(string Package, string Version, string Proxy, string ProxyToken) : RunnerMessage;

/// <summary>The runner has loaded the version and takes a <c>run</c>.</summary>
public sealed record ReadyMessage : RunnerMessage;

/// <summary>The worker asks the runner for one attempt. <paramref name="Input"/> holds values as <see cref="RunnerValues"/> writes them.</summary>
public sealed record RunMessage(string Operation, Guid OperationId, JsonObject Input, RunBindings Bindings) : RunnerMessage;

/// <summary>The binding values of one attempt, each mapping a binding name to its value.</summary>
public sealed record RunBindings(Dictionary<string, string> Urls, Dictionary<string, string> Secrets);

/// <summary>The attempt finished. <paramref name="Output"/> holds values as <see cref="RunnerValues"/> writes them.</summary>
public sealed record ResultMessage(JsonObject Output) : RunnerMessage;

/// <summary>The attempt threw a <c>RetryableException</c>. <paramref name="Error"/> is the full name of its type.</summary>
public sealed record RetryableMessage(string Error) : RunnerMessage;

/// <summary>
/// The attempt failed. It holds the full name of the error's type in <paramref name="Error"/>, or in
/// <paramref name="Binding"/> the undeclared binding that the operation asked for, and never both.
/// </summary>
public sealed record FailureMessage(string? Error = null, string? Binding = null) : RunnerMessage;

/// <summary>The worker is done with the runner.</summary>
public sealed record StopMessage : RunnerMessage;

/// <summary>
/// The worker asks the package host whether it runs under gVisor, with no <paramref name="Gvisor"/>. The host answers
/// with <paramref name="Gvisor"/> set, closes the connection and starts no runner.
/// </summary>
public sealed record SandboxMessage(bool? Gvisor = null) : RunnerMessage;

/// <summary>Writes and reads the lines of the runner protocol.</summary>
public static class RunnerProtocol
{
    /// <summary>The TCP port that the package host listens on.</summary>
    public const int Port = 5310;

    /// <summary>The default TCP port of the worker's egress proxy.</summary>
    public const int ProxyPort = 5311;

    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web)
    {
        AllowOutOfOrderMetadataProperties = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The message as one line of JSON, with no line break in it.</summary>
    public static string Write(RunnerMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        return JsonSerializer.Serialize(message, _options);
    }

    /// <summary>Reads one line.</summary>
    /// <exception cref="JsonException">The line is not a message of the protocol.</exception>
    public static RunnerMessage Read(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        try
        {
            return JsonSerializer.Deserialize<RunnerMessage>(line, _options) ?? throw new JsonException("The line is not a message.");
        }
        catch (NotSupportedException exception)
        {
            // An unknown type discriminator is reported as an unsupported type.
            throw new JsonException("The line is not a message of the protocol.", exception);
        }
    }
}
