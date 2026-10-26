namespace Axis.Configuration.Resources;

/// <summary>
/// The message of a <c>notify</c> step as written: the text keys of its subject and body, and the
/// expression that gives the value of each placeholder, in file order. Whether the placeholders and
/// the values match is checked by <see cref="Compilation.ApplicationCompiler"/>.
/// </summary>
public sealed record NotifyMessageDefinition
{
    public required TextReference Subject { get; init; }

    public required TextReference Body { get; init; }

    public IReadOnlyDictionary<string, string>? Values { get; init; }
}
