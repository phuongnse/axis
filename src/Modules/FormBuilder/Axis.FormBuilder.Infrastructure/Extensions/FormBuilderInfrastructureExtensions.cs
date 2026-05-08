using Axis.FormBuilder.Application.Repositories;
using Axis.FormBuilder.Application.Services;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.FormBuilder.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.FormBuilder.Infrastructure.Extensions;

public static class FormBuilderInfrastructureExtensions
{
    public static IServiceCollection AddFormBuilderInfrastructure(
        this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<FormBuilderDbContext>(opts =>
            opts.UseNpgsql(connectionString));

        services.AddScoped<IFormRepository, FormRepository>();
        services.AddScoped<IUnitOfWork, FormBuilderUnitOfWork>();

        return services;
    }
}
