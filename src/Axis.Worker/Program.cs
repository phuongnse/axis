using Axis.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddAxisWorker(builder.Configuration, builder.Environment);
using var host = builder.Build();
host.Run();
return host.Services.GetRequiredService<StartupCheckState>().Failed ? 1 : 0;
