using Confluent.SchemaRegistry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.Kafka;
using Wolverine.Kafka.Serialization;

namespace Axis.Shared.Infrastructure.Messaging;

public static class WolverineKafkaAvroExtensions
{
    public const string SchemaRegistryUrlKey = "SchemaRegistry:Url";

    /// <summary>
    /// Registers Schema Registry client, CloudEvents envelope rules, and optional Kafka Avro transport.
    /// When <paramref name="useKafkaTransport"/> is false (e.g. Testing), callers should route events with <c>.Locally</c>.
    /// </summary>
    public static WolverineOptions UseAxisKafkaAvro(
        this WolverineOptions opts,
        IConfiguration configuration,
        bool useKafkaTransport,
        Action<WolverineOptions, SchemaRegistryAvroSerializer>? configureKafkaEvents = null,
        Action<WolverineOptions>? configureLocalEvents = null)
    {
        opts.MetadataRules.Add(new CloudEventsEnvelopeRule());

        if (!useKafkaTransport)
        {
            configureLocalEvents?.Invoke(opts);
            return opts;
        }

        string schemaRegistryUrl = configuration[SchemaRegistryUrlKey]
            ?? throw new InvalidOperationException($"{SchemaRegistryUrlKey} is required when Kafka Avro is enabled.");

        ISchemaRegistryClient registry = new CachedSchemaRegistryClient(new SchemaRegistryConfig { Url = schemaRegistryUrl });
        opts.Services.AddSingleton(registry);
        SchemaRegistryAvroSerializer avroSerializer = new(registry);
        configureKafkaEvents?.Invoke(opts, avroSerializer);
        return opts;
    }

    public static void PublishAndListenWithAvro<T>(
        WolverineOptions opts,
        string topic,
        SchemaRegistryAvroSerializer serializer)
    {
        opts.PublishMessage<T>()
            .ToKafkaTopic(topic)
            .DefaultSerializer(serializer);

        opts.ListenToKafkaTopic(topic)
            .ProcessInline()
            .DefaultSerializer(serializer);
    }

    public static void PublishLocally<T>(WolverineOptions opts) =>
        opts.PublishMessage<T>().Locally();
}
