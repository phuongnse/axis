using System.ComponentModel.DataAnnotations;

namespace Axis.Shared.Application;

public sealed record PagedResult<T>(
    [property: Required]
    IReadOnlyList<T> Items,
    [property: Required]
    int TotalCount,
    [property: Required]
    int Page,
    [property: Required]
    int PageSize);
