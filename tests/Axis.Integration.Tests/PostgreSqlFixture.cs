using Testcontainers.PostgreSql;

namespace Axis.Integration.Tests;

/// <summary>Starts one real PostgreSQL container shared by the tests in a class.</summary>
public sealed class PostgreSqlFixture : IAsyncLifetime
{
    public const string Image = "postgres:18-alpine";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image).Build();

    public string ConnectionString => _container.GetConnectionString();

    public async ValueTask InitializeAsync() => await _container.StartAsync();

    public async ValueTask DisposeAsync() => await _container.DisposeAsync();
}
