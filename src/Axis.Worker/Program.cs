using Axis.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
using var host = builder.Build();
// Resolve the state before Run(), which disposes the host before it returns.
var startupCheck = host.Services.GetRequiredService<StartupCheckState>();
host.Run();
return startupCheck.Failed ? 1 : 0;
