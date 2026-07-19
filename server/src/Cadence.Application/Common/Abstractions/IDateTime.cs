namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// The clock, as a dependency.
/// </summary>
/// <remarks>
/// Every "now" in a handler comes from here so time-dependent behaviour — token expiry, retention
/// purges, "meetings this week" — is testable without waiting for the wall clock or sleeping.
/// Always UTC; the client formats to local time (§3.1).
/// </remarks>
public interface IDateTime
{
    DateTimeOffset UtcNow { get; }

    DateOnly Today { get; }
}
