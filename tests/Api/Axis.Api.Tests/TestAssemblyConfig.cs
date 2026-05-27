// Both test collections ("Api" and "Api-E2E") each create a WebApplicationFactory<Program>,
// which runs Program.cs in-process via HostFactoryResolver. Program.cs mutates process-wide
// static state: Serilog's Log.Logger (ReloadableLogger) and environment variables.
// Running two factories concurrently causes races:
//   • ReloadableLogger.Freeze() throws "already frozen" if both factories resolve ILoggerFactory
//     at the same time — the second freeze hits a logger the first already froze.
//   • HostFactoryResolver subscribes to DiagnosticListener.AllListeners (process-wide), so
//     two simultaneous "HostBuilt" events can be captured by the wrong factory's listener.
//
// DisableTestParallelization = true sequences the collections so only one WebApplicationFactory
// host-build runs at a time. Tests within each collection still run in parallel as before.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
