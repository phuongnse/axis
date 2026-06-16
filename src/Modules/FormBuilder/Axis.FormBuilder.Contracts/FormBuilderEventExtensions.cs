using System.Text.Json;
using axis.formbuilder.events;

namespace Axis.FormBuilder.Contracts;

/// <summary>Typed accessors for Avro-generated event payloads (string UUID fields).</summary>
public static class FormBuilderEventExtensions
{
    private static readonly JsonSerializerOptions SubmittedDataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Deserialises the JSON-encoded SubmittedData blob back into a dictionary.
    /// Avro lacks a native "any" type so the publisher (FormBuilder) marshals
    /// the dictionary as JSON; consumers use this helper to round-trip.
    /// Must use the same JsonSerializerOptions as FormBuilderEventMapper.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> SubmittedData(this FormTaskSubmittedEvent @event)
    {
        if (string.IsNullOrWhiteSpace(@event.submittedDataJson))
            return new Dictionary<string, object?>();

        Dictionary<string, object?>? parsed = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            @event.submittedDataJson, SubmittedDataJsonOptions);
        return parsed ?? new Dictionary<string, object?>();
    }

    public static Guid FormSubmissionId(this FormTaskSubmittedEvent @event)
        => ParseRequiredGuid(@event.formSubmissionId, nameof(@event.formSubmissionId));

    public static Guid FormDefinitionId(this FormTaskSubmittedEvent @event)
        => ParseRequiredGuid(@event.formDefinitionId, nameof(@event.formDefinitionId));

    public static Guid TeamAccountId(this FormTaskSubmittedEvent @event)
        => ParseRequiredGuid(@event.teamAccountId, nameof(@event.teamAccountId));

    public static Guid ExecutionId(this FormTaskSubmittedEvent @event)
        => ParseRequiredGuid(@event.executionId, nameof(@event.executionId));

    public static Guid ExecutionStepId(this FormTaskSubmittedEvent @event)
        => ParseRequiredGuid(@event.executionStepId, nameof(@event.executionStepId));

    public static Guid FormSubmissionId(this FormTaskExpiredEvent @event)
        => ParseRequiredGuid(@event.formSubmissionId, nameof(@event.formSubmissionId));

    public static Guid FormDefinitionId(this FormTaskExpiredEvent @event)
        => ParseRequiredGuid(@event.formDefinitionId, nameof(@event.formDefinitionId));

    public static Guid TeamAccountId(this FormTaskExpiredEvent @event)
        => ParseRequiredGuid(@event.teamAccountId, nameof(@event.teamAccountId));

    public static Guid ExecutionId(this FormTaskExpiredEvent @event)
        => ParseRequiredGuid(@event.executionId, nameof(@event.executionId));

    public static Guid ExecutionStepId(this FormTaskExpiredEvent @event)
        => ParseRequiredGuid(@event.executionStepId, nameof(@event.executionStepId));

    public static Guid FormId(this FormDeletedEvent @event)
        => ParseRequiredGuid(@event.formId, nameof(@event.formId));

    public static Guid TeamAccountId(this FormDeletedEvent @event)
        => ParseRequiredGuid(@event.teamAccountId, nameof(@event.teamAccountId));

    private static Guid ParseRequiredGuid(string value, string fieldName)
        => Guid.TryParse(value, out Guid parsed)
            ? parsed
            : throw new FormatException($"Invalid GUID in field '{fieldName}': '{value}'.");
}
