namespace Axis.Processes.Instances;

/// <summary>How the worker sends the emails that <c>notify</c> steps staged.</summary>
/// <param name="AllowPlainSmtp">
/// Whether it may send over SMTP with <c>UseTls</c> off. Only a worker that runs in Development allows
/// it. Elsewhere a tenant whose mail settings turn TLS off gets no email sent.
/// </param>
public sealed record EmailSecuritySettings(bool AllowPlainSmtp);
