using System.ComponentModel;
using System.Text.Json;
using Axis.Mcp.Api;
using Axis.Mcp.Configuration;
using ModelContextProtocol.Server;

namespace Axis.Mcp.Tools;

[McpServerToolType]
public sealed class AxisMcpBusinessObjectTools(
    AxisApiClient api,
    AxisMcpMutationGuard mutationGuard,
    AxisMcpConfirmationStore confirmationStore)
{
    [McpServerTool(Name = "axis_create_business_object_definition")]
    [Description("[WRITE] Create an unpublished business-object definition in the authenticated workspace.")]
    public Task<string> CreateBusinessObjectDefinitionAsync(
        CreateBusinessObjectDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("CreateBusinessObjectDefinition");
        return api.PostJsonAsync("api/business-object-definitions", input, cancellationToken);
    }

    [McpServerTool(Name = "axis_save_unpublished_business_object_definition")]
    [Description("[WRITE] Save an unpublished business-object definition using the caller's expected revision.")]
    public Task<string> SaveUnpublishedBusinessObjectDefinitionAsync(
        Guid id,
        SaveUnpublishedBusinessObjectDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("id must be a non-empty UUID.", nameof(id));
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Fields);
        mutationGuard.EnsureEnabled("SaveUnpublishedBusinessObjectDefinition");
        return api.PutJsonAsync(
            $"api/business-object-definitions/{id:D}/unpublished",
            input,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_prepare_publish_business_object_definition")]
    [Description("[WRITE] Capture the current unpublished business-object snapshot for an explicit publish confirmation. The returned token expires shortly and is single-use.")]
    public async Task<string> PreparePublishBusinessObjectDefinitionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("id must be a non-empty UUID.", nameof(id));
        mutationGuard.EnsureEnabled("PreparePublishBusinessObjectDefinition");

        string snapshotJson = await api.GetJsonAsync(
            $"api/business-object-definitions/{id:D}",
            cancellationToken);
        BusinessObjectSnapshot snapshot = ReadSnapshot(snapshotJson, id);
        EnsureUnpublished(snapshot);

        string subject = await GetCurrentSubjectAsync(cancellationToken);
        BusinessObjectPublishConfirmation confirmation = confirmationStore.Create(
            id,
            snapshot.Revision,
            subject,
            AxisMcpConfirmationStore.ComputeSnapshotHash(snapshotJson));

        return JsonSerializer.Serialize(new
        {
            confirmationToken = confirmation.Token,
            businessObjectDefinitionId = confirmation.BusinessObjectDefinitionId,
            expectedRevision = confirmation.ExpectedRevision,
            expiresAt = confirmation.ExpiresAt,
        });
    }

    [McpServerTool(Name = "axis_publish_business_object_definition")]
    [Description("[WRITE] Publish an unpublished business-object definition only with the exact single-use token returned by the prepare tool.")]
    public async Task<string> PublishBusinessObjectDefinitionAsync(
        Guid id,
        ExpectedRevisionInput input,
        string confirmationToken,
        CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("id must be a non-empty UUID.", nameof(id));
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmationToken);
        mutationGuard.EnsureEnabled("PublishBusinessObjectDefinition");

        string subject = await GetCurrentSubjectAsync(cancellationToken);
        string snapshotJson = await api.GetJsonAsync(
            $"api/business-object-definitions/{id:D}",
            cancellationToken);
        BusinessObjectSnapshot snapshot = ReadSnapshot(snapshotJson, id);
        EnsureUnpublished(snapshot);

        bool consumed = confirmationStore.TryConsume(
            confirmationToken,
            id,
            input.ExpectedRevision,
            subject,
            AxisMcpConfirmationStore.ComputeSnapshotHash(snapshotJson));
        if (!consumed)
        {
            throw new InvalidOperationException(
                "The business-object publish confirmation is invalid, expired, already used, or no longer matches the current snapshot.");
        }

        return await api.PostJsonAsync(
            $"api/business-object-definitions/{id:D}/publish",
            input,
            cancellationToken);
    }

    private async Task<string> GetCurrentSubjectAsync(CancellationToken cancellationToken)
    {
        string currentUserJson = await api.GetJsonAsync("api/users/me", cancellationToken);
        using JsonDocument document = JsonDocument.Parse(currentUserJson);
        JsonElement root = document.RootElement;
        string userId = GetRequiredString(root, "id");
        string workspaceId = GetOptionalString(root, "workspaceId") ?? "none";
        return $"{userId}:{workspaceId}";
    }

    private static BusinessObjectSnapshot ReadSnapshot(string json, Guid expectedId)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        string idText = GetRequiredString(root, "id");
        if (!Guid.TryParse(idText, out Guid id) || id != expectedId)
            throw new InvalidOperationException("Axis returned a business-object snapshot for an unexpected definition.");

        string status = GetRequiredString(root, "status");
        if (!root.TryGetProperty("revision", out JsonElement revisionElement) ||
            !revisionElement.TryGetInt32(out int revision))
            throw new InvalidOperationException("Axis returned a business-object snapshot without a valid revision.");

        return new BusinessObjectSnapshot(status, revision);
    }

    private static void EnsureUnpublished(BusinessObjectSnapshot snapshot)
    {
        if (!string.Equals(snapshot.Status, "Unpublished", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Only an unpublished business-object definition can enter the publish confirmation flow.");
        }
    }

    private static string GetRequiredString(JsonElement root, string name) =>
        GetOptionalString(root, name)
        ?? throw new InvalidOperationException($"Axis returned a snapshot without '{name}'.");

    private static string? GetOptionalString(JsonElement root, string name)
    {
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
                return property.Value.GetString();
        }

        return null;
    }

    private sealed record BusinessObjectSnapshot(string Status, int Revision);
}
