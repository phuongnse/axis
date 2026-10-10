using Axis.Configuration.Model;

namespace Axis.Server.Applications;

/// <summary>The model of an application's active release, with that release's id.</summary>
internal sealed record ActiveApplication(Guid ReleaseId, ApplicationModel Model);
