namespace Axis.Mcp.Tools;

/// <summary>
/// The explicit MCP-to-OpenAPI coverage contract. Every authenticated product
/// operation is either exposed as one semantic MCP tool, intentionally blocked
/// pending its owning product contract, or kept internal to browser/OAuth bootstrap.
/// </summary>
public static class AxisMcpOperationCatalog
{
    public static IReadOnlyDictionary<string, string> OperationToTool { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ListBusinessObjectDefinitions"] = "axis_list_business_object_definitions",
            ["CreateBusinessObjectDefinition"] = "axis_create_business_object_definition",
            ["GetBusinessObjectDefinition"] = "axis_get_business_object_definition",
            ["SaveUnpublishedBusinessObjectDefinition"] = "axis_save_unpublished_business_object_definition",
            ["PublishBusinessObjectDefinition"] = "axis_publish_business_object_definition",
            ["ListBusinessObjectRecords"] = "axis_list_business_object_records",
            ["CreateBusinessObjectRecord"] = "axis_create_business_object_record",
            ["GetBusinessObjectRecord"] = "axis_get_business_object_record",
            ["SaveBusinessObjectRecord"] = "axis_save_business_object_record",
            ["SubmitBusinessObjectRecord"] = "axis_submit_business_object_record",
            ["GetLegalVersions"] = "axis_get_legal_versions",
            ["GetMe"] = "axis_get_current_user",
            ["UpdateLanguagePreference"] = "axis_update_language_preference",
            ["UpdateThemePreference"] = "axis_update_theme_preference",
            ["CreateOrganizationWorkspace"] = "axis_create_organization_workspace",
            ["InviteWorkspaceMember"] = "axis_invite_workspace_member",
            ["ListWorkspaceInvitations"] = "axis_list_workspace_invitations",
            ["ResendWorkspaceInvitation"] = "axis_resend_workspace_invitation",
            ["RevokeWorkspaceInvitation"] = "axis_revoke_workspace_invitation",
            ["GetRuleBinding"] = "axis_get_rule_binding",
            ["ListRuleBindingUsage"] = "axis_list_rule_binding_usage",
            ["ListRuleDefinitions"] = "axis_list_rules",
            ["CreateRuleDefinition"] = "axis_create_rule_definition",
            ["GetRuleExpressionLanguage"] = "axis_get_rule_expression_language",
            ["ProjectRuleCondition"] = "axis_project_rule_condition",
            ["SearchRuleExpressionGuide"] = "axis_search_rule_expression_guide",
            ["GetRuleDefinition"] = "axis_get_rule",
            ["SaveRuleDefinitionDraft"] = "axis_save_rule_definition_draft",
            ["CreateRuleDefinitionVersion"] = "axis_create_rule_definition_version",
            ["ActivateRuleDefinitionVersion"] = "axis_activate_rule_definition_version",
            ["DeactivateRuleDefinition"] = "axis_deactivate_rule_definition",
            ["ArchiveRuleDefinition"] = "axis_archive_rule_definition",
            ["SimulateRuleDefinitionDraft"] = "axis_simulate_rule_definition_draft",
            ["SimulateRuleDefinitionVersion"] = "axis_simulate_rule_definition_version",
            ["ProjectRuleAuthoring"] = "axis_project_rule_authoring",
            ["CompleteRuleAuthoring"] = "axis_complete_rule_authoring",
            ["CreateRuleBinding"] = "axis_create_rule_binding",
            ["UpdateRuleBinding"] = "axis_update_rule_binding",
            ["DeleteRuleBinding"] = "axis_delete_rule_binding",
            ["EvaluateRuleBinding"] = "axis_evaluate_rule_binding",
        };

    public static IReadOnlySet<string> BlockedOperationIds { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
        };

    public static IReadOnlySet<string> ExcludedOperationIds { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "SignInUser",
            "VerifyEmail",
            "ResendEmailVerification",
            "SignOutUser",
            "RegisterUser",
            "GetBrowserSession",
        };
}
