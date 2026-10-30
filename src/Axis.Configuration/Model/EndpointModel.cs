namespace Axis.Configuration.Model;

/// <summary>
/// A compiled endpoint: an inbound call that creates a record of a root entity from typed input
/// fields and starts a process on it.
/// </summary>
public sealed record EndpointModel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The endpoint's file, relative to the application folder with <c>/</c> separators.</summary>
    public required string File { get; init; }

    /// <summary>The root entity of the record the call creates.</summary>
    public required EntityReference Entity { get; init; }

    /// <summary>
    /// The process the call starts on the new record, or null when the endpoint starts none. Its
    /// subject entity is <see cref="Entity"/>.
    /// </summary>
    public ProcessReference? Process { get; init; }

    /// <summary>The event the call is to raise about the new record, or null when the endpoint names none.</summary>
    public EndpointEventModel? Event { get; init; }

    /// <summary>The input fields of the call's body, in declaration order.</summary>
    public required IReadOnlyList<EndpointInputModel> Input { get; init; }

    /// <summary>The mapped fields, in the entity's declaration order, each named as the entity declares it.</summary>
    public required IReadOnlyList<EndpointValueModel> Values { get; init; }

    /// <summary>The field names a completed call returns, as the entity declares them, or null when the call does not wait.</summary>
    public IReadOnlyList<string>? Output { get; init; }

    /// <summary>How long the call waits for the instance to complete, or null when it does not wait.</summary>
    public TimeSpan? Wait { get; init; }
}

/// <summary>
/// The event a compiled endpoint names: its declared name, and the payload the call gives, one
/// expression over the input fields for each payload field, in the event's declaration order.
/// </summary>
public sealed record EndpointEventModel(string Name, IReadOnlyList<EventPayloadModel> Fields);

/// <summary>
/// An input field of a compiled endpoint, with its field type and the constraints of that type. A
/// reference or child-collection input has its <see cref="Target"/> resolved.
/// </summary>
public sealed record EndpointInputModel(
    string Name,
    FieldType Type,
    bool Required,
    int? MaxLength,
    int? Precision,
    int? Scale,
    IReadOnlyList<string>? Values,
    EntityReference? Target);

/// <summary>
/// A mapped field of a compiled endpoint. A field that is not a child collection gets the value of
/// <see cref="Expression"/>, which sees only the input fields. A child collection takes the rows of
/// the input named <see cref="CollectionInput"/>, as the endpoint declares it.
/// </summary>
public sealed record EndpointValueModel(string Field, ExpressionModel? Expression, string? CollectionInput);
