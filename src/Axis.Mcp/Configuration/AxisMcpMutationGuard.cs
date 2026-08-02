namespace Axis.Mcp.Configuration;

public sealed class AxisMcpMutationGuard(AxisMcpOptions options)
{
    public void EnsureEnabled(string operationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);

        if (!options.MutationsEnabled)
        {
            throw new InvalidOperationException(
                $"Axis MCP mutation '{operationId}' is disabled. "
                + $"Start the server with --access write or set "
                + $"{AxisMcpOptions.AccessEnvironmentVariable}=write to enable it.");
        }
    }
}
