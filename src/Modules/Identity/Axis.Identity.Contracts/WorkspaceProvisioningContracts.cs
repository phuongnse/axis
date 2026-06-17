namespace Axis.Identity.Contracts;

/// <summary>Cross-module workspace provisioning identifiers and retry payload.</summary>
public static class WorkspaceModuleNames
{
    public const string DataModeling = "datamodeling";
    public const string FormBuilder = "formbuilder";
    public const string WorkflowBuilder = "workflowbuilder";
    public const string WorkflowEngine = "workflowengine";

    public static IReadOnlyList<string> All { get; } =
    [
        DataModeling,
        FormBuilder,
        WorkflowBuilder,
        WorkflowEngine,
    ];
}

/// <summary>Scheduled retry for a single module's workspace schema provisioning (RabbitMQ / Wolverine).</summary>
public sealed record RetryWorkspaceModuleProvisionMessage(Guid workspaceId, string Module, int Attempt);
