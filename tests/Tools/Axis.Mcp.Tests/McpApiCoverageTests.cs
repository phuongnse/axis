using System.Text.Json;
using Axis.Mcp.Tools;

namespace Axis.Mcp.Tests;

public sealed class McpApiCoverageTests
{
    [Fact]
    public void OpenApi_WhenLoaded_MatchesTheMcpCoverageClassification()
    {
        string openApiPath = Path.Combine(FindRepoRoot(), "openapi.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(openApiPath));

        HashSet<string> operationIds = new(StringComparer.Ordinal);
        foreach (JsonProperty path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (JsonProperty method in path.Value.EnumerateObject())
            {
                if (method.Value.TryGetProperty("operationId", out JsonElement operationId))
                    operationIds.Add(operationId.GetString()!);
            }
        }

        HashSet<string> classified = new(
            AxisMcpOperationCatalog.OperationToTool.Keys
                .Concat(AxisMcpOperationCatalog.BlockedOperationIds)
                .Concat(AxisMcpOperationCatalog.ExcludedOperationIds),
            StringComparer.Ordinal);

        Assert.Equal(
            operationIds.OrderBy(value => value, StringComparer.Ordinal),
            classified.OrderBy(value => value, StringComparer.Ordinal));
        Assert.Equal(
            AxisMcpOperationCatalog.OperationToTool.Count,
            AxisMcpOperationCatalog.OperationToTool.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.Empty(
            AxisMcpOperationCatalog.OperationToTool.Keys.Intersect(
                AxisMcpOperationCatalog.BlockedOperationIds,
                StringComparer.Ordinal));
    }

    [Fact]
    public void RuleOperationCatalog_CurrentCutover_UsesExpectedOperationIds()
    {
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["CreateRuleDefinitionVersion"] = "axis_create_rule_definition_version",
                ["ActivateRuleDefinitionVersion"] = "axis_activate_rule_definition_version",
                ["DeactivateRuleDefinition"] = "axis_deactivate_rule_definition",
                ["ArchiveRuleDefinition"] = "axis_archive_rule_definition",
                ["SimulateRuleDefinitionDraft"] = "axis_simulate_rule_definition_draft",
                ["SimulateRuleDefinitionVersion"] = "axis_simulate_rule_definition_version",
                ["ProjectRuleAuthoring"] = "axis_project_rule_authoring",
                ["CompleteRuleAuthoring"] = "axis_complete_rule_authoring",
                ["EvaluateRuleBinding"] = "axis_evaluate_rule_binding",
            },
            AxisMcpOperationCatalog.OperationToTool
                .Where(pair => pair.Key is "CreateRuleDefinitionVersion"
                    or "ActivateRuleDefinitionVersion"
                    or "DeactivateRuleDefinition"
                    or "ArchiveRuleDefinition"
                    or "SimulateRuleDefinitionDraft"
                    or "SimulateRuleDefinitionVersion"
                    or "ProjectRuleAuthoring"
                    or "CompleteRuleAuthoring"
                    or "EvaluateRuleBinding")
                .ToDictionary());
    }

    [Fact]
    public void OperationCatalog_WhenMapped_UsesTypedSemanticToolsOnly()
    {
        Dictionary<string, string> expected = new(StringComparer.Ordinal)
        {
            ["GetBusinessObjectDefinitionCollectionActions"] = "axis_get_business_object_definition_collection_actions",
            ["GetRuleDefinitionCollectionActions"] = "axis_get_rule_definition_collection_actions",
            ["CreateServiceIdentity"] = "axis_create_service_identity",
            ["ListServiceIdentities"] = "axis_list_service_identities",
            ["GetServiceIdentity"] = "axis_get_service_identity",
            ["AddServiceIdentityKey"] = "axis_add_service_identity_key",
            ["RevokeServiceIdentityKey"] = "axis_revoke_service_identity_key",
            ["RevokeServiceIdentity"] = "axis_revoke_service_identity",
            ["ListProductRoleAssignments"] = "axis_list_product_role_assignments",
            ["AssignProductRole"] = "axis_assign_product_role",
            ["RevokeProductRole"] = "axis_revoke_product_role",
            ["ListWorkspaceProductBuilders"] = "axis_list_workspace_product_builders",
            ["GrantWorkspaceProductBuilder"] = "axis_grant_workspace_product_builder",
            ["RevokeWorkspaceProductBuilder"] = "axis_revoke_workspace_product_builder",
            ["PublishSolutionVersion"] = "axis_publish_solution_version",
            ["ListSolutionVersions"] = "axis_list_solution_versions",
            ["GetSolutionVersionStatus"] = "axis_get_solution_version_status",
            ["InstallSolutionVersion"] = "axis_install_solution_version",
            ["ListSolutionInstallations"] = "axis_list_solution_installations",
            ["GetSolutionOperationStatus"] = "axis_get_solution_installation_status",
            ["ResumeSolutionInstallation"] = "axis_resume_solution_installation",
        };

        Assert.Equal(
            expected,
            AxisMcpOperationCatalog.OperationToTool
                .Where(pair => expected.ContainsKey(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        Assert.DoesNotContain(
            AxisMcpOperationCatalog.OperationToTool.Values,
            tool => tool.Contains("proxy", StringComparison.OrdinalIgnoreCase)
                || tool.Contains("raw_package", StringComparison.OrdinalIgnoreCase)
                || tool.Contains("trusted_publisher", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "openapi.json")))
                return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Axis repository root.");
    }
}
