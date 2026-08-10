using Axis.Mcp.Api;
using Axis.Mcp.Authentication;
using Axis.Mcp.Configuration;
using Axis.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
AxisMcpOptions options = AxisMcpOptions.FromEnvironment();

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<AxisMcpMutationGuard>();
builder.Services.AddSingleton<IBrowserLauncher, SystemBrowserLauncher>();
builder.Services.AddSingleton(_ => AxisMcpHttpClientFactory.Create(options));
builder.Services.AddSingleton<OAuthTokenProvider>();
builder.Services.AddSingleton<IAxisAccessTokenProvider>(services =>
    services.GetRequiredService<OAuthTokenProvider>());
builder.Services.AddSingleton<AxisApiClient>();
builder.Services.AddSingleton<AxisMcpConfirmationStore>();

IMcpServerBuilder mcpServer = builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<AxisMcpTools>()
    .WithTools<AxisMcpReferenceTools>()
    .WithTools<AxisMcpBindingReadTools>()
    .WithTools<AxisMcpBindingEvaluationTools>()
    .WithTools<AxisMcpRuleReadTools>()
    .WithTools<AxisMcpIdentityReadTools>()
    .WithTools<AxisMcpServiceIdentityReadTools>()
    .WithTools<AxisMcpAuthorizationReadTools>()
    .WithTools<AxisMcpSolutionReadTools>()
    .WithTools<AxisMcpBusinessObjectRecordReadTools>();

if (options.MutationsEnabled)
{
    mcpServer
        .WithTools<AxisMcpIdentityTools>()
        .WithTools<AxisMcpServiceIdentityWriteTools>()
        .WithTools<AxisMcpAuthorizationTools>()
        .WithTools<AxisMcpSolutionWriteTools>()
        .WithTools<AxisMcpRuleLifecycleTools>()
        .WithTools<AxisMcpBindingWriteTools>()
        .WithTools<AxisMcpBusinessObjectTools>()
        .WithTools<AxisMcpBusinessObjectRecordWriteTools>();
}

await builder.Build().RunAsync();
