using System.Linq.Expressions;

namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// Deferred and recurring work. Implemented on Hangfire.
/// </summary>
/// <remarks>
/// Behind a port so the choice of Hangfire is reversible: moving to a broker later replaces one
/// adapter and leaves every caller alone (§14.1).
/// </remarks>
public interface IJobScheduler
{
    /// <summary>Runs as soon as a worker is free. Returns the provider's job id for correlation.</summary>
    string Enqueue<TJob>(Expression<Func<TJob, Task>> job)
        where TJob : notnull;

    string Schedule<TJob>(Expression<Func<TJob, Task>> job, TimeSpan delay)
        where TJob : notnull;

    /// <summary>
    /// Runs only after <paramref name="parentJobId"/> succeeds — this is what chains the
    /// transcribe → summarise → extract pipeline without a job polling for its predecessor (§14.3).
    /// </summary>
    string ContinueWith<TJob>(string parentJobId, Expression<Func<TJob, Task>> job)
        where TJob : notnull;

    /// <summary>
    /// Registers or updates a cron job. <paramref name="recurringJobId"/> is the identity, so
    /// calling this again on startup updates the schedule instead of creating a duplicate.
    /// </summary>
    void AddOrUpdateRecurring<TJob>(string recurringJobId, Expression<Func<TJob, Task>> job, string cronExpression)
        where TJob : notnull;

    bool Delete(string jobId);
}
