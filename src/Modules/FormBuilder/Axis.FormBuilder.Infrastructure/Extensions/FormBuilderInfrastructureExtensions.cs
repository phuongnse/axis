using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.FormBuilder.Infrastructure.Repositories;
using Axis.FormBuilder.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.FormBuilder.Infrastructure.Extensions;

public static class FormBuilderInfrastructureExtensions
{
    public static IServiceCollection AddFormBuilderInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<FormBuilderDbContext>(opts =>
            opts.UseNpgsql(configuration.GetConnectionString("FormBuilder")
                ?? throw new InvalidOperationException("Missing connection string 'FormBuilder'.")));

        services.AddScoped<IFormRepository, FormRepository>();
        services.AddScoped<IFormSubmissionRepository, FormSubmissionRepository>();
        services.AddScoped<IFormModelReferenceRepository, FormModelReferenceRepository>();
        services.AddScoped<IFormModelReferenceSync, FormModelReferenceSync>();
        services.AddScoped<IUnitOfWork, FormBuilderUnitOfWork>();

        return services;
    }
}
