using System.Text;
using Axis.ControlPlane.Plans;
using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Axis.ControlPlane.Integration.Tests;

/// <summary>The catalogue and the subscriptions that the billing tests share.</summary>
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
                  "prices": [{ "currency": "VND", "month": 4900000 }, { "currency": "USD", "month": 1900 }],
                  "addOns": [{ "code": "users-10", "users": 10, "prices": [{ "currency": "VND", "month": 500000 }, { "currency": "USD", "month": 20 }] }]
                }] } }
                """)))
            .Build();
        var options = new ControlPlaneOptions();
        configuration.GetSection(ControlPlaneOptions.SectionName).Bind(options);
        return PlanCatalogue.Create(options.Plans);
    }

    /// <summary>A subscription of a verified sign-up that pins the team plan in VND for the period, or none when the period is null.</summary>
    public static async Task<Guid> SubscriptionAsync(NpgsqlDataSource dataSource, string tenant, string? period, CancellationToken cancellationToken)
    {
        var signUp = Guid.CreateVersion7();
        var id = Guid.CreateVersion7();
        await ControlPlaneDatabase.ExecuteAsync(
            dataSource,
            $"""
            INSERT INTO control_plane.sign_ups (id, full_name, email, normalised_email, business_name, subdomain, language, application, state, expires_at)
            VALUES ('{signUp}', 'An', '{tenant}@example.com', '{tenant}@example.com', 'Acme', '{tenant}', 'en', '', 'verified', now())
            """,
            cancellationToken);
        await ControlPlaneDatabase.ExecuteAsync(
            dataSource,
            $"""
            INSERT INTO control_plane.subscriptions (id, tenant_id, sign_up_id, trial_ends_at, step_id, state{(period is null ? "" : ", plan_code, plan_version, currency, period")})
            VALUES ('{id}', '{tenant}', '{signUp}', now(), '{Guid.CreateVersion7()}', 'trialing'{(period is null ? "" : $", 'team', 1, 'VND', '{period}'")})
            """,
            cancellationToken);
        return id;
    }
}
