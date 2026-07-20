using Cadence.Domain.Identity;
using Cadence.Domain.Intelligence;
using Cadence.Domain.Meetings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Infrastructure.Persistence.Configurations;

internal sealed class MeetingConfiguration : IEntityTypeConfiguration<Meeting>
{
    public void Configure(EntityTypeBuilder<Meeting> builder)
    {
        builder.Property(meeting => meeting.Title).HasMaxLength(300).IsRequired();
        builder.Property(meeting => meeting.Description).IsRequired();
        builder.Property(meeting => meeting.MeetingUrl).HasMaxLength(2048);

        builder.PrimitiveCollection<List<string>>("_tags")
            .HasColumnName("tags")
            .IsRequired();

        builder.Ignore(meeting => meeting.Tags);

        // Mirrors the entity invariant, so the rule survives a bulk insert or a psql session that
        // never touches the domain model (§3.10).
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_meeting_ends_after_starts", "ends_at > starts_at");
            table.HasCheckConstraint("ck_meeting_duration_non_negative", "duration_seconds >= 0");
        });

        // The default meetings list and the calendar's range scans (§3.6).
        builder.HasIndex(meeting => new { meeting.OrganizationId, meeting.StartsAt })
            .IsDescending(false, true)
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(meeting => new { meeting.OrganizationId, meeting.Status })
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex("_tags").HasMethod("gin");

        builder.HasMany(meeting => meeting.Participants)
            .WithOne()
            .HasForeignKey(participant => participant.MeetingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(meeting => meeting.Bookmarks)
            .WithOne()
            .HasForeignKey(bookmark => bookmark.MeetingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Meeting.Participants))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(Meeting.Bookmarks))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ParticipantConfiguration : IEntityTypeConfiguration<Participant>
{
    public void Configure(EntityTypeBuilder<Participant> builder)
    {
        // Denormalised on purpose: the participant list must still read correctly after someone
        // changes their display name, and external attendees have no user row at all (§3.9).
        builder.Property(participant => participant.Name).HasMaxLength(200).IsRequired();
        builder.Property(participant => participant.Email).HasMaxLength(320).IsRequired();

        builder.HasIndex(participant => new { participant.MeetingId, participant.UserId }).IsUnique();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_participant_talk_time_ratio",
            "talk_time_ratio >= 0 AND talk_time_ratio <= 1"));
    }
}

internal sealed class BookmarkConfiguration : IEntityTypeConfiguration<Bookmark>
{
    public void Configure(EntityTypeBuilder<Bookmark> builder)
    {
        builder.Property(bookmark => bookmark.Label).HasMaxLength(300).IsRequired();

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_bookmark_position_non_negative",
            "at_seconds >= 0"));
    }
}

internal sealed class TranscriptSegmentConfiguration : IEntityTypeConfiguration<TranscriptSegment>
{
    public void Configure(EntityTypeBuilder<TranscriptSegment> builder)
    {
        builder.Property(segment => segment.SpeakerName).HasMaxLength(200).IsRequired();
        builder.Property(segment => segment.Content).IsRequired();

        // The hot read: ordered playback of one meeting's transcript.
        builder.HasIndex(segment => new { segment.MeetingId, segment.StartMs });

        builder.ToTable(table =>
        {
            table.HasCheckConstraint("ck_transcript_segment_confidence", "confidence BETWEEN 0 AND 1");
            table.HasCheckConstraint("ck_transcript_segment_range", "end_ms >= start_ms");
        });

        // A transcript has no meaning without its meeting, so it dies with it. Not soft-deletable
        // for the same reason (§3.7).
        builder.HasOne<Meeting>()
            .WithMany()
            .HasForeignKey(segment => segment.MeetingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(segment => segment.SpeakerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class AiSummaryConfiguration : IEntityTypeConfiguration<AiSummary>
{
    public void Configure(EntityTypeBuilder<AiSummary> builder)
    {
        builder.Property(summary => summary.ExecutiveSummary).IsRequired();
        builder.Property(summary => summary.Model).HasMaxLength(100).IsRequired();

        // jsonb, not a child table: key points are an ordered list read as a whole and never queried
        // individually, so a table would add a join and an ordering column for nothing (§3.9).
        builder.Property<List<string>>("_keyPoints")
            .HasColumnName("key_points")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Ignore(summary => summary.KeyPoints);

        // One summary per meeting; regeneration replaces its content rather than adding a row.
        builder.HasIndex(summary => summary.MeetingId).IsUnique();

        builder.HasOne<Meeting>()
            .WithMany()
            .HasForeignKey(summary => summary.MeetingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(summary => summary.Highlights)
            .WithOne()
            .HasForeignKey(highlight => highlight.SummaryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(AiSummary.Highlights))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SummaryHighlightConfiguration : IEntityTypeConfiguration<SummaryHighlight>
{
    public void Configure(EntityTypeBuilder<SummaryHighlight> builder)
    {
        builder.Property(highlight => highlight.Text).IsRequired();

        // Highlights are a real table rather than jsonb because they are filtered by kind and joined
        // back to the transcript segment that produced them (§3.9).
        builder.HasIndex(highlight => new { highlight.SummaryId, highlight.Kind });

        builder.HasOne<TranscriptSegment>()
            .WithMany()
            .HasForeignKey(highlight => highlight.SourceSegmentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
