namespace Axis.Scenarios;

/// <summary>A scenario step did not do what the scenario says. The message says why, without the step's number and type.</summary>
internal sealed class StepFailedException(string message) : Exception(message);
