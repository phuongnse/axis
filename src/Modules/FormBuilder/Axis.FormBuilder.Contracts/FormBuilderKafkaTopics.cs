namespace Axis.FormBuilder.Contracts;

/// <summary>Kafka topic names for FormBuilder cross-module events (ADR-019, ADR-025).</summary>
public static class FormBuilderKafkaTopics
{
    public const string FormTaskSubmitted = "axis.formbuilder.form-task-submitted";
    public const string FormTaskExpired = "axis.formbuilder.form-task-expired";
    public const string FormDeleted = "axis.formbuilder.form-deleted";
}
