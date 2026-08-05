using System.ComponentModel;
using System.Globalization;
using Axis.Mcp.Api;
using Axis.Mcp.Configuration;
using ModelContextProtocol.Server;

namespace Axis.Mcp.Tools;

[McpServerToolType]
public sealed class AxisMcpBusinessObjectRecordReadTools(AxisApiClient api)
{
    [McpServerTool(Name = "axis_list_business_object_records")]
    [Description("[READ] List persisted business-object records visible to the authenticated workspace.")]
    public Task<string> ListBusinessObjectRecordsAsync(
        [Description("One-based result page.")] int page = 1,
        [Description("Number of records to return, from 1 to 100.")] int pageSize = 20,
        [Description("Optional business-object key, such as business_record.")] string? objectKey = null,
        CancellationToken cancellationToken = default)
    {
        ValidatePage(page, pageSize);
        string path = "api/business-object-records" + AxisApiQuery.Build(
            ("page", page.ToString(CultureInfo.InvariantCulture)),
            ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            ("objectKey", objectKey));
        return api.GetJsonAsync(path, cancellationToken);
    }

    [McpServerTool(Name = "axis_get_business_object_record")]
    [Description("[READ] Get one persisted business-object record, its immutable field contract, and saved rule evidence.")]
    public Task<string> GetBusinessObjectRecordAsync(
        [Description("Business-object record UUID.")] Guid recordId,
        CancellationToken cancellationToken = default)
    {
        if (recordId == Guid.Empty)
            throw new ArgumentException("recordId must be a non-empty UUID.", nameof(recordId));
        return api.GetJsonAsync($"api/business-object-records/{recordId:D}", cancellationToken);
    }

    private static void ValidatePage(int page, int pageSize)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), "page must be greater than zero.");
        if (pageSize is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize must be between 1 and 100.");
    }
}

[McpServerToolType]
public sealed class AxisMcpBusinessObjectRecordWriteTools(
    AxisApiClient api,
    AxisMcpMutationGuard mutationGuard)
{
    [McpServerTool(Name = "axis_create_business_object_record")]
    [Description("[WRITE] Create a persisted Draft record from the latest published business-object version. The idempotency key is owned by the API contract.")]
    public Task<string> CreateBusinessObjectRecordAsync(
        [Description("Stable business-object key, such as business_record.")] string objectKey,
        CreateBusinessObjectRecordInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("CreateBusinessObjectRecord");
        return api.PostJsonAsync(
            $"api/business-object-records/{Uri.EscapeDataString(objectKey)}",
            input,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_save_business_object_record")]
    [Description("[WRITE] Save a Draft business-object record using its expected revision. Submitted records cannot be edited.")]
    public Task<string> SaveBusinessObjectRecordAsync(
        Guid recordId,
        SaveBusinessObjectRecordInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateRecordId(recordId);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Values);
        mutationGuard.EnsureEnabled("SaveBusinessObjectRecord");
        return api.PutJsonAsync(
            $"api/business-object-records/{recordId:D}",
            input,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_submit_business_object_record")]
    [Description("[WRITE] Evaluate the exact published field-rule revisions for a Draft record and submit it only when every rule matches. A non-match remains a recoverable Draft.")]
    public Task<string> SubmitBusinessObjectRecordAsync(
        Guid recordId,
        SubmitBusinessObjectRecordInput input,
        CancellationToken cancellationToken = default)
    {
        ValidateRecordId(recordId);
        ArgumentNullException.ThrowIfNull(input);
        mutationGuard.EnsureEnabled("SubmitBusinessObjectRecord");
        return api.PostJsonAsync(
            $"api/business-object-records/{recordId:D}/submit",
            input,
            cancellationToken);
    }

    private static void ValidateRecordId(Guid recordId)
    {
        if (recordId == Guid.Empty)
            throw new ArgumentException("recordId must be a non-empty UUID.", nameof(recordId));
    }
}
