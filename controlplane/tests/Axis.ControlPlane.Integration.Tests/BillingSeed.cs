using System.Text;
using Axis.ControlPlane.Plans;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>The catalogue that the billing API tests share.</summary>
internal static class BillingSeed
{
    public static PlanCatalogue Catalogue()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes("""
                { "ControlPlane": { "plans": [{
                  "code": "team",
                  "version": 1,
                  "name": { "en": "Team", "vi": "Nhóm" },
                  "includedUsers": 25,
                  "quotas": {
                    "storage": 10737418240,
                    "applications": 5,
                    "emailsPerDay": 2000,
                    "packageCpuSecondsPerDay": 3600,
                    "concurrentImports": 2,
                    "concurrentConnectorCalls": 10,
                    "concurrentQueries": 10,
                    "workerWeight": 2,
                    "authoringRequests": 200
                  },
                  "addOns": [{ "code": "users-10", "users": 10 }]
                }] } }
                """)))
            .Build();
        var options = new ControlPlaneOptions();
        configuration.GetSection(ControlPlaneOptions.SectionName).Bind(options);
        return PlanCatalogue.Create(options.Plans);
    }
}
