using Axis.Configuration.Model;

namespace Axis.Server.Applications;

/// <summary>An active site and the model of the application release that holds it.</summary>
internal sealed record ActiveSite(ApplicationModel Application, SiteModel Site);
