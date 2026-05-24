namespace Axis.WorkflowEngine.Contracts;

/// <summary>Kafka topic names for WorkflowEngine cross-module events (ADR-019, ADR-025).</summary>
public static class WorkflowEngineKafkaTopics
{
    public const string FormStepReached = "axis.workflowengine.form-step-reached";
}
