using System.Text.Json;
using axis.formbuilder.events;
using Axis.FormBuilder.Domain.Events;
using Axis.Shared.Domain.Primitives;

namespace Axis.FormBuilder.Infrastructure.Messaging;

/// <summary>Maps FormBuilder domain events to Avro contract messages for Kafka (ADR-019).</summary>
internal static class FormBuilderEventMapper
{
    private static readonly JsonSerializerOptions SubmittedDataJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static object? ToIntegrationEvent(IDomainEvent domainEvent) =>
        domainEvent switch
        {
            FormTaskSubmitted submitted => new FormTaskSubmittedEvent
            {
                formSubmissionId = submitted.FormSubmissionId.ToString(),
                formDefinitionId = submitted.FormDefinitionId.ToString(),
                tenantId = submitted.tenantId.ToString(),
                executionId = submitted.ExecutionId.ToString(),
                executionStepId = submitted.ExecutionStepId.ToString(),
                // SubmittedData is an arbitrary key/value map; Avro lacks a native
                // "any" type so we marshal as JSON. Consumer deserialises with
                // matching JsonSerializerOptions via FormBuilderEventExtensions.
                submittedDataJson = JsonSerializer.Serialize(submitted.SubmittedData, SubmittedDataJsonOptions),
            },
            FormTaskExpired expired => new FormTaskExpiredEvent
            {
                formSubmissionId = expired.FormSubmissionId.ToString(),
                formDefinitionId = expired.FormDefinitionId.ToString(),
                tenantId = expired.tenantId.ToString(),
                executionId = expired.ExecutionId.ToString(),
                executionStepId = expired.ExecutionStepId.ToString(),
            },
            FormDeleted deleted => new FormDeletedEvent
            {
                formId = deleted.FormId.ToString(),
                tenantId = deleted.tenantId.ToString(),
            },
            _ => null,
        };
}
