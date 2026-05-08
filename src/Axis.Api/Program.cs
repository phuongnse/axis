using System.Text;
using Axis.Api.Authorization;
using Axis.Api.Infrastructure;
using Axis.Api.Middleware;
using Axis.DataModeling.Infrastructure.Extensions;
using Axis.FormBuilder.Infrastructure.Extensions;
using Axis.Identity.Infrastructure.Extensions;
using Axis.Shared.Application.Behaviors;
using Axis.WorkflowBuilder.Infrastructure.Extensions;
using Axis.WorkflowEngine.Infrastructure.Extensions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using StackExchange.Redis;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, config) => config
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services));

    // MediatR + validation pipeline
    builder.Services.AddMediatR(cfg =>
    {
        cfg.RegisterServicesFromAssemblies(
            typeof(Axis.Identity.Application.Commands.RegisterOrganization.RegisterOrganizationCommand).Assembly,
            typeof(Axis.DataModeling.Application.Commands.CreateModel.CreateModelCommand).Assembly,
            typeof(Axis.WorkflowBuilder.Application.Commands.CreateWorkflow.CreateWorkflowCommand).Assembly,
            typeof(Axis.FormBuilder.Application.Commands.CreateForm.CreateFormCommand).Assembly,
            typeof(Axis.WorkflowEngine.Application.Commands.CancelExecution.CancelExecutionCommand).Assembly
        );
        cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
    });

    builder.Services.AddValidatorsFromAssemblies([
        typeof(Axis.Identity.Application.Commands.RegisterOrganization.RegisterOrganizationCommand).Assembly,
        typeof(Axis.DataModeling.Application.Commands.CreateModel.CreateModelCommand).Assembly,
        typeof(Axis.WorkflowBuilder.Application.Commands.CreateWorkflow.CreateWorkflowCommand).Assembly,
        typeof(Axis.FormBuilder.Application.Commands.CreateForm.CreateFormCommand).Assembly,
        typeof(Axis.WorkflowEngine.Application.Commands.CancelExecution.CancelExecutionCommand).Assembly,
    ]);

    // JWT Authentication
    var jwtKey = builder.Configuration["Jwt:SecretKey"]
        ?? throw new InvalidOperationException("Jwt:SecretKey is required");

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"],
                ValidateAudience = true,
                ValidAudience = builder.Configuration["Jwt:Audience"],
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero,
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    var jti = context.Principal?.FindFirst("jti")?.Value;
                    if (jti is not null)
                    {
                        var blacklist = context.HttpContext.RequestServices
                            .GetRequiredService<IJtiBlacklist>();
                        if (await blacklist.IsBlacklistedAsync(jti))
                            context.Fail("Token has been revoked.");
                    }
                },
                OnChallenge = context =>
                {
                    if (!context.Handled)
                    {
                        context.Response.StatusCode = 401;
                        context.Response.ContentType = "application/json";
                        context.HandleResponse();
                        return context.Response.WriteAsync(
                            """{"error":"unauthorized","message":"Authentication required."}""");
                    }
                    return Task.CompletedTask;
                },
                OnForbidden = context =>
                {
                    context.Response.StatusCode = 403;
                    context.Response.ContentType = "application/json";
                    return context.Response.WriteAsync(
                        """{"error":"forbidden","message":"You do not have permission to perform this action."}""");
                },
            };
        });

    // Permission-based authorization
    builder.Services.AddAuthorization();
    builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
    builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();

    // Module infrastructure
    var cfg = builder.Configuration;
    builder.Services.AddIdentityInfrastructure(cfg);
    builder.Services.AddDataModelingInfrastructure(cfg.GetConnectionString("DataModeling")!);
    builder.Services.AddWorkflowBuilderInfrastructure(cfg.GetConnectionString("WorkflowBuilder")!);
    builder.Services.AddFormBuilderInfrastructure(cfg.GetConnectionString("FormBuilder")!);
    builder.Services.AddWorkflowEngineInfrastructure(cfg.GetConnectionString("WorkflowEngine")!);

    // Redis
    builder.Services.AddSingleton<IConnectionMultiplexer>(_ =>
        ConnectionMultiplexer.Connect(
            builder.Configuration["Redis:ConnectionString"]
                ?? throw new InvalidOperationException("Redis:ConnectionString is required")));
    builder.Services.AddSingleton<IJtiBlacklist, RedisJtiBlacklist>();

    // API services
    builder.Services.AddScoped<ITokenService, JwtTokenService>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<CurrentUser>();

    // CORS
    var allowedOrigins = builder.Configuration
        .GetSection("Cors:AllowedOrigins")
        .Get<string[]>() ?? [];

    builder.Services.AddCors(opts => opts.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials()));

    builder.Services.AddControllers()
        .AddJsonOptions(opts =>
        {
            opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.SnakeCaseLower;
            opts.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        });

    var app = builder.Build();

    app.UseMiddleware<ValidationExceptionMiddleware>();
    app.UseSerilogRequestLogging();
    app.UseCors();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapControllers();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

// Needed for WebApplicationFactory in tests
public partial class Program;
