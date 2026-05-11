using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Events;
using FluentAssertions;

namespace Axis.DataModeling.Domain.Tests;

public class DataRecordTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();
    private const string UserId = "user-123";

    private static IReadOnlyDictionary<string, object?> SampleData() =>
        new Dictionary<string, object?> { ["amount"] = 100, ["notes"] = "test" };

    [Fact]
    public void Create_sets_modelId_orgId_and_data()
    {
        var data = SampleData();
        var record = DataRecord.Create(ModelId, OrgId, data, UserId);

        record.ModelId.Should().Be(ModelId);
        record.OrganizationId.Should().Be(OrgId);
        record.Data.Should().ContainKey("amount").WhoseValue.Should().Be(100);
        record.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Create_sets_CreatedBy_and_DateTimeOffset_timestamps()
    {
        var before = DateTimeOffset.UtcNow;
        var record = DataRecord.Create(ModelId, OrgId, SampleData(), UserId);

        record.CreatedBy.Should().Be(UserId);
        record.CreatedAt.Should().BeOnOrAfter(before);
        record.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Update_replaces_data_and_bumps_UpdatedAt()
    {
        var record = DataRecord.Create(ModelId, OrgId, SampleData(), UserId);
        var before = record.UpdatedAt;

        var newData = new Dictionary<string, object?> { ["amount"] = 200, ["notes"] = "updated" };
        record.Update(newData);

        record.Data["amount"].Should().Be(200);
        record.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Delete_sets_DeletedAt_and_raises_event()
    {
        var record = DataRecord.Create(ModelId, OrgId, SampleData(), UserId);
        var before = DateTimeOffset.UtcNow;
        record.Delete();

        record.DeletedAt.Should().NotBeNull();
        record.DeletedAt!.Value.Should().BeOnOrAfter(before);
        record.DomainEvents.Should().ContainSingle(e => e is DataRecordDeleted);
    }

    [Fact]
    public void Delete_throws_when_already_deleted()
    {
        var record = DataRecord.Create(ModelId, OrgId, SampleData(), UserId);
        record.Delete();

        var act = () => record.Delete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already deleted*");
    }
}
