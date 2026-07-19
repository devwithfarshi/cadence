using System.ComponentModel.DataAnnotations;

namespace Cadence.Infrastructure.Configuration;

/// <summary>
/// Database settings, bound from the <c>Database</c> section and validated at startup.
/// </summary>
/// <remarks>
/// Validated with <c>ValidateOnStart()</c> so a missing or malformed value stops the process at boot
/// with a clear message, rather than surfacing as a confusing 500 on the first request an hour later
/// (§11.2).
/// </remarks>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>
    /// Whether to apply pending migrations during startup.
    /// </summary>
    /// <remarks>
    /// A convenience for local development only. <b>In production, migrations are a gated job that
    /// runs before the rolling deploy</b> (§8.4) — automatic startup migration means every replica
    /// races to migrate, and a failed migration takes down the whole deployment instead of one job.
    /// </remarks>
    public bool MigrateOnStartup { get; init; }

    /// <summary>Seconds before a command is cancelled.</summary>
    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// How many times a transient failure is retried by the execution strategy.
    /// </summary>
    /// <remarks>
    /// Retries apply to transient faults only (connection resets, failovers). They are what makes
    /// <c>ExecuteInTransactionAsync</c> safe to replay, which is why the transaction is opened
    /// <i>inside</i> the strategy rather than around it.
    /// </remarks>
    [Range(0, 10)]
    public int MaxRetryCount { get; init; } = 3;

    /// <summary>
    /// Logs parameter values and full SQL. <b>Never enable outside development</b> — transcripts and
    /// summaries are customer content, and this puts them in the log stream.
    /// </summary>
    public bool EnableSensitiveDataLogging { get; init; }
}
