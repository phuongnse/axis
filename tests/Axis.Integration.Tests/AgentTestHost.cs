using Axis.Agent;
using Axis.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Axis.Integration.Tests;

/// <summary>An agent host built as <c>Axis.Agent</c> registers it, with its log lines collected.</summary>
internal static class AgentTestHost
{
    /// <summary>
    /// Builds the host without starting it. With <paramref name="aiOn"/>, it adds a binding <c>main</c> of kind <c>anthropic</c> and makes it the default,
    /// unless <paramref name="settings"/> sets them. <paramref name="services"/> runs after <c>AddAxisAgent</c>.
    /// </summary>
    public static WebApplication Build(bool aiOn, Dictionary<string, string?> settings, LogCollector logs, Action<IServiceCollection>? services = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logs);

        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        settings["Health:Urls"] = "http://127.0.0.1:0";
        if (aiOn)
        {
            settings.TryAdd("Ai:Bindings:main:Kind", "anthropic");
            settings.TryAdd("Ai:Bindings:main:Model", "model-1");
            settings.TryAdd("Ai:DefaultBinding", "main");
        }

        builder.Configuration.AddInMemoryCollection(settings);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.Services.AddAxisAgent(builder.Configuration, builder.Environment);
        services?.Invoke(builder.Services);
        var app = builder.Build();
        app.MapHostHealth();
        return app;
    }
}
