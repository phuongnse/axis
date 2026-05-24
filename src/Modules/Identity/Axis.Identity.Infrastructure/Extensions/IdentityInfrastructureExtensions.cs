using Amazon.S3;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Grpc;
using Axis.Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Identity.Infrastructure.Extensions;

public static class IdentityInfrastructureExtensions
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(opts =>
            opts.UseNpgsql(configuration.GetConnectionString("Identity"))
                // Required by OpenIddict EF Core — stores must be able to resolve
                // the context from the internal service provider
                .UseOpenIddict());

        services.AddOpenIddict()
            .AddCore(opts =>
            {
                opts.UseEntityFrameworkCore()
                    .UseDbContext<IdentityDbContext>();
            });

        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();

        services.AddScoped<IUnitOfWork, IdentityUnitOfWork>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddScoped<IPasswordResetTokenStore, PasswordResetTokenStore>();
        services.AddScoped<ISessionStore, SessionStoreService>();

        services.AddGrpc();

        services.AddHostedService<OpenIddictSeeder>();

        services.AddAWSService<IAmazonS3>();
        services.AddScoped<IAvatarStorageService, S3AvatarStorageService>();

        return services;
    }

    public static WebApplication MapIdentityGrpc(this WebApplication app)
    {
        app.MapGrpcService<IdentityGrpcService>()
            .RequireAuthorization()
            .RequireRateLimiting("auth");
        return app;
    }
}
