using Amazon.S3;
using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Grpc;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.PlanLimits;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Services;
using Axis.Shared.Application.PlanLimits;
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

        services.AddScoped<ITenantRepository, TenantRepository>();
        services.AddScoped<ITenantAccessService, TenantAccessService>();
        services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
        services.AddScoped<ITenantPlanChangeLogRepository, TenantPlanChangeLogRepository>();
        services.AddScoped<IPlatformAdminAuthorization, ConfigPlatformAdminAuthorization>();
        services.AddScoped<IPlanLimitUsageCounter, UserPlanLimitUsageCounter>();
        services.AddSingleton<PlanLimitRedisCache>();
        services.AddScoped<IPlanLimitService, PlanLimitService>();
        services.AddScoped<ITenantModuleProvisioningRepository, TenantModuleProvisioningRepository>();
        services.AddScoped<IPlatformProvisioningAlert, LoggingPlatformProvisioningAlert>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<ITenantMembershipRepository, TenantMembershipRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<IRegistrationIdempotencyRepository, RegistrationIdempotencyRepository>();

        services.AddScoped<IUnitOfWork, IdentityUnitOfWork>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddSingleton<IResendVerificationRateLimiter, RedisResendVerificationRateLimiter>();
        services.AddScoped<IPasswordResetTokenStore, PasswordResetTokenStore>();
        services.AddScoped<IEmailVerificationTokenStore, EmailVerificationTokenStore>();
        services.AddScoped<ITenantRegistrationTokenStore, TenantRegistrationTokenStore>();
        services.AddScoped<ITenantSlugGenerator, TenantSlugGenerator>();
        services.AddScoped<ISessionStore, SessionStoreService>();

        services.AddGrpc();

        services.AddHostedService<OpenIddictSeeder>();
        services.AddHostedService<SubscriptionPlanSeeder>();
        services.AddHostedService<TenantSettingsPermissionSeeder>();

        services.AddAWSService<IAmazonS3>();
        services.AddScoped<IAvatarStorageService, S3AvatarStorageService>();
        services.AddScoped<ITenantLogoStorageService, S3TenantLogoStorageService>();
        services.AddScoped<ITenantDeletionScheduler, WolverineTenantDeletionScheduler>();
        services.AddScoped<ItenantIdentityPurger, tenantIdentityPurger>();

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
