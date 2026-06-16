namespace Axis.Identity.Contracts;

/// <summary>Cross-module tenant provisioning identifiers and retry payload.</summary>
public static class TenantModuleNames
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

/// <summary>Scheduled retry for a single module's tenant schema provisioning (RabbitMQ / Wolverine).</summary>
public sealed record RetryTenantModuleProvisionMessage(Guid TeamAccountId, string Module, int Attempt);
