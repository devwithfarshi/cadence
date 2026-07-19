namespace Cadence.Application.Common.Models;

/// <summary>
/// The paging and sorting parameters shared by every list endpoint.
/// </summary>
/// <remarks>
/// Mirrors the client's <c>ListQuery</c> (§6.1). Module-specific queries inherit this and add their
/// own filters, so paging behaves identically everywhere instead of being re-implemented per module.
/// </remarks>
public abstract record ListQuery
{
    /// <summary>Above this, a "page" is a table scan wearing a disguise.</summary>
    public const int MaxPageSize = 100;

    public const int DefaultPageSize = 20;

    private readonly int _page = 1;
    private readonly int _pageSize = DefaultPageSize;

    /// <summary>Free-text search. Interpretation is the module's business.</summary>
    public string? Search { get; init; }

    public string? SortBy { get; init; }

    public SortDirection SortDir { get; init; } = SortDirection.Desc;

    /// <summary>1-based. Values below 1 are clamped rather than rejected — a bad page number is
    /// not worth a 400, and clamping keeps the response shape predictable.</summary>
    public int Page
    {
        get => _page;
        init => _page = value < 1 ? 1 : value;
    }

    /// <summary>Clamped to <see cref="MaxPageSize"/>, so no caller can ask for the whole table.</summary>
    public int PageSize
    {
        get => _pageSize;
        init => _pageSize = value switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => value,
        };
    }

    /// <summary>Rows to skip. Derived so no handler computes it by hand.</summary>
    public int Skip => (Page - 1) * PageSize;
}

public enum SortDirection
{
    Asc,
    Desc,
}
