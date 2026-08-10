using System.ComponentModel;
using Axis.Mcp.Api;
using Axis.Mcp.Configuration;
using ModelContextProtocol.Server;

namespace Axis.Mcp.Tools;

[McpServerToolType]
public sealed class AxisMcpSolutionReadTools(AxisApiClient api)
{
    [McpServerTool(Name = "axis_list_solution_versions")]
    [Description("[READ] List safe immutable published solution-version projections without package bytes.")]
    public Task<string> ListSolutionVersionsAsync(CancellationToken cancellationToken = default) =>
        api.GetJsonAsync("api/solutions/versions", cancellationToken);

    [McpServerTool(Name = "axis_get_solution_version_status")]
    [Description("[READ] Get one safe immutable published solution-version projection without package bytes.")]
    public Task<string> GetSolutionVersionStatusAsync(
        [Description("Published solution-version UUID returned by Axis.")] Guid solutionVersionId,
        CancellationToken cancellationToken = default) =>
        api.GetJsonAsync(
            $"api/solutions/versions/{RequireId(solutionVersionId, nameof(solutionVersionId)):D}",
            cancellationToken);

    [McpServerTool(Name = "axis_list_solution_installations")]
    [Description("[READ] List safe solution-installation status projections for the authenticated current Workspace.")]
    public Task<string> ListSolutionInstallationsAsync(CancellationToken cancellationToken = default) =>
        api.GetJsonAsync("api/solutions/installations", cancellationToken);

    [McpServerTool(Name = "axis_get_solution_installation_status")]
    [Description("[READ] Get the safe durable status and step outcomes for one current-Workspace solution installation operation.")]
    public Task<string> GetSolutionInstallationStatusAsync(
        [Description("Installation operation UUID returned by Axis.")] Guid operationId,
        CancellationToken cancellationToken = default) =>
        api.GetJsonAsync(
            $"api/solutions/operations/{RequireId(operationId, nameof(operationId)):D}",
            cancellationToken);

    private static Guid RequireId(Guid value, string parameterName) =>
        value != Guid.Empty
            ? value
            : throw new ArgumentException("A non-empty UUID is required.", parameterName);
}

[McpServerToolType]
public sealed class AxisMcpSolutionWriteTools(
    AxisApiClient api,
    AxisMcpMutationGuard mutationGuard)
{
    private const int MaximumPackageBytes = 10 * 1024 * 1024;

    [McpServerTool(Name = "axis_publish_solution_version")]
    [Description("[WRITE] Publish one signed solution package from an explicit regular local file of at most 10 MiB. Package bytes are uploaded only to Axis and are never returned in tool output.")]
    public async Task<string> PublishSolutionVersionAsync(
        PublishSolutionVersionInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.PackageFilePath);
        mutationGuard.EnsureEnabled("PublishSolutionVersion");

        byte[] package = await ReadRegularPackageFileAsync(
            input.PackageFilePath,
            cancellationToken);
        return await api.PostBinaryAsync(
            "api/solutions/versions",
            package,
            "application/vnd.dsse.envelope.v1+json",
            cancellationToken);
    }

    [McpServerTool(Name = "axis_install_solution_version")]
    [Description("[WRITE] Begin a durable installation of one exact trusted solution version in the authenticated current Workspace. The idempotency key is forwarded as an API header.")]
    public Task<string> InstallSolutionVersionAsync(
        Guid solutionVersionId,
        InstallSolutionVersionInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.IdempotencyKey);
        mutationGuard.EnsureEnabled("InstallSolutionVersion");
        return api.PostIdempotentAsync(
            $"api/solutions/versions/{RequireId(solutionVersionId, nameof(solutionVersionId)):D}/installations",
            input.IdempotencyKey,
            cancellationToken);
    }

    [McpServerTool(Name = "axis_resume_solution_installation")]
    [Description("[WRITE] Resume one recoverable failed solution installation operation after Axis revalidates current trust and state.")]
    public Task<string> ResumeSolutionInstallationAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        mutationGuard.EnsureEnabled("ResumeSolutionInstallation");
        return api.PostAsync(
            $"api/solutions/operations/{RequireId(operationId, nameof(operationId)):D}/resume",
            cancellationToken);
    }

    private static async Task<byte[]> ReadRegularPackageFileAsync(
        string packageFilePath,
        CancellationToken cancellationToken)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(packageFilePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException("packageFilePath must identify a valid local file.", nameof(packageFilePath));
        }

        FileInfo file = new(fullPath);
        file.Refresh();
        if (!file.Exists ||
            (file.Attributes & (FileAttributes.Directory | FileAttributes.Device | FileAttributes.ReparsePoint)) != 0)
        {
            throw new ArgumentException(
                "packageFilePath must resolve directly to a regular local file.",
                nameof(packageFilePath));
        }
        if (file.Length > MaximumPackageBytes)
        {
            throw new ArgumentException(
                "The solution package file must be at most 10 MiB.",
                nameof(packageFilePath));
        }

        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaximumPackageBytes)
        {
            throw new ArgumentException(
                "The solution package file must be at most 10 MiB.",
                nameof(packageFilePath));
        }

        byte[] package = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(package, cancellationToken);
        if (await stream.ReadAsync(new byte[1], cancellationToken) != 0)
        {
            throw new ArgumentException(
                "The solution package file must be at most 10 MiB.",
                nameof(packageFilePath));
        }
        return package;
    }

    private static Guid RequireId(Guid value, string parameterName) =>
        value != Guid.Empty
            ? value
            : throw new ArgumentException("A non-empty UUID is required.", parameterName);
}
