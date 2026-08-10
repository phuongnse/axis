using Axis.Audit.Application;
using Axis.Audit.Application.Persistence;
using Axis.Audit.Contracts;
using Axis.Audit.Infrastructure.Persistence;
using Axis.Audit.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Axis.Audit.Infrastructure.Extensions;

public static class AuditInfrastructureExtensions
{
    public static IServiceCollection AddAuditInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AuditDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Audit")));
        services.AddScoped<IAuditRecordRepository, AuditRecordRepository>();
        services.AddScoped<IAuditUnitOfWork, AuditUnitOfWork>();
        services.AddScoped<IAuditEventIngestionService, AuditEventIngestionService>();
        services.AddScoped<IAuditEventSink, AuditEventSink>();
        return services;
    }
}
