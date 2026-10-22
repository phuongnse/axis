using Axis.Configuration.Resources;

namespace Axis.Configuration.Model;

/// <summary>
/// A compiled widget. On a table or form widget, exactly one of <see cref="Entity"/> and
/// <see cref="DataSource"/> is set: the widget shows all records of an entity, or the rows of a data
/// source. A task inbox lists the signed-in user's tasks, so every reference on it is null. <see cref="Form"/> is set
/// only on a form widget that names a form, and <see cref="Entity"/> is then the form's entity.
/// <see cref="Actions"/> is empty on every widget but a form widget that declares
/// actions, and <see cref="RowActions"/> and <see cref="ToolbarActions"/> are empty on every widget but a table
/// that declares them. <see cref="Process"/> is set only on a process inspector, which has no other reference.
/// A <c>columns</c>, <c>tabs</c> or <c>section</c> widget is a container with no reference of its own: it holds
/// <see cref="Columns"/>, <see cref="Tabs"/> or <see cref="Widgets"/>, and every other widget leaves those empty.
/// Records compare lists by reference, so two widgets with containers are equal only when they share those lists.
/// </summary>
public sealed record WidgetModel(
    WidgetType Type,
    EntityReference? Entity,
    DataSourceReference? DataSource = null,
    FormReference? Form = null,
    ProcessReference? Process = null)
{
    /// <summary>The <c>startProcess</c> and <c>navigate</c> actions of a form widget, in page file order.</summary>
    public IReadOnlyList<WidgetActionModel> Actions { get; init; } = [];

    /// <summary>The <c>navigate</c> actions a table widget shows on each row, in page file order.</summary>
    public IReadOnlyList<WidgetActionModel> RowActions { get; init; } = [];

    /// <summary>The <c>navigate</c> actions a table widget shows above its rows, in page file order.</summary>
    public IReadOnlyList<WidgetActionModel> ToolbarActions { get; init; } = [];

    /// <summary>The columns of a <c>columns</c> widget, in page file order.</summary>
    public IReadOnlyList<WidgetColumnModel> Columns { get; init; } = [];

    /// <summary>The tabs of a <c>tabs</c> widget, in page file order.</summary>
    public IReadOnlyList<WidgetTabModel> Tabs { get; init; } = [];

    /// <summary>The title of a <c>section</c> widget, or null on any other widget.</summary>
    public TextReference? Title { get; init; }

    /// <summary>Whether a <c>section</c> widget starts collapsed.</summary>
    public bool Collapsed { get; init; }

    /// <summary>The widgets of a <c>section</c> widget, in page file order.</summary>
    public IReadOnlyList<WidgetModel> Widgets { get; init; } = [];
}

/// <summary>One column of a <c>columns</c> widget. The widths of a widget's columns add up to 12.</summary>
public sealed record WidgetColumnModel(int Width, IReadOnlyList<WidgetModel> Widgets);

/// <summary>One tab of a <c>tabs</c> widget.</summary>
public sealed record WidgetTabModel(TextReference Label, IReadOnlyList<WidgetModel> Widgets);
