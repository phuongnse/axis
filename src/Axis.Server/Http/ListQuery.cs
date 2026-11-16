using Axis.Configuration.Model;
using Axis.Data.Records;

namespace Axis.Server.Http;

/// <summary>The kind of a column of a list. It decides which filter operators the column takes.</summary>
internal enum ListColumnKind
{
    Text,
    Enum,
    Number,
    Date,
}

/// <summary>
/// One declared column of a list. <paramref name="Value"/> gives a row's value: a string for text and enum, a number
/// for number and a <see cref="DateOnly"/> for date, or null for none. <paramref name="Order"/> gives the value that a sort
/// uses when it differs from the one a filter compares, as a time sorts by its instant and filters by its day.
/// </summary>
internal sealed record ListColumn<T>(
    string Name,
    ListColumnKind Kind,
    Func<T, object?> Value,
    bool Searchable = false,
    IReadOnlyList<string>? Values = null,
    Func<T, IComparable?>? Order = null);

/// <summary>One page of a list, with the number of rows that matched before it was cut.</summary>
internal sealed record ListPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);

/// <summary>
/// The <c>page</c>, <c>pageSize</c>, <c>sort</c>, <c>search</c> and <c>filter</c> of a list, checked against the
/// columns that the list declares and applied to rows held in memory. The format is the record API's, see
/// <c>docs/reference/record-api.md#paging-and-sorting</c>. The filter is parsed by <see cref="RecordListFilter"/>, over the
/// columns turned into an entity, so the operators have one definition. A sort or a filter on a column the list does
/// not declare is an error, and so is a search on a list with no searchable column.
/// </summary>
internal sealed class ListQuery<T>
{
    private readonly IReadOnlyList<ListColumn<T>> _columns;

    private readonly int _page;

    private readonly int _pageSize;

    private readonly ListColumn<T>? _sortColumn;

    private readonly bool _descending;

    private readonly string? _search;

    private readonly RecordListFilter? _filter;

    private ListQuery(
        IReadOnlyList<ListColumn<T>> columns,
        int page,
        int pageSize,
        ListColumn<T>? sortColumn,
        bool descending,
        string? search,
        RecordListFilter? filter)
    {
        _columns = columns;
        _page = page;
        _pageSize = pageSize;
        _sortColumn = sortColumn;
        _descending = descending;
        _search = search;
        _filter = filter;
    }

    /// <summary>
    /// Reads the five parameters of <paramref name="request"/>. Each invalid one adds its message to <paramref name="errors"/>
    /// under its name, and the query that comes back then must not be applied.
    /// </summary>
    public static ListQuery<T> Read(
        HttpRequest request, IReadOnlyList<ListColumn<T>> columns, SortedDictionary<string, string[]> errors)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(errors);

        var entity = Entity(columns);
        PagingQuery.TryRead(request.Query["page"], request.Query["pageSize"], errors, out var page, out var pageSize);

        ListColumn<T>? sortColumn = null;
        var descending = false;
        if (request.Query["sort"] is { Count: > 0 } sort)
        {
            if (sort.Count == 1 && RecordSort.TryParse(sort[0]!, entity, out var order))
            {
                sortColumn = columns.First(column => column.Name == order.Field.Name);
                descending = order.Descending;
            }
            else
            {
                errors["sort"] = ["Must be a declared field name that is not sensitive, optionally preceded by '-'."];
            }
        }

        string? search = null;
        if (SearchQuery.IsRepeated(request))
        {
            errors["search"] = SearchQuery.RepeatedError;
        }
        else if (request.Query["search"].ToString() is { Length: > 0 } text)
        {
            if (!columns.Any(column => column.Searchable))
            {
                errors["search"] = ["The list has no column to search."];
            }
            else if (SearchQuery.InvalidError(text) is { } invalid)
            {
                errors["search"] = invalid;
            }
            else
            {
                search = text;
            }
        }

        RecordListFilter? filter = null;
        if (request.Query["filter"].Count > 1)
        {
            errors["filter"] = SearchQuery.RepeatedError;
        }
        else if (request.Query["filter"].ToString() is { Length: > 0 } json
            && !RecordListFilter.TryParse(json, entity, _ => true, out filter, out var message))
        {
            errors["filter"] = [message];
        }

        return new ListQuery<T>(columns, page, pageSize, sortColumn, descending, search, filter);
    }

    /// <summary>Filters, searches, sorts and pages <paramref name="rows"/>. A page past the end holds no items.</summary>
    public ListPage<T> Apply(IReadOnlyList<T> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        IEnumerable<T> kept = rows;
        if (_filter is { } filter)
        {
            kept = kept.Where(row => filter.Matches(field => _columns.First(column => column.Name == field.Name).Value(row)));
        }

        if (_search is { } search)
        {
            var searched = _columns.Where(column => column.Searchable).ToList();
            kept = kept.Where(row => searched.Any(column =>
                column.Value(row) is string text && text.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        var matched = kept.ToList();
        if (_sortColumn is { } sortColumn)
        {
            IComparable? Key(T row) => sortColumn.Order is { } order ? order(row) : (IComparable?)sortColumn.Value(row);

            // The sorts are stable, so rows with an equal key keep the list's own order. A null sorts last ascending.
            matched = _descending
                ? [.. matched.OrderByDescending(Key, KeyComparer.Instance)]
                : [.. matched.OrderBy(Key, KeyComparer.Instance)];
        }

        var items = matched.Skip((int)Math.Min((long)(_page - 1) * _pageSize, int.MaxValue)).Take(_pageSize).ToList();
        return new ListPage<T>(items, _page, _pageSize, matched.Count);
    }

    // The columns as an entity, so the record API's parsers read them. A number is a decimal, which holds every number.
    private static EntityModel Entity(IReadOnlyList<ListColumn<T>> columns) => new()
    {
        Id = Guid.Empty,
        Name = "List",
        File = "List",
        Fields =
        [
            .. columns.Select(column => new FieldModel
            {
                Name = column.Name,
                Type = column.Kind switch
                {
                    ListColumnKind.Text => FieldType.Text,
                    ListColumnKind.Enum => FieldType.Enum,
                    ListColumnKind.Number => FieldType.Decimal,
                    _ => FieldType.Date,
                },
                Values = column.Kind == ListColumnKind.Enum ? column.Values : null,
            }),
        ],
    };

    // Strings compare by the invariant culture, other keys by their own order, and null is the largest.
    private sealed class KeyComparer : IComparer<IComparable?>
    {
        public static readonly KeyComparer Instance = new();

        public int Compare(IComparable? x, IComparable? y) => (x, y) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            (string left, string right) => StringComparer.InvariantCulture.Compare(left, right),
            _ => x.CompareTo(y),
        };
    }
}
