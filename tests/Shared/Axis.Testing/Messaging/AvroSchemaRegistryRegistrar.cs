using System.Net.Http.Json;
using System.Text.Json;

namespace Axis.Testing.Messaging;

/// <summary>
/// Registers module Avro schemas with Confluent Schema Registry (mirrors scripts/register-avro-schemas.sh).
/// </summary>
public static class AvroSchemaRegistryRegistrar
{
    private static readonly (string Subject, string RelativePath)[] EventSchemas =
    [
        ("axis.workflowbuilder.workflow-published-value",
            "src/Modules/WorkflowBuilder/Axis.WorkflowBuilder.Contracts/Schemas/WorkflowPublishedEvent.avsc"),
        ("axis.workflowbuilder.workflow-archived-value",
            "src/Modules/WorkflowBuilder/Axis.WorkflowBuilder.Contracts/Schemas/WorkflowArchivedEvent.avsc"),
        ("axis.workflowbuilder.workflow-unarchived-value",
            "src/Modules/WorkflowBuilder/Axis.WorkflowBuilder.Contracts/Schemas/WorkflowUnarchivedEvent.avsc"),
        ("axis.datamodeling.model-created-value",
            "src/Modules/DataModeling/Axis.DataModeling.Contracts/Schemas/ModelCreatedEvent.avsc"),
        ("axis.datamodeling.model-deleted-value",
            "src/Modules/DataModeling/Axis.DataModeling.Contracts/Schemas/ModelDeletedEvent.avsc"),
        ("axis.datamodeling.data-class-created-value",
            "src/Modules/DataModeling/Axis.DataModeling.Contracts/Schemas/DataClassCreatedEvent.avsc"),
        ("axis.datamodeling.data-class-deleted-value",
            "src/Modules/DataModeling/Axis.DataModeling.Contracts/Schemas/DataClassDeletedEvent.avsc"),
        ("axis.datamodeling.data-record-created-value",
            "src/Modules/DataModeling/Axis.DataModeling.Contracts/Schemas/DataRecordCreatedEvent.avsc"),
        ("axis.datamodeling.data-record-deleted-value",
            "src/Modules/DataModeling/Axis.DataModeling.Contracts/Schemas/DataRecordDeletedEvent.avsc"),
        ("axis.datamodeling.field-added-value",
            "src/Modules/DataModeling/Axis.DataModeling.Contracts/Schemas/FieldAddedEvent.avsc"),
        ("axis.datamodeling.field-updated-value",
            "src/Modules/DataModeling/Axis.DataModeling.Contracts/Schemas/FieldUpdatedEvent.avsc"),
        ("axis.datamodeling.field-removed-value",
            "src/Modules/DataModeling/Axis.DataModeling.Contracts/Schemas/FieldRemovedEvent.avsc"),
        ("axis.identity.organization-verified-value",
            "src/Modules/Identity/Axis.Identity.Contracts/Schemas/OrganizationVerifiedEvent.avsc"),
        ("axis.identity.tenant-module-provision-report-value",
            "src/Modules/Identity/Axis.Identity.Contracts/Schemas/TenantModuleProvisionReportEvent.avsc"),
        ("axis.identity.user-deactivated-value",
            "src/Modules/Identity/Axis.Identity.Contracts/Schemas/UserDeactivatedEvent.avsc"),
        ("axis.identity.user-reactivated-value",
            "src/Modules/Identity/Axis.Identity.Contracts/Schemas/UserReactivatedEvent.avsc"),
        ("axis.identity.role-assigned-value",
            "src/Modules/Identity/Axis.Identity.Contracts/Schemas/RoleAssignedEvent.avsc"),
        ("axis.identity.role-removed-value",
            "src/Modules/Identity/Axis.Identity.Contracts/Schemas/RoleRemovedEvent.avsc"),
        ("axis.formbuilder.form-deleted-value",
            "src/Modules/FormBuilder/Axis.FormBuilder.Contracts/Schemas/FormDeletedEvent.avsc"),
        ("axis.formbuilder.form-task-submitted-value",
            "src/Modules/FormBuilder/Axis.FormBuilder.Contracts/Schemas/FormTaskSubmittedEvent.avsc"),
        ("axis.formbuilder.form-task-expired-value",
            "src/Modules/FormBuilder/Axis.FormBuilder.Contracts/Schemas/FormTaskExpiredEvent.avsc"),
        ("axis.workflowengine.form-step-reached-value",
            "src/Modules/WorkflowEngine/Axis.WorkflowEngine.Contracts/Schemas/FormStepReachedEvent.avsc"),
    ];

    public static async Task RegisterModuleEventSchemasAsync(
        string schemaRegistryUrl,
        CancellationToken cancellationToken = default)
    {
        string repoRoot = FindRepositoryRoot();
        using HttpClient http = new();

        foreach ((string subject, string relativePath) in EventSchemas)
        {
            string filePath = Path.Combine(repoRoot, relativePath);
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Avro schema file not found for subject '{subject}'.", filePath);

            string schemaJson = await File.ReadAllTextAsync(filePath, cancellationToken);
            using JsonDocument doc = JsonDocument.Parse(schemaJson);
            string compactSchema = JsonSerializer.Serialize(doc.RootElement);

            HttpResponseMessage response = await http.PostAsJsonAsync(
                $"{schemaRegistryUrl.TrimEnd('/')}/subjects/{subject}/versions",
                new { schema = compactSchema },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new InvalidOperationException(
                    $"Failed to register Avro schema '{subject}' at {schemaRegistryUrl}: {(int)response.StatusCode} {body}");
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? dir = new(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Axis.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root (Axis.sln).");
    }
}
