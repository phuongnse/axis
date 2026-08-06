using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Axis.Identity.Infrastructure.Extensions;

public static class IdentityInfrastructureExtensions
{
    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        OpenIddictClientCatalog clientCatalog = OpenIddictClientCatalog.Load(configuration);

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

        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<IOrganizationMembershipRepository, OrganizationMembershipRepository>();
        services.AddScoped<IWorkspaceMembershipRepository, WorkspaceMembershipRepository>();
        services.AddScoped<IWorkspaceContextTransitionRepository, WorkspaceContextTransitionRepository>();
        services.AddScoped<ICreateOrganizationIdempotencyRepository, CreateOrganizationIdempotencyRepository>();
        services.AddScoped<IIdentityAuditOutbox, IdentityAuditOutbox>();
        services.AddScoped<IIdentityAuditDispatchStore, IdentityAuditDispatchStore>();
        services.AddScoped<IIdentityAuditHealthReader, IdentityAuditHealthReader>();
        services.AddScoped<IWorkspaceTransitionCleanupStore, WorkspaceTransitionCleanupStore>();
        services.AddScoped<IWorkspaceTransitionExpiryStore, WorkspaceTransitionExpiryStore>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRegistrationIdempotencyRepository, RegistrationIdempotencyRepository>();

        services.AddScoped<IUnitOfWork, IdentityUnitOfWork>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddSingleton<IResendVerificationRateLimiter, RedisResendVerificationRateLimiter>();
        services.AddScoped<IEmailVerificationTokenStore, EmailVerificationTokenStore>();
        services.AddScoped<IWorkspaceSlugGenerator, WorkspaceSlugGenerator>();
        services.AddSingleton(clientCatalog);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHostedService<OpenIddictSeeder>();
        services.AddHostedService<IdentityAuditDispatcher>();

        return services;
    }
}
