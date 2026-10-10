using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>A compiled <c>startProcess</c> action of a form widget: the process it starts and its button label.</summary>
public sealed record StartProcessActionModel(ProcessReference Process, TextReference Label);
