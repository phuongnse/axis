using Axis.DataModeling.Domain.Aggregates;
using Axis.DataModeling.Domain.Events;
using FluentAssertions;

namespace Axis.DataModeling.Domain.Tests;

public class DataRecordTests
{
    private static readonly Guid OrgId = Guid.NewGuid();
    private static readonly Guid ModelId = Guid.NewGuid();

    private static IReadOnlyDictionary<string, object?> SampleData() =>
        new Dictionary<string, object?> { ["amount"] = 100, ["notes"] = "test" };

    [Fact]
    public void Create_sets_modelId_orgId_and_data()
    {
        var data = SampleData();
        var record = DataRecord.Create(ModelId, OrgId, data);

        record.ModelId.Should().Be(ModelId);
        record.OrganizationId.Should().Be(OrgId);
        record.Data.Should().ContainKey("amount").WhoseValue.Should().Be(100);
        record.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Update_replaces_data_and_bumps_UpdatedAt()
    {
        var record = DataRecord.Create(ModelId, OrgId, SampleData());
        var before = record.UpdatedAt;

        var newData = new Dictionary<string, object?> { ["amount"] = 200, ["notes"] = "updated" };
        record.Update(newData);

        record.Data["amount"].Should().Be(200);
        record.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Delete_sets_IsDeleted_and_raises_event()
    {
        var record = DataRecord.Create(ModelId, OrgId, SampleData());
        record.Delete();

        record.IsDeleted.Should().BeTrue();
        record.DomainEvents.Should().ContainSingle(e => e is DataRecordDeleted);
    }

    [Fact]
    public void Delete_throws_when_already_deleted()
    {
        var record = DataRecord.Create(ModelId, OrgId, SampleData());
        record.Delete();

        var act = () => record.Delete();
        act.Should().Throw<InvalidOperationException>().WithMessage("*already deleted*");
    }
}
