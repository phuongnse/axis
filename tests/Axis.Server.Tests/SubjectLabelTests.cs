using Axis.Configuration.Model;
using Axis.Server.Processes;

namespace Axis.Server.Tests;

/// <summary>The subject label of an instance in the list shows only when the user may read the display field and no release marks it sensitive.</summary>
public sealed class SubjectLabelTests
{
    private static readonly HashSet<string> _titleOnly = new(["title"], StringComparer.Ordinal);

    [Fact]
    public void The_label_shows_when_the_user_has_no_field_limit_and_the_field_is_not_sensitive()
    {
        Assert.True(ProcessInspectionEndpoints.ShowsSubjectLabel(Entity(sensitive: false), Entity(sensitive: false), readable: null));
    }

    [Fact]
    public void The_label_shows_when_the_users_readable_fields_include_the_display_field()
    {
        Assert.True(ProcessInspectionEndpoints.ShowsSubjectLabel(Entity(sensitive: false), Entity(sensitive: false), _titleOnly));
    }

    [Fact]
    public void The_label_shows_when_the_pinned_release_has_no_such_entity()
    {
        Assert.True(ProcessInspectionEndpoints.ShowsSubjectLabel(Entity(sensitive: false), pinned: null, readable: null));
    }

    [Fact]
    public void The_label_is_hidden_when_the_users_readable_fields_lack_the_display_field()
    {
        Assert.False(ProcessInspectionEndpoints.ShowsSubjectLabel(Entity(sensitive: false), Entity(sensitive: false), new HashSet<string>(["status"], StringComparer.Ordinal)));
    }

    [Fact]
    public void The_label_is_hidden_when_the_display_field_is_sensitive_in_the_active_release()
    {
        Assert.False(ProcessInspectionEndpoints.ShowsSubjectLabel(Entity(sensitive: true), Entity(sensitive: false), readable: null));
    }

    [Fact]
    public void The_label_is_hidden_when_the_display_field_is_sensitive_in_the_pinned_release()
    {
        Assert.False(ProcessInspectionEndpoints.ShowsSubjectLabel(Entity(sensitive: false), Entity(sensitive: true), readable: null));
    }

    [Fact]
    public void The_label_is_hidden_when_the_entity_has_no_display_field()
    {
        Assert.False(ProcessInspectionEndpoints.ShowsSubjectLabel(Entity(sensitive: false, displayField: null), Entity(sensitive: false), readable: null));
    }

    private static EntityModel Entity(bool sensitive, string? displayField = "title") =>
        new()
        {
            Id = Guid.Parse("88888888-8888-4888-8888-888888888801"),
            Name = "Request",
            File = "entities/request.json",
            DisplayField = displayField,
            Fields = [new FieldModel { Name = "title", Type = FieldType.Text, Sensitive = sensitive }],
        };
}
