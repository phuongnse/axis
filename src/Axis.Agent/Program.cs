using Axis.Agent;
using Axis.Hosting;
using Microsoft.AspNetCore.Builder;

var builder = WebApplication.CreateSlimBuilder(args);
// A file in /run/secrets, named as a setting in its environment variable form, wins over the variable.
builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
builder.Services.AddAxisAgent(builder.Configuration);
using var app = builder.Build();
app.MapHostHealth();
app.Run();

// The tests also reference the server, whose entry point is public, so keep this one out of their reach.
internal partial class Program;
