using Cadence.Application.Common.Abstractions;

namespace Cadence.Infrastructure.Services;

/// <summary>The real clock. Tests substitute a fixed one.</summary>
public sealed class SystemDateTime : IDateTime
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    /// <summary>
    /// Derived from UTC, not from the server's local date. A server in UTC+13 would otherwise
    /// report "today" as tomorrow for most of its users.
    /// </summary>
    public DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);
}
