using Npgsql;

namespace Axis.Data.Records;

/// <summary>
/// A list's filter and search as one SQL condition, and its sort as one ORDER BY term, over the columns of a fixed table.
/// The SQL holds only expressions written in code. Every value of the request is a parameter that <see cref="AddTo"/> adds.
/// </summary>
public sealed class ListSql(string condition, string? order, RecordListFilter? filter, RecordSearch? search)
{
    /// <summary>The condition to put after WHERE, which is <c>TRUE</c> when the list has no filter and no search.</summary>
    public string Condition { get; } = condition;

    /// <summary>The term to put after ORDER BY, such as <c>created_at DESC</c>, or null when the list is not sorted.</summary>
    public string? Order { get; } = order;

    /// <summary>Adds the parameter of every value in <see cref="Condition"/> to <paramref name="command"/>.</summary>
    public void AddTo(NpgsqlCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        filter?.AddTo(command);
        search?.AddTo(command);
    }
}
