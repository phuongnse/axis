using Axis.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddAxisWorker(builder.Configuration);
builder.Build().Run();
