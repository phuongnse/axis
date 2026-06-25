using Axis.Identity.Application.Repositories;
using Axis.Identity.Application.Services;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Identity.Infrastructure.Repositories;
using Axis.Identity.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.Identity.Infrastructure.Extensions;

public static class IdentityInfrastructureExtensions
{
    private const string TestingEnvironmentName = "Testing";

    public static IServiceCollection AddIdentityInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
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

        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRegistrationIdempotencyRepository, RegistrationIdempotencyRepository>();

        services.AddScoped<IUnitOfWork, IdentityUnitOfWork>();
        services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        services.AddScoped<IEmailSender, MailKitEmailSender>();
        services.AddSingleton<IResendVerificationRateLimiter, RedisResendVerificationRateLimiter>();
        services.AddScoped<IEmailVerificationTokenStore, EmailVerificationTokenStore>();
        services.AddScoped<IWorkspaceSlugGenerator, WorkspaceSlugGenerator>();

        if (environment.IsDevelopment() || environment.IsEnvironment(TestingEnvironmentName))
            services.AddHostedService<OpenIddictSeeder>();

        return services;
    }
}
