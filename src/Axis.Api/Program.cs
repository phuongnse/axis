using Axis.Api.Extensions;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.AddAxisApiServices();

    WebApplication app = builder.Build();

    await app.RunAxisStartupTasksAsync();
    app.UseAxisApiPipeline();
    app.MapAxisApiEndpoints();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    Environment.ExitCode = 1;
    throw;
}
finally
{
    Log.CloseAndFlush();
}

// Needed for WebApplicationFactory in integration tests.
public partial class Program;
