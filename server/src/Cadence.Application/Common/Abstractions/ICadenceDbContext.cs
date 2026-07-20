using Cadence.Domain.Chat;
using Cadence.Domain.Collaboration;
using Cadence.Domain.Identity;
using Cadence.Domain.Integrations;
using Cadence.Domain.Intelligence;
using Cadence.Domain.Library;
using Cadence.Domain.Meetings;
using Cadence.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace Cadence.Application.Common.Abstractions;

/// <summary>
/// The database as the Application layer sees it.
/// </summary>
/// <remarks>
/// <para>
/// Reads project straight from these sets with <c>AsNoTracking()</c> and <c>Select</c> rather than
/// going through a repository (§9.1). Forcing every read through a repository is what produces the
/// N+1 and over-fetching problems repositories were meant to prevent — a list endpoint should fetch
/// its own columns and nothing more.
/// </para>
/// <para>
/// An interface rather than the concrete context, so Application still cannot see Infrastructure —
/// an architecture test fails the build if it does. Every set here carries the global tenant and
/// soft-delete filters; <c>IgnoreQueryFilters()</c> is available but is a deliberate, reviewable act.
/// </para>
/// </remarks>
public interface ICadenceDbContext
{
    DbSet<User> Users { get; }

    DbSet<ExternalLogin> ExternalLogins { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<UserPreferences> UserPreferences { get; }

    DbSet<Organization> Organizations { get; }

    DbSet<OrganizationMember> OrganizationMembers { get; }

    DbSet<Invitation> Invitations { get; }

    DbSet<ApiKey> ApiKeys { get; }

    DbSet<Meeting> Meetings { get; }

    DbSet<Participant> Participants { get; }

    DbSet<Bookmark> Bookmarks { get; }

    DbSet<TranscriptSegment> TranscriptSegments { get; }

    DbSet<AiSummary> AiSummaries { get; }

    DbSet<SummaryHighlight> SummaryHighlights { get; }

    DbSet<ActionItem> ActionItems { get; }

    DbSet<Document> Documents { get; }

    DbSet<KnowledgeItem> KnowledgeItems { get; }

    DbSet<Comment> Comments { get; }

    DbSet<Notification> Notifications { get; }

    DbSet<ActivityLog> ActivityLogs { get; }

    DbSet<Integration> Integrations { get; }

    DbSet<Conversation> Conversations { get; }

    DbSet<ChatMessage> ChatMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
