namespace Axis.WorkflowBuilder.Contracts;

/// <summary>Kafka topic names for WorkflowBuilder cross-module events (ADR-019, ADR-025).</summary>
public static class WorkflowBuilderKafkaTopics
{
    public const string WorkflowPublished = "axis.workflowbuilder.workflow-published";
    public const string WorkflowArchived = "axis.workflowbuilder.workflow-archived";
    public const string WorkflowUnarchived = "axis.workflowbuilder.workflow-unarchived";
}
