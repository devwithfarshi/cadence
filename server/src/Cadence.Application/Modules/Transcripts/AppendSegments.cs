using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Meetings;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Application.Modules.Transcripts;

/// <summary>
/// Persists a batch of live transcript lines.
/// </summary>
/// <remarks>
/// A batch, never a single utterance. A live meeting produces a segment every few seconds per
/// speaker, and one insert plus one transaction per line is write amplification for no benefit
/// (§14.4) — the buffer in the Api layer accumulates them and calls this on a timer.
/// </remarks>
public sealed record AppendSegmentsCommand(
    Guid MeetingId,
    IReadOnlyList<AppendSegmentRequest> Segments)
    : ICommand<Result<IReadOnlyList<TranscriptSegmentDto>>>;

internal sealed class AppendSegmentsValidator : AbstractValidator<AppendSegmentsCommand>
{
    /// <summary>
    /// An upper bound on one batch, so a client cannot turn a flush into an unbounded insert.
    /// </summary>
    public const int MaxBatchSize = 500;

    public AppendSegmentsValidator()
    {
        RuleFor(command => command.Segments)
            .NotEmpty().WithMessage("A batch must contain at least one segment.")
            .Must(segments => segments.Count <= MaxBatchSize)
            .WithMessage($"A batch cannot exceed {MaxBatchSize} segments.");

        RuleForEach(command => command.Segments).ChildRules(segment =>
        {
            segment.RuleFor(item => item.SpeakerName)
                .NotEmpty().WithMessage("Every segment needs a speaker name.")
                .MaximumLength(200);

            segment.RuleFor(item => item.StartMs)
                .GreaterThanOrEqualTo(0)
                .WithMessage("A segment cannot start before the recording.");

            segment.RuleFor(item => item.EndMs)
                .GreaterThanOrEqualTo(item => item.StartMs)
                .WithMessage("A segment cannot end before it starts.");

            segment.RuleFor(item => item.Confidence)
                .InclusiveBetween(0, 1)
                .WithMessage("Confidence must be between 0 and 1.");
        });
    }
}

public sealed class AppendSegmentsHandler(
    ICadenceDbContext context,
    IMeetingBroadcaster broadcaster,
    ILogger<AppendSegmentsHandler> logger)
    : ICommandHandler<AppendSegmentsCommand, Result<IReadOnlyList<TranscriptSegmentDto>>>
{
    public async ValueTask<Result<IReadOnlyList<TranscriptSegmentDto>>> Handle(
        AppendSegmentsCommand command,
        CancellationToken cancellationToken)
    {
        // Through the tenant-filtered set, so a segment cannot be written into another workspace's
        // meeting by id. TranscriptSegment carries no organization_id of its own — it inherits
        // scope from its meeting — which makes this check the whole boundary.
        var meeting = await context.Meetings
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Id == command.MeetingId, cancellationToken);

        if (meeting is null)
        {
            return Result.Failure<IReadOnlyList<TranscriptSegmentDto>>(
                Error.NotFound("meeting.not_found", "That meeting could not be found."));
        }

        if (meeting.Status is MeetingStatus.Completed or MeetingStatus.Cancelled)
        {
            // Refused rather than accepted late. A transcript that keeps growing after the meeting
            // ended would contradict the summary already generated from it.
            return Result.Failure<IReadOnlyList<TranscriptSegmentDto>>(Error.Conflict(
                "transcript.meeting_finished",
                "That meeting has finished and its transcript is closed."));
        }

        var segments = new List<TranscriptSegment>(command.Segments.Count);

        foreach (var request in command.Segments)
        {
            try
            {
                segments.Add(TranscriptSegment.Create(
                    command.MeetingId,
                    request.SpeakerId,
                    request.SpeakerName,
                    request.StartMs,
                    request.EndMs,
                    request.Text,
                    request.Confidence));
            }
            catch (DomainException exception)
            {
                return Result.Failure<IReadOnlyList<TranscriptSegmentDto>>(
                    Error.Validation("transcript.invalid_segment", exception.Message));
            }
        }

        foreach (var (segment, request) in segments.Zip(command.Segments))
        {
            if (request.IsActionItem)
            {
                segment.FlagAsActionItem();
            }
        }

        await context.TranscriptSegments.AddRangeAsync(segments, cancellationToken);
        await context.SaveChangesAsync(cancellationToken);

        var stored = segments
            .OrderBy(segment => segment.StartMs)
            .Select(segment => new TranscriptSegmentDto(
                segment.Id,
                segment.MeetingId,
                segment.SpeakerId,
                segment.SpeakerName,
                segment.StartMs / 1000d,
                segment.EndMs / 1000d,
                segment.Content,
                segment.Confidence,
                segment.IsActionItem))
            .ToList();

        // After the commit, so a client is never shown a line that a rolled-back transaction means
        // never existed. Best-effort: a failed broadcast must not fail the write.
        try
        {
            await broadcaster.SegmentsAppendedAsync(command.MeetingId, stored, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Persisted {Count} segments for meeting {MeetingId} but could not broadcast them",
                stored.Count,
                command.MeetingId);
        }

        return Result.Success<IReadOnlyList<TranscriptSegmentDto>>(stored);
    }
}
