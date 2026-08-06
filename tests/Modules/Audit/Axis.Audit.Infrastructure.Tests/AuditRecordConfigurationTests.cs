using Axis.Audit.Domain;
using Axis.Audit.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Axis.Audit.Infrastructure.Tests;

public sealed class AuditRecordConfigurationTests
{
    [Fact]
    public void Model_WhenConfigured_MapsEventIdAsUniqueAndRecordsAsImmutable()
    {
        using AuditDbContext context = new(new DbContextOptionsBuilder<AuditDbContext>()
            .UseNpgsql("Host=localhost;Database=axis_audit_model;Username=axis;Password=axis")
            .Options);
        IEntityType entity = context.Model.FindEntityType(typeof(AuditRecord))!;

        entity.FindProperty(nameof(AuditRecord.ActorKind))!.GetColumnName().Should().Be("actor_kind");
        entity.FindProperty(nameof(AuditRecord.ActorId))!.IsNullable.Should().BeTrue();
        entity.FindProperty(nameof(AuditRecord.EventId))!.GetColumnName().Should().Be("event_id");
        entity.GetIndexes().Should().ContainSingle(index =>
            index.IsUnique && index.Properties.Count == 1 && index.Properties[0].Name == nameof(AuditRecord.EventId));
        entity.GetProperties().Should().OnlyContain(property => property.GetAfterSaveBehavior() == PropertySaveBehavior.Throw);
    }
}
