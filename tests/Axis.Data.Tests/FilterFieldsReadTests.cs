using Axis.Configuration.Compilation;
using Axis.Configuration.Model;
using Axis.Data.DataSources;
using Axis.Data.Records;
using Axis.Expressions.Typing;
using static Axis.Data.Tests.Models;

namespace Axis.Data.Tests;

/// <summary>
/// The fields a policy filter or a data source filter reads, which decide whether a pending computed
/// field refuses it. A name or path inside <c>any</c> or <c>count</c> reads fields of the child row
/// or the linked record.
/// </summary>
public sealed class FilterFieldsReadTests
{
    private static readonly EntityModel _department = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1b01"), "Department", "entities/department.json", "name", Field("name", FieldType.Text));

    private static readonly EntityModel _person = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1b02"),
        "Person",
        "entities/person.json",
        "userId",
        Field("userId", FieldType.Text),
        Field("department", FieldType.Reference, target: _department));

    private static readonly EntityModel _line = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1b03"), "Line", "entities/line.json", Field("quantity", FieldType.Integer));

    private static readonly EntityModel _job = Entity(
        Guid.Parse("4b6f0c1e-6a0e-4c47-9a53-0f5f8f8b1b04"),
        "Job",
        "entities/job.json",
        "title",
        Field("title", FieldType.Text),
        Field("assignees", FieldType.References, target: _person),
        Field("lines", FieldType.ChildCollection, target: _line));

    private static readonly ApplicationModel _application = Application(_department, _person, _line, _job);

    private static EntityModel? FindEntity(string name) => _application.TryGetEntity(name, out var entity) ? entity : null;

    private static ExpressionModel Compile(string filter) =>
        ExpressionModel.Compile(filter, ExpressionScopes.ForDataSource(_job.Fields, [], FindEntity), ExpressionType.Boolean, policyFilter: true);

    [Fact]
    public void Policy_filter_reads_the_fields_of_the_linked_record_and_of_the_entities_on_its_paths()
    {
        var read = RecordFilters.FieldsRead(_application, _job, Compile("title != null and any(assignees, userId == currentUser() and department.name == 'Sales')"));

        Assert.Equal(
            [("Job", "title"), ("Person", "userId"), ("Person", "department"), ("Department", "name")],
            read.Select(field => (field.Entity.Name, field.Field)));
    }

    [Fact]
    public void Policy_filter_reads_the_fields_of_a_child_row()
    {
        var read = RecordFilters.FieldsRead(_application, _job, Compile("count(lines, quantity > 2) >= 2"));

        Assert.Equal([("Line", "quantity")], read.Select(field => (field.Entity.Name, field.Field)));
    }

    [Fact]
    public void Data_source_filter_reads_the_fields_of_the_linked_record_with_a_path_that_starts_at_the_list()
    {
        var dataSource = new DataSourceModel
        {
            Id = Guid.NewGuid(),
            Name = "Jobs",
            File = "data-sources/jobs.json",
            Entity = new EntityReference(_job.Id, _job.Name),
            Fields = [],
            Filter = Compile("any(assignees, department.name == 'Sales') and count(lines) > 0"),
        };

        var read = DataSourceQueries.FilterFieldsRead(_application, dataSource);

        Assert.Equal(
            [(_person.Id, "department", "assignees.department.name"), (_department.Id, "name", "assignees.department.name")],
            read);
    }
}
