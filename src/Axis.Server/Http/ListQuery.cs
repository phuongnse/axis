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
/// <paramref name="Sql"/> is the column's SQL expression in a fixed table, for a list that <see cref="ListQuery{T}.Sql"/> renders, and
/// <paramref name="OrderSql"/> is the one a sort uses when it differs. Both are written in code and never come from a request.
/// </summary>
internal sealed record ListColumn<T>(
    string Name,
    ListColumnKind Kind,
    Func<T, object?> Value,
    bool Searchable = false,
    IReadOnlyList<string>? Values = null,
    Func<T, IComparable?>? Order = null,
    string? Sql = null,
    string? OrderSql = null);

/// <summary>One page of a list, with the number of rows that matched before it was cut.</summary>
internal sealed record ListPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, long TotalCount);

/// <summary>
/// The <c>page</c>, <c>pageSize</c>, <c>sort</c>, <c>search</c> and <c>filter</c> of a list, checked against the
/// columns that the list declares and applied to rows held in memory, or rendered as SQL over the columns of a fixed table
/// by <see cref="Sql"/>. The format is the record API's, see
/// <c>docs/reference/record-api.md#paging-and-sorting</c>. The filter is parsed by <see cref="RecordListFilter"/>, over the
/// columns turned into an entity, so the operators have one definition. A sort or a filter on a column the list does
/// not declare is an error, and so is a search on a list with no searchable column.
/// </summary>
internal sealed class ListQuery<T>
{
    private readonly IReadOnlyList<ListColumn<T>> _columns;

    private readonly EntityModel _entity;

    private readonly ListColumn<T>? _sortColumn;

    private readonly bool _descending;

    private readonly string? _search;

    private readonly RecordListFilter? _filter;

    private ListQuery(
        IReadOnlyList<ListColumn<T>> columns,
        EntityModel entity,
        int page,
        int pageSize,
        ListColumn<T>? sortColumn,
        bool descending,
        string? search,
        RecordListFilter? filter)
    {
        _columns = columns;
        _entity = entity;
        Page = page;
        PageSize = pageSize;
        _sortColumn = sortColumn;
        _descending = descending;
        _search = search;
        _filter = filter;
    }

    /// <summary>The page to read, from 1.</summary>
    public int Page { get; }

    /// <summary>The number of rows on a page.</summary>
    public int PageSize { get; }

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

        return new ListQuery<T>(columns, entity, page, pageSize, sortColumn, descending, search, filter);
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

        var items = matched.Skip((int)Math.Min((long)(Page - 1) * PageSize, int.MaxValue)).Take(PageSize).ToList();
        return new ListPage<T>(items, Page, PageSize, matched.Count);
    }

    /// <summary>
    /// Renders the filter and the search as one condition and the sort as one ORDER BY term, over the SQL expressions that the
    /// columns declare. Every column that the request names must declare <see cref="ListColumn{T}.Sql"/>. A search looks in the searchable
    /// columns. The paging is <see cref="Page"/> and <see cref="PageSize"/>.
    /// </summary>
    public ListSql Sql()
    {
        string Expression(FieldModel field) =>
            _columns.First(column => column.Name == field.Name).Sql
            ?? throw new InvalidOperationException($"The column '{field.Name}' declares no SQL expression.");

        var terms = new List<string>();
        if (_filter is { } filter)
        {
            terms.Add(filter.Sql(Expression));
        }

        RecordSearch? search = null;
        if (_search is { } text)
        {
            var searched = _entity.Fields.Where(field => _columns.First(column => column.Name == field.Name).Searchable).ToList();
            search = new RecordSearch(text, searched);
            terms.Add(search.Sql(Expression));
        }

        string? order = null;
        if (_sortColumn is { } sortColumn)
        {
            var expression = sortColumn.OrderSql ?? sortColumn.Sql
                ?? throw new InvalidOperationException($"The column '{sortColumn.Name}' declares no SQL expression.");
            order = $"{expression} {(_descending ? "DESC" : "ASC")}";
        }

        return new ListSql(terms.Count == 0 ? "TRUE" : string.Join(" AND ", terms), order, _filter, search);
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
