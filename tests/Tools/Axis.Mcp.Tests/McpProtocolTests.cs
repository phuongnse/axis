using System.Diagnostics;
using System.Text.Json;

namespace Axis.Mcp.Tests;

public sealed class McpProtocolTests
{
    [Fact]
    public Task StdioServer_ReadAccess_ExposesReadTools() =>
        AssertToolNamesAsync(
            "read",
            [
                "axis_get_business_object_definition",
                "axis_get_business_object_definition_collection_actions",
                "axis_get_rule_binding",
                "axis_get_current_user",
                "axis_get_legal_versions",
                "axis_get_rule",
                "axis_get_rule_definition_collection_actions",
                "axis_get_rule_expression_language",
                "axis_get_service_identity",
                "axis_get_solution_installation_status",
                "axis_get_solution_version_status",
                "axis_get_business_object_record",
                "axis_list_business_object_definitions",
                "axis_list_business_object_records",
                "axis_list_product_role_assignments",
                "axis_list_rule_binding_usage",
                "axis_list_rules",
                "axis_list_service_identities",
                "axis_list_solution_installations",
                "axis_list_solution_versions",
                "axis_list_workspace_invitations",
                "axis_evaluate_rule_binding",
                "axis_complete_rule_authoring",
                "axis_project_rule_authoring",
                "axis_project_rule_condition",
                "axis_search_rule_expression_guide",
                "axis_simulate_rule_definition_draft",
                "axis_simulate_rule_definition_version",
            ]);

    [Fact]
    public Task StdioServer_WriteAccess_ExposesApprovedTools() =>
        AssertToolNamesAsync(
            "write",
            [
                "axis_create_business_object_definition",
                "axis_create_business_object_record",
                "axis_create_organization_workspace",
                "axis_create_rule_binding",
                "axis_create_rule_definition",
                "axis_create_rule_definition_version",
                "axis_create_service_identity",
                "axis_delete_rule_binding",
                "axis_get_business_object_definition",
                "axis_get_business_object_definition_collection_actions",
                "axis_get_business_object_record",
                "axis_get_rule_binding",
                "axis_get_current_user",
                "axis_get_legal_versions",
                "axis_get_rule",
                "axis_get_rule_definition_collection_actions",
                "axis_get_rule_expression_language",
                "axis_get_service_identity",
                "axis_get_solution_installation_status",
                "axis_get_solution_version_status",
                "axis_invite_workspace_member",
                "axis_install_solution_version",
                "axis_list_business_object_definitions",
                "axis_list_business_object_records",
                "axis_list_product_role_assignments",
                "axis_list_rule_binding_usage",
                "axis_list_rules",
                "axis_list_service_identities",
                "axis_list_solution_installations",
                "axis_list_solution_versions",
                "axis_list_workspace_invitations",
                "axis_evaluate_rule_binding",
                "axis_complete_rule_authoring",
                "axis_prepare_publish_business_object_definition",
                "axis_project_rule_authoring",
                "axis_project_rule_condition",
                "axis_publish_business_object_definition",
                "axis_publish_solution_version",
                "axis_resend_workspace_invitation",
                "axis_revoke_workspace_invitation",
                "axis_revoke_product_role",
                "axis_revoke_service_identity",
                "axis_revoke_service_identity_key",
                "axis_resume_solution_installation",
                "axis_assign_product_role",
                "axis_add_service_identity_key",
                "axis_activate_rule_definition_version",
                "axis_archive_rule_definition",
                "axis_deactivate_rule_definition",
                "axis_save_rule_definition_draft",
                "axis_save_unpublished_business_object_definition",
                "axis_save_business_object_record",
                "axis_search_rule_expression_guide",
                "axis_simulate_rule_definition_draft",
                "axis_simulate_rule_definition_version",
                "axis_update_language_preference",
                "axis_update_rule_binding",
                "axis_update_theme_preference",
                "axis_submit_business_object_record",
            ]);

    private static async Task AssertToolNamesAsync(
        string accessMode,
        IReadOnlyList<string> expectedToolNames)
    {
        string executableName = OperatingSystem.IsWindows() ? "Axis.Mcp.exe" : "Axis.Mcp";
        string executablePath = Path.Combine(AppContext.BaseDirectory, executableName);
        Assert.True(File.Exists(executablePath), $"Expected MCP executable at {executablePath}.");

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        process.StartInfo.Environment["AXIS_MCP_ACCESS"] = accessMode;

        Assert.True(process.Start());
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        try
        {
            await process.StandardInput.WriteLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\",\"capabilities\":{},\"clientInfo\":{\"name\":\"axis-test\",\"version\":\"1\"}}}");
            await process.StandardInput.FlushAsync(cancellationToken);

            using JsonDocument initialize = await ReadJsonLineAsync(process, cancellationToken);

            await process.StandardInput.WriteLineAsync(
                "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\",\"params\":{}}");
            await process.StandardInput.FlushAsync(cancellationToken);
            using JsonDocument tools = await ReadJsonLineAsync(process, cancellationToken);

            Assert.Equal(1, initialize.RootElement.GetProperty("id").GetInt32());
            Assert.Equal(
                "2025-06-18",
                initialize.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());

            string[] toolNames = tools.RootElement
                .GetProperty("result")
                .GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString()!)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(
                expectedToolNames.OrderBy(name => name, StringComparer.Ordinal),
                toolNames);
        }
        finally
        {
            process.StandardInput.Close();
            if (!process.HasExited)
                await process.WaitForExitAsync(cancellationToken);
        }
    }

    private static async Task<JsonDocument> ReadJsonLineAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        string? line = await process.StandardOutput.ReadLineAsync(cancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(line), "MCP stdout ended before a JSON-RPC response.");
        return JsonDocument.Parse(line!);
    }
}
