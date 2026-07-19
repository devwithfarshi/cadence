namespace Cadence.Application.Common.Models;

/// <summary>
/// The envelope every list endpoint returns.
/// </summary>
/// <remarks>
/// Shaped identically to the client's existing <c>Paginated&lt;T&gt;</c> (blueprint §6.1), so the
/// frontend's table, pagination and empty-state components need no changes when the mock API is
/// swapped for REST. <c>TotalPages</c> is computed rather than stored — two sources of truth for
/// the same number is how off-by-one pagination bugs start.
/// </remarks>
public sealed record PagedResult<T>
{
    public PagedResult(IReadOnlyList<T> items, int total, int page, int pageSize)
    {
        Items = items;
        Total = total;
        Page = page;
        PageSize = pageSize;
    }

    public IReadOnlyList<T> Items { get; init; }

    /// <summary>Total matching rows, ignoring pagination.</summary>
    public int Total { get; init; }

    /// <summary>1-based, matching the client.</summary>
    public int Page { get; init; }

    public int PageSize { get; init; }

    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(Total / (double)PageSize);

    /// <summary>An empty page that still reports the requested paging, so the UI renders correctly.</summary>
    public static PagedResult<T> Empty(int page, int pageSize) => new([], 0, page, pageSize);
}
