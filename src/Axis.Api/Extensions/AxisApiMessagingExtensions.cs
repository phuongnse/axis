using axis.datamodeling.events;
using axis.formbuilder.events;
using axis.identity.events;
using axis.workflowbuilder.events;
using axis.workflowengine.events;
using Axis.Api.Infrastructure;
using Axis.DataModeling.Contracts;
using Axis.DataModeling.Infrastructure.Extensions;
using Axis.DataModeling.Infrastructure.Persistence;
using Axis.FormBuilder.Contracts;
using Axis.FormBuilder.Infrastructure.Extensions;
using Axis.FormBuilder.Infrastructure.Persistence;
using Axis.Identity.Contracts;
using Axis.Identity.Infrastructure.Extensions;
using Axis.Identity.Infrastructure.Persistence;
using Axis.Shared.Infrastructure.Messaging;
using Axis.Shared.Infrastructure.Wolverine;
using Axis.WorkflowBuilder.Contracts;
using Axis.WorkflowBuilder.Infrastructure.Extensions;
using Axis.WorkflowBuilder.Infrastructure.Persistence;
using Axis.WorkflowEngine.Contracts;
using Axis.WorkflowEngine.Infrastructure.Extensions;
using Axis.WorkflowEngine.Infrastructure.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Kafka;
using Wolverine.Kafka.Serialization;
using Wolverine.Persistence.Durability;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace Axis.Api.Extensions;

internal static class AxisApiMessagingExtensions
{
    private static readonly ICrossModuleEventRoute[] CrossModuleEventRoutes =
    [
        new CrossModuleEventRoute<WorkflowPublishedEvent>(WorkflowBuilderKafkaTopics.WorkflowPublished),
        new CrossModuleEventRoute<WorkflowArchivedEvent>(WorkflowBuilderKafkaTopics.WorkflowArchived),
        new CrossModuleEventRoute<WorkflowUnarchivedEvent>(WorkflowBuilderKafkaTopics.WorkflowUnarchived),

        new CrossModuleEventRoute<WorkspaceVerifiedEvent>(IdentityKafkaTopics.WorkspaceVerified),
        new CrossModuleEventRoute<WorkspaceModuleProvisionReportEvent>(IdentityKafkaTopics.WorkspaceModuleProvisionReport),
        new CrossModuleEventRoute<UserDeactivatedEvent>(IdentityKafkaTopics.UserDeactivated),
        new CrossModuleEventRoute<UserReactivatedEvent>(IdentityKafkaTopics.UserReactivated),
        new CrossModuleEventRoute<RoleAssignedEvent>(IdentityKafkaTopics.RoleAssigned),
        new CrossModuleEventRoute<RoleRemovedEvent>(IdentityKafkaTopics.RoleRemoved),

        new CrossModuleEventRoute<FormStepReachedEvent>(WorkflowEngineKafkaTopics.FormStepReached),
        new CrossModuleEventRoute<FormTaskSubmittedEvent>(FormBuilderKafkaTopics.FormTaskSubmitted),
        new CrossModuleEventRoute<FormTaskExpiredEvent>(FormBuilderKafkaTopics.FormTaskExpired),
        new CrossModuleEventRoute<FormDeletedEvent>(FormBuilderKafkaTopics.FormDeleted),

        new CrossModuleEventRoute<ModelCreatedEvent>(DataModelingKafkaTopics.ModelCreated),
        new CrossModuleEventRoute<ModelDeletedEvent>(DataModelingKafkaTopics.ModelDeleted),
        new CrossModuleEventRoute<DataClassCreatedEvent>(DataModelingKafkaTopics.DataClassCreated),
        new CrossModuleEventRoute<DataClassDeletedEvent>(DataModelingKafkaTopics.DataClassDeleted),
        new CrossModuleEventRoute<DataRecordCreatedEvent>(DataModelingKafkaTopics.DataRecordCreated),
        new CrossModuleEventRoute<DataRecordDeletedEvent>(DataModelingKafkaTopics.DataRecordDeleted),
        new CrossModuleEventRoute<FieldAddedEvent>(DataModelingKafkaTopics.FieldAdded),
        new CrossModuleEventRoute<FieldUpdatedEvent>(DataModelingKafkaTopics.FieldUpdated),
        new CrossModuleEventRoute<FieldRemovedEvent>(DataModelingKafkaTopics.FieldRemoved),
    ];

    public static WebApplicationBuilder AddAxisMessaging(this WebApplicationBuilder builder)
    {
        // Capture the live ConfigurationManager and read inside the lambda so
        // WebApplicationFactory.ConfigureAppConfiguration overrides applied later
        // in test setup are picked up by Wolverine.
        IConfiguration configuration = builder.Configuration;

        builder.Host.UseWolverine(opts =>
        {
            string identityConnectionString = RequiredConnectionString(configuration, "Identity");
            string dataModelingConnectionString = RequiredConnectionString(configuration, "DataModeling");
            string workflowBuilderConnectionString = RequiredConnectionString(configuration, "WorkflowBuilder");
            string formBuilderConnectionString = RequiredConnectionString(configuration, "FormBuilder");
            string workflowEngineConnectionString = RequiredConnectionString(configuration, "WorkflowEngine");
            string kafkaBrokers = RequiredValue(configuration, "Kafka:Brokers");
            string rabbitMqConnectionString = RequiredConnectionString(configuration, "RabbitMq");

            opts.Policies.AddMiddleware<HandlerLoggingMiddleware>();
            opts.UseEntityFrameworkCoreTransactions();

            opts.PersistMessagesWithPostgresql(identityConnectionString, "wolverine");

            opts.PersistMessagesWithPostgresql(identityConnectionString, "wolverine", MessageStoreRole.Ancillary)
                .Enroll<IdentityDbContext>();
            opts.PersistMessagesWithPostgresql(dataModelingConnectionString, "wolverine", MessageStoreRole.Ancillary)
                .Enroll<DataModelingDbContext>();
            opts.PersistMessagesWithPostgresql(workflowBuilderConnectionString, "wolverine", MessageStoreRole.Ancillary)
                .Enroll<WorkflowBuilderDbContext>();
            opts.PersistMessagesWithPostgresql(formBuilderConnectionString, "wolverine", MessageStoreRole.Ancillary)
                .Enroll<FormBuilderDbContext>();
            opts.PersistMessagesWithPostgresql(workflowEngineConnectionString, "wolverine", MessageStoreRole.Ancillary)
                .Enroll<WorkflowEngineDbContext>();

            if (builder.Environment.IsDevelopmentOrTesting())
            {
                opts.UseKafka(kafkaBrokers).AutoProvision();
                opts.UseRabbitMq(new Uri(rabbitMqConnectionString)).AutoProvision();
            }
            else
            {
                opts.UseKafka(kafkaBrokers);
                opts.UseRabbitMq(new Uri(rabbitMqConnectionString));
            }

            opts.UseAxisKafkaAvro(
                configuration,
                useKafkaTransport: !builder.Environment.IsTesting(),
                configureKafkaEvents: ConfigureKafkaEventRoutes,
                configureLocalEvents: ConfigureLocalEventRoutes);

            opts.Discovery.IncludeAssembly(typeof(IdentityInfrastructureExtensions).Assembly);
            opts.Discovery.IncludeAssembly(typeof(DataModelingInfrastructureExtensions).Assembly);
            opts.Discovery.IncludeAssembly(typeof(WorkflowBuilderInfrastructureExtensions).Assembly);
            opts.Discovery.IncludeAssembly(typeof(WorkflowEngineInfrastructureExtensions).Assembly);
            opts.Discovery.IncludeAssembly(typeof(FormBuilderInfrastructureExtensions).Assembly);
        });

        return builder;
    }

    private static void ConfigureKafkaEventRoutes(
        WolverineOptions opts,
        SchemaRegistryAvroSerializer serializer)
    {
        foreach (ICrossModuleEventRoute route in CrossModuleEventRoutes)
            route.PublishAndListen(opts, serializer);
    }

    private static void ConfigureLocalEventRoutes(WolverineOptions opts)
    {
        foreach (ICrossModuleEventRoute route in CrossModuleEventRoutes)
            route.PublishLocally(opts);
    }

    private static string RequiredConnectionString(IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"ConnectionStrings:{name} is required");

    private static string RequiredValue(IConfiguration configuration, string key) =>
        configuration[key]
        ?? throw new InvalidOperationException($"{key} is required");

    private interface ICrossModuleEventRoute
    {
        void PublishAndListen(WolverineOptions opts, SchemaRegistryAvroSerializer serializer);

        void PublishLocally(WolverineOptions opts);
    }

    private sealed class CrossModuleEventRoute<T>(string topic) : ICrossModuleEventRoute
    {
        public void PublishAndListen(WolverineOptions opts, SchemaRegistryAvroSerializer serializer) =>
            WolverineKafkaAvroExtensions.PublishAndListenWithAvro<T>(opts, topic, serializer);

        public void PublishLocally(WolverineOptions opts) =>
            WolverineKafkaAvroExtensions.PublishLocally<T>(opts);
    }
}
