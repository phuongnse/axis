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
                }, {
                  "code": "team",
                  "version": 2,
                  "name": { "en": "Team", "vi": "Nhóm" },
                  "includedUsers": 25,
                  "quotas": {
                    "storage": 21474836480,
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
                }, {
                  "code": "trial",
                  "version": 1,
                  "trial": true,
                  "name": { "en": "Trial", "vi": "Dùng thử" },
                  "includedUsers": 10,
                  "quotas": {
                    "storage": 1073741824,
                    "applications": 2,
                    "emailsPerDay": 200,
                    "packageCpuSecondsPerDay": 0,
                    "concurrentImports": 1,
                    "concurrentConnectorCalls": 2,
                    "concurrentQueries": 2,
                    "workerWeight": 1,
                    "authoringRequests": 20
                  },
                  "addOns": []
                }] } }
                """)))
            .Build();
        var options = new ControlPlaneOptions();
        configuration.GetSection(ControlPlaneOptions.SectionName).Bind(options);
        return PlanCatalogue.Create(options.Plans);
    }
}
