namespace Cadence.Application.Common.Models;

/// <summary>A selection of rows to act on, by id.</summary>
public sealed record BulkIdsRequest(IReadOnlyList<Guid> Ids);

/// <summary>
/// How many rows a bulk operation actually changed.
/// </summary>
/// <remarks>
/// A count rather than a per-id result. Ids the caller cannot see are simply not counted, which is
/// the same answer a tenant boundary gives to "does this exist?" — reporting them individually would
/// turn a bulk endpoint into an existence oracle.
/// </remarks>
public sealed record BulkResultDto(int Affected);
