using System.Linq.Expressions;
using Cadence.Application.Common.Abstractions;
using Hangfire;

namespace Cadence.Infrastructure.Jobs;

/// <summary>
/// The Hangfire adapter for <see cref="IJobScheduler"/> (§14.1).
/// </summary>
/// <remarks>
/// A thin translation on purpose. Every method maps to one Hangfire call, so swapping Hangfire for a
/// broker later replaces this file and nothing else — which is the whole reason callers depend on
/// the port rather than on <c>BackgroundJob</c> directly.
/// </remarks>
public sealed class HangfireJobScheduler(
    IBackgroundJobClient jobs,
    IRecurringJobManager recurring)
    : IJobScheduler
{
    public string Enqueue<TJob>(Expression<Func<TJob, Task>> job)
        where TJob : notnull =>
        jobs.Enqueue(job);

    public string Schedule<TJob>(Expression<Func<TJob, Task>> job, TimeSpan delay)
        where TJob : notnull =>
        jobs.Schedule(job, delay);

    /// <summary>
    /// Chains a job to run after another succeeds.
    /// </summary>
    /// <remarks>
    /// This is what makes the pipeline a pipeline: summarise runs when transcribe has <i>succeeded</i>,
    /// rather than polling for its output or being fired hopefully alongside it (§14.3).
    /// </remarks>
    public string ContinueWith<TJob>(string parentJobId, Expression<Func<TJob, Task>> job)
        where TJob : notnull =>
        jobs.ContinueJobWith(parentJobId, job);

    public void AddOrUpdateRecurring<TJob>(
        string recurringJobId,
        Expression<Func<TJob, Task>> job,
        string cronExpression)
        where TJob : notnull =>
        recurring.AddOrUpdate(recurringJobId, job, cronExpression);

    public bool Delete(string jobId) => jobs.Delete(jobId);
}
