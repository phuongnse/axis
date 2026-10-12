namespace Axis.Processes.Instances;

/// <summary>How the worker sends connector calls.</summary>
/// <param name="AllowHttp">
/// Whether a call may go to an <c>http</c> base URL. Only a worker that runs in Development allows
/// it. Elsewhere such a call is never sent and fails.
/// </param>
public sealed record ConnectorCallSettings(bool AllowHttp);
