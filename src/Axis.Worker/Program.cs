using Axis.Worker;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateSlimBuilder(args);
builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
using var app = builder.Build();
app.MapWorkerHealth();
// Resolve the state before Run(), which disposes the host before it returns.
var startupCheck = app.Services.GetRequiredService<StartupCheckState>();
app.Run();
return startupCheck.Failed ? 1 : 0;

// The tests also reference the server, whose entry point is public, so keep this one out of their reach.
internal partial class Program;
