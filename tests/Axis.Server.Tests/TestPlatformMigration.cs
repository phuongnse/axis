using System.Xml.Linq;
using Axis.Server.Authentication;
using Axis.Server.Studio;
using Axis.Server.Tenancy;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Axis.Server.Tests;

/// <summary>
/// Removes the platform migration step and the key ring start check, and keeps the data protection keys in memory,
/// so a test host starts and signs in without a platform database.
/// </summary>
internal static class TestPlatformMigration
{
    /// <summary>
    /// Removes the hosted service that applies the platform migrations under <c>Tenancy:Source=configuration</c>.
    /// It also removes the hosted service that decrypts the stored data protection keys, and replaces the key store
    /// with an in-memory one. It also replaces the Studio write lock, which needs the platform database, with a lock
    /// of this process. It runs after the server's own registrations.
    /// </summary>
    public static IWebHostBuilder WithoutPlatformMigration(this IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
        {
            foreach (var descriptor in services
                .Where(d => d.ServiceType == typeof(IHostedService)
                    && (d.ImplementationType == typeof(PlatformMigrationStart) || d.ImplementationType == typeof(PlatformKeyRingStart)))
                .ToList())
            {
                services.Remove(descriptor);
            }

            services.Configure<KeyManagementOptions>(options => options.XmlRepository = new TestKeyRing());
            services.AddSingleton<IStudioWriteLock, InProcessStudioWriteLock>();
        });

    // One lock for every folder of the instance, as the server held before the platform lock.
    private sealed class InProcessStudioWriteLock : IStudioWriteLock, IDisposable
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public async Task<IAsyncDisposable> TakeAsync(string folder, CancellationToken cancellationToken)
        {
            await _semaphore.WaitAsync(cancellationToken);
            return new Held(_semaphore);
        }

        public void Dispose() => _semaphore.Dispose();

        private sealed class Held(SemaphoreSlim semaphore) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                semaphore.Release();
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class TestKeyRing : IXmlRepository
    {
        private readonly List<XElement> _elements = [];

        public IReadOnlyCollection<XElement> GetAllElements()
        {
            lock (_elements)
            {
                return [.. _elements];
            }
        }

        public void StoreElement(XElement element, string friendlyName)
        {
            lock (_elements)
            {
                _elements.Add(element);
            }
        }
    }
}
