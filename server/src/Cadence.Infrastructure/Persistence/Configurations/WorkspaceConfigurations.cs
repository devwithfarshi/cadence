using Cadence.Domain.Chat;
using Cadence.Domain.Collaboration;
using Cadence.Domain.Identity;
using Cadence.Domain.Integrations;
using Cadence.Domain.Library;
using Cadence.Domain.Meetings;
using Cadence.Domain.Work;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Infrastructure.Persistence.Configurations;

internal sealed class ActionItemConfiguration : IEntityTypeConfiguration<ActionItem>
{
    public void Configure(EntityTypeBuilder<ActionItem> builder)
    {
        builder.Property(item => item.Title).HasMaxLength(300).IsRequired();
        builder.Property(item => item.Description).IsRequired();

        builder.PrimitiveCollection<List<string>>("_tags")
            .HasColumnName("tags")
            .IsRequired();

        builder.Ignore(item => item.Tags);

        // "Assigned to me" — the Tasks default view (§3.6).
        builder.HasIndex(item => new { item.OrganizationId, item.AssigneeId, item.Status })
            .HasFilter("deleted_at IS NULL");

        // Partial, so the overdue index stays small as completed work accumulates.
        builder.HasIndex(item => new { item.OrganizationId, item.DueDate })
            .HasFilter("status <> 'done' AND deleted_at IS NULL");

        // SET NULL, never CASCADE. A commitment outlives the meeting that produced it — deleting a
        // meeting must not silently delete someone's assigned work (§3.8).
        builder.HasOne<Meeting>()
            .WithMany()
            .HasForeignKey(item => item.MeetingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne<TranscriptSegment>()
            .WithMany()
            .HasForeignKey(item => item.SourceSegmentId)
            .OnDelete(DeleteBehavior.SetNull);

        // Removing a member unassigns their tasks; it does not delete the team's work.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(item => item.AssigneeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.Property(document => document.Name).HasMaxLength(500).IsRequired();
        builder.Property(document => document.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(document => document.Url).HasMaxLength(2048).IsRequired();
        builder.Property(document => document.Excerpt).IsRequired();

        builder.PrimitiveCollection<List<string>>("_tags")
            .HasColumnName("tags")
            .IsRequired();

        builder.Ignore(document => document.Tags);

        builder.ToTable(table => table.HasCheckConstraint(
            "ck_document_size_non_negative",
            "size_bytes >= 0"));

        builder.HasIndex(document => new { document.OrganizationId, document.ProcessingStatus })
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(document => document.StorageKey).IsUnique();

        builder.HasOne<Meeting>()
            .WithMany()
            .HasForeignKey(document => document.MeetingId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

internal sealed class KnowledgeItemConfiguration : IEntityTypeConfiguration<KnowledgeItem>
{
    public void Configure(EntityTypeBuilder<KnowledgeItem> builder)
    {
        builder.Property(item => item.Title).HasMaxLength(500).IsRequired();
        builder.Property(item => item.Category).HasMaxLength(100).IsRequired();
        builder.Property(item => item.Excerpt).IsRequired();
        builder.Property(item => item.SourceUrl).HasMaxLength(2048);

        builder.PrimitiveCollection<List<string>>("_tags")
            .HasColumnName("tags")
            .IsRequired();

        builder.Ignore(item => item.Tags);

        builder.HasIndex(item => new { item.OrganizationId, item.Category })
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex("_tags").HasMethod("gin");
    }
}

internal sealed class CommentConfiguration : IEntityTypeConfiguration<Comment>
{
    public void Configure(EntityTypeBuilder<Comment> builder)
    {
        builder.Property(comment => comment.Body).IsRequired();

        builder.PrimitiveCollection<List<Guid>>("_mentions")
            .HasColumnName("mentions")
            .IsRequired();

        builder.Ignore(comment => comment.Mentions);

        builder.HasIndex(comment => comment.MeetingId).HasFilter("deleted_at IS NULL");

        builder.HasOne<Meeting>()
            .WithMany()
            .HasForeignKey(comment => comment.MeetingId)
            .OnDelete(DeleteBehavior.Cascade);

        // One level of threading. Restrict rather than cascade so deleting a parent cannot silently
        // take its replies with it — the module decides what happens to them.
        builder.HasOne<Comment>()
            .WithMany()
            .HasForeignKey(comment => comment.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.Property(notification => notification.Title).HasMaxLength(300).IsRequired();
        builder.Property(notification => notification.Body).IsRequired();
        builder.Property(notification => notification.Href).HasMaxLength(2048);

        // The bell and its unread count — the most frequent read in the app (§3.6).
        builder.HasIndex(notification => new
        {
            notification.UserId,
            notification.IsRead,
            notification.CreatedAt,
        }).IsDescending(false, false, true);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(notification => notification.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ActivityLogConfiguration : IEntityTypeConfiguration<ActivityLog>
{
    public void Configure(EntityTypeBuilder<ActivityLog> builder)
    {
        builder.Property(entry => entry.Summary).HasMaxLength(1000).IsRequired();
        builder.Property(entry => entry.Href).HasMaxLength(2048);

        builder.HasIndex(entry => new { entry.OrganizationId, entry.OccurredAt })
            .IsDescending(false, true);
    }
}

internal sealed class IntegrationConfiguration : IEntityTypeConfiguration<Integration>
{
    public void Configure(EntityTypeBuilder<Integration> builder)
    {
        builder.Property(integration => integration.Key).HasMaxLength(100).IsRequired();
        builder.Property(integration => integration.Name).HasMaxLength(200).IsRequired();
        builder.Property(integration => integration.Description).IsRequired();
        builder.Property(integration => integration.AccountLabel).HasMaxLength(320);
        builder.Property(integration => integration.LastError).HasMaxLength(1000);

        // One connection per provider per workspace.
        builder.HasIndex(integration => new { integration.OrganizationId, integration.Key }).IsUnique();
    }
}

internal sealed class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.Property(conversation => conversation.Title).HasMaxLength(300).IsRequired();

        // The sidebar orders by recency of the last message, not by creation.
        builder.HasIndex(conversation => new { conversation.UserId, conversation.LastMessageAt })
            .IsDescending(false, true)
            .HasFilter("deleted_at IS NULL");

        builder.HasOne<Meeting>()
            .WithMany()
            .HasForeignKey(conversation => conversation.MeetingId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(conversation => conversation.Messages)
            .WithOne()
            .HasForeignKey(message => message.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(Conversation.Messages))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.Property(message => message.Content).IsRequired();

        builder.HasIndex(message => new { message.ConversationId, message.CreatedAt });

        // Citations are stored as jsonb rather than rows: they are only ever read with their message,
        // and a foreign key would block deleting the meeting or document they point at.
        builder.OwnsMany(message => message.Sources, sources =>
        {
            sources.ToJson("sources");
            sources.Property(source => source.Label).HasMaxLength(500);
            sources.Property(source => source.Href).HasMaxLength(2048);
        });

        builder.Metadata.FindNavigation(nameof(ChatMessage.Sources))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}
