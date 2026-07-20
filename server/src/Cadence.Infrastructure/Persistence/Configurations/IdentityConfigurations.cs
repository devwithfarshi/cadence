using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using Cadence.Infrastructure.Persistence.Converters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(user => user.Email).HasMaxLength(320).IsRequired();
        builder.Property(user => user.Name).HasMaxLength(200).IsRequired();
        builder.Property(user => user.AvatarUrl).HasMaxLength(2048);
        builder.Property(user => user.JobTitle).HasMaxLength(200).IsRequired();
        builder.Property(user => user.Department).HasMaxLength(200).IsRequired();
        builder.Property(user => user.Timezone).HasMaxLength(64).IsRequired();

        // Partial, because the global filter always adds `deleted_at IS NULL` — and because a
        // non-partial unique index would stop a deleted account's address being re-registered (§3.7).
        builder.HasIndex(user => user.Email)
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        // Configured from this side because User owns the navigations. Declaring the same
        // relationship from the child with `HasOne<User>().WithMany()` — no navigation named —
        // makes EF treat it as a *second*, unrelated relationship and quietly add a shadow
        // `user_id1` column alongside the real FK.
        builder.HasMany(user => user.ExternalLogins)
            .WithOne()
            .HasForeignKey(login => login.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(user => user.Memberships)
            .WithOne()
            .HasForeignKey(member => member.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata.FindNavigation(nameof(User.ExternalLogins))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
        builder.Metadata.FindNavigation(nameof(User.Memberships))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class ExternalLoginConfiguration : IEntityTypeConfiguration<ExternalLogin>
{
    public void Configure(EntityTypeBuilder<ExternalLogin> builder)
    {
        builder.Property(login => login.Provider).HasMaxLength(50).IsRequired();
        builder.Property(login => login.Subject).HasMaxLength(255).IsRequired();
        builder.Property(login => login.EmailAtProvider).HasMaxLength(320).IsRequired();

        // The identity key is (provider, subject) — Google's `sub` — never the email. An address can
        // be reassigned inside a Workspace domain; `sub` cannot, so keying on email would eventually
        // hand one person another's account (§4.3).
        builder.HasIndex(login => new { login.Provider, login.Subject }).IsUnique();

        // The relationship to User is declared in UserConfiguration, which owns the navigation.
    }
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(token => token.Device).HasMaxLength(255);
        builder.Property(token => token.IpAddress).HasMaxLength(64);

        builder.HasIndex(token => token.TokenHash).IsUnique();

        // Reuse detection revokes the whole family at once, so the family is the query (§4.5).
        builder.HasIndex(token => token.FamilyId);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.Property(organization => organization.Name).HasMaxLength(200).IsRequired();
        builder.Property(organization => organization.Slug).HasMaxLength(100).IsRequired();

        builder.HasIndex(organization => organization.Slug)
            .IsUnique()
            .HasFilter("deleted_at IS NULL");

        // Owned, not a table: settings have no identity of their own and are always replaced
        // wholesale. They live as columns on `organization`.
        builder.OwnsOne(organization => organization.Settings, settings =>
        {
            settings.Property(value => value.Name).HasColumnName("settings_name").HasMaxLength(200).IsRequired();
            settings.Property(value => value.DefaultVisibility)
                .HasColumnName("settings_default_visibility")
                .HasConversion(new SnakeCaseEnumConverter<MeetingVisibility>())
                .HasMaxLength(50)
                .IsRequired();
            settings.Property(value => value.Retention)
                .HasColumnName("settings_retention")
                .HasConversion(new SnakeCaseEnumConverter<RetentionPeriod>())
                .HasMaxLength(50)
                .IsRequired();
        });

        builder.Navigation(organization => organization.Settings).IsRequired();

        builder.HasMany(organization => organization.Members)
            .WithOne()
            .HasForeignKey(member => member.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Metadata
            .FindNavigation(nameof(Organization.Members))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class OrganizationMemberConfiguration : IEntityTypeConfiguration<OrganizationMember>
{
    public void Configure(EntityTypeBuilder<OrganizationMember> builder)
    {
        builder.HasIndex(member => new { member.OrganizationId, member.UserId }).IsUnique();

        // Both relationships are declared from the principal side — Organization.Members and
        // User.Memberships — for the reason noted in UserConfiguration.
    }
}

internal sealed class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.Property(invitation => invitation.Email).HasMaxLength(320).IsRequired();
        builder.Property(invitation => invitation.TokenHash).HasMaxLength(128).IsRequired();

        builder.HasIndex(invitation => invitation.TokenHash).IsUnique();

        // One live invitation per address per workspace; re-inviting after a revoke must still work,
        // hence the status predicate rather than a plain unique index.
        builder.HasIndex(invitation => new { invitation.OrganizationId, invitation.Email })
            .IsUnique()
            .HasFilter("status = 'pending'");
    }
}

internal sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.Property(key => key.Name).HasMaxLength(200).IsRequired();
        builder.Property(key => key.Prefix).HasMaxLength(32).IsRequired();
        builder.Property(key => key.KeyHash).HasMaxLength(128).IsRequired();

        // Authentication looks a key up by hash on every API-key request.
        builder.HasIndex(key => key.KeyHash).IsUnique();

        // PrimitiveCollection with an ElementType conversion, not Property with a whole-collection
        // converter. EF maps a primitive collection element by element; handing it a converter for
        // the entire collection builds a model fine and then fails on the first write.
        builder.PrimitiveCollection<List<ApiKeyScope>>("_scopes")
            .HasColumnName("scopes")
            .ElementType(element => element.HasConversion(new SnakeCaseEnumConverter<ApiKeyScope>()))
            .IsRequired();

        builder.Ignore(key => key.Scopes);
    }
}

internal sealed class UserPreferencesConfiguration : IEntityTypeConfiguration<UserPreferences>
{
    public void Configure(EntityTypeBuilder<UserPreferences> builder)
    {
        builder.Property(preferences => preferences.Language).HasMaxLength(10).IsRequired();

        // One row per user; the unique index is what makes that a database rule rather than a
        // convention the code happens to follow.
        builder.HasIndex(preferences => preferences.UserId).IsUnique();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(preferences => preferences.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.PrimitiveCollection<List<Guid>>("_recentMeetingIds")
            .HasColumnName("recent_meeting_ids")
            .IsRequired();

        builder.PrimitiveCollection<List<string>>("_recentSearches")
            .HasColumnName("recent_searches")
            .IsRequired();

        builder.Ignore(preferences => preferences.RecentMeetingIds);
        builder.Ignore(preferences => preferences.RecentSearches);

        builder.OwnsOne(preferences => preferences.Notifications, notifications =>
        {
            notifications.PrimitiveCollection(value => value.InApp)
                .HasColumnName("notifications_in_app")
                .ElementType(element => element.HasConversion(new SnakeCaseEnumConverter<NotificationKind>()))
                .IsRequired();

            notifications.PrimitiveCollection(value => value.Email)
                .HasColumnName("notifications_email")
                .ElementType(element => element.HasConversion(new SnakeCaseEnumConverter<NotificationKind>()))
                .IsRequired();
        });

        builder.OwnsOne(preferences => preferences.Ai, ai =>
        {
            ai.Property(value => value.SummaryLength)
                .HasColumnName("ai_summary_length")
                .HasConversion(new SnakeCaseEnumConverter<SummaryLength>())
                .HasMaxLength(50)
                .IsRequired();
            ai.Property(value => value.AutoSummarise).HasColumnName("ai_auto_summarise");
            ai.Property(value => value.AutoExtractActionItems).HasColumnName("ai_auto_extract_action_items");
            ai.Property(value => value.RequireActionItemReview).HasColumnName("ai_require_action_item_review");
            ai.Property(value => value.OutputLanguage)
                .HasColumnName("ai_output_language")
                .HasMaxLength(10)
                .IsRequired();
        });

        builder.Navigation(preferences => preferences.Notifications).IsRequired();
        builder.Navigation(preferences => preferences.Ai).IsRequired();
    }

}
