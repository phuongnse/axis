using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Networks;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Axis.Testing.Messaging;

/// <summary>
/// Shared Testcontainers stack for API and messaging integration tests: PostgreSQL, Redis,
/// Kafka, Confluent Schema Registry, and RabbitMQ. Kafka event transport uses the same
/// brokers and registry URL as production (ADR-019).
/// </summary>
public sealed class MessagingTestInfrastructure : IAsyncDisposable
{
    private INetwork? _network;
    private PostgreSqlContainer? _postgres;
    private RedisContainer? _redis;
    private KafkaContainer? _kafka;
    private IContainer? _schemaRegistry;
    private RabbitMqContainer? _rabbitMq;

    public string PostgresAdminConnectionString { get; private set; } = null!;

    public string KafkaBootstrapAddress { get; private set; } = null!;

    public string SchemaRegistryUrl { get; private set; } = null!;

    public string RabbitMqConnectionString { get; private set; } = null!;

    public string RedisConnectionString { get; private set; } = null!;

    public async Task StartAsync()
    {
        _network = new NetworkBuilder().Build();
        await _network.CreateAsync();

        _postgres = new PostgreSqlBuilder()
            .WithImage("postgres:16-alpine")
            .Build();
        _redis = new RedisBuilder()
            .WithImage("redis:7-alpine")
            .Build();
        _kafka = new KafkaBuilder()
            .WithImage("confluentinc/cp-kafka:7.7.0")
            .WithNetwork(_network)
            .WithNetworkAliases("kafka")
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
            .Build();
        _schemaRegistry = new ContainerBuilder()
            .WithImage("confluentinc/cp-schema-registry:7.7.0")
            .WithNetwork(_network)
            .WithEnvironment("SCHEMA_REGISTRY_HOST_NAME", "schema-registry")
            .WithEnvironment("SCHEMA_REGISTRY_LISTENERS", "http://0.0.0.0:8081")
            .WithEnvironment("SCHEMA_REGISTRY_KAFKASTORE_BOOTSTRAP_SERVERS", "PLAINTEXT://kafka:9092")
            .WithEnvironment("SCHEMA_REGISTRY_SCHEMA_COMPATIBILITY_LEVEL", "BACKWARD")
            .WithPortBinding(8081, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r =>
                r.ForPort(8081).ForPath("/subjects")))
            .Build();
        _rabbitMq = new RabbitMqBuilder()
            .WithImage("rabbitmq:3.13-management-alpine")
            .Build();

        await Task.WhenAll(
            _postgres.StartAsync(),
            _redis.StartAsync(),
            _kafka.StartAsync(),
            _rabbitMq.StartAsync());
        await _schemaRegistry.StartAsync();

        PostgresAdminConnectionString = _postgres.GetConnectionString();
        KafkaBootstrapAddress = _kafka.GetBootstrapAddress();
        RabbitMqConnectionString = _rabbitMq.GetConnectionString();
        RedisConnectionString = _redis.GetConnectionString();

        int schemaRegistryPort = _schemaRegistry.GetMappedPublicPort(8081);
        SchemaRegistryUrl = $"http://127.0.0.1:{schemaRegistryPort}";

        await AvroSchemaRegistryRegistrar.RegisterModuleEventSchemasAsync(SchemaRegistryUrl);
    }

    public async ValueTask DisposeAsync()
    {
        if (_schemaRegistry is not null)
            await _schemaRegistry.DisposeAsync();

        List<Task> disposals = [];
        if (_postgres is not null)
            disposals.Add(_postgres.DisposeAsync().AsTask());
        if (_redis is not null)
            disposals.Add(_redis.DisposeAsync().AsTask());
        if (_kafka is not null)
            disposals.Add(_kafka.DisposeAsync().AsTask());
        if (_rabbitMq is not null)
            disposals.Add(_rabbitMq.DisposeAsync().AsTask());
        await Task.WhenAll(disposals);

        if (_network is not null)
            await _network.DisposeAsync();
    }
}
