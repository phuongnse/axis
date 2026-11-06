namespace Axis.ControlPlane.Jobs;

/// <summary>A kind of job that <see cref="JobSchedule"/> queues once for each <see cref="Interval"/>.</summary>
internal sealed record RecurringJob(string Kind, TimeSpan Interval);
