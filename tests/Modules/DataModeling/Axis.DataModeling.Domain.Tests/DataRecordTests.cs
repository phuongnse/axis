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
    public void DataRecord_WhenCreated_SetsModelIdOrgIdAndData()
    {
        var data = SampleData();
        var record = DataRecord.Create(ModelId, OrgId, data, UserId);

        record.ModelId.Should().Be(ModelId);
        record.OrganizationId.Should().Be(OrgId);
        record.Data.Should().ContainKey("amount").WhoseValue.Should().Be(100);
        record.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void DataRecord_WhenCreated_SetsCreatedByAndTimestamps()
    {
        var before = DateTimeOffset.UtcNow;
        var record = DataRecord.Create(ModelId, OrgId, SampleData(), UserId);

        record.CreatedBy.Should().Be(UserId);
        record.CreatedAt.Should().BeOnOrAfter(before);
        record.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void DataRecord_WhenUpdated_ReplacesDataAndBumpsUpdatedAt()
    {
        var record = DataRecord.Create(ModelId, OrgId, SampleData(), UserId);
        var before = record.UpdatedAt;

        var newData = new Dictionary<string, object?> { ["amount"] = 200, ["notes"] = "updated" };
        record.Update(newData);

        record.Data["amount"].Should().Be(200);
        record.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Delete_WhenCalled_SetsDeletedAtAndRaisesEvent()
    {
        var record = DataRecord.Create(ModelId, OrgId, SampleData(), UserId);
        var before = DateTimeOffset.UtcNow;
        record.Delete();

        record.DeletedAt.Should().NotBeNull();
        record.DeletedAt!.Value.Should().BeOnOrAfter(before);
        record.DomainEvents.Should().ContainSingle(e => e is DataRecordDeleted);
    }

    [Fact]
    public void Delete_WhenAlreadyDeleted_Throws()
    {
        var record = DataRecord.Create(ModelId, OrgId, SampleData(), UserId);
        record.Delete();

        var act = () => record.Delete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already deleted*");
    }
}
