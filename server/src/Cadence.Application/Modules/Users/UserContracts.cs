using Cadence.Domain.Enums;

namespace Cadence.Application.Modules.Users;

/// <summary>
/// The editable part of a profile.
/// </summary>
/// <remarks>
/// <b>Email is absent, deliberately.</b> It is the Google-owned identity key; changing it would
/// orphan the <c>external_login</c> row and lock the person out of their own account (§5.1).
/// </remarks>
public sealed record UpdateProfileRequest(
    string Name,
    string JobTitle,
    string Department,
    string Timezone,
    string? AvatarUrl);

/// <summary>
/// One user's settings, shaped exactly like the client's <c>Preferences</c>.
/// </summary>
/// <remarks>
/// Replaced wholesale by <c>PUT</c> rather than patched. Preferences are read and written as one
/// document by the settings screen, and a partial update over a nested structure needs a merge
/// semantics nobody agrees on — is an empty array "clear it" or "leave it alone"?
/// </remarks>
public sealed record PreferencesDto(
    ThemeMode Theme,
    bool SidebarCollapsed,
    ViewMode MeetingsView,
    ViewMode KnowledgeView,
    CalendarView CalendarView,
    TasksView TasksView,
    string Language,
    UiDensity Density,
    IReadOnlyList<Guid> RecentMeetingIds,
    IReadOnlyList<string> RecentSearches,
    NotificationPreferencesDto Notifications,
    AiPreferencesDto Ai);

public sealed record NotificationPreferencesDto(
    IReadOnlyList<NotificationKind> InApp,
    IReadOnlyList<NotificationKind> Email);

public sealed record AiPreferencesDto(
    SummaryLength SummaryLength,
    bool AutoSummarise,
    bool AutoExtractActionItems,
    bool RequireActionItemReview,
    string OutputLanguage);

/// <summary>
/// An active sign-in, as shown on the security screen.
/// </summary>
/// <remarks>
/// A session is a refresh-token <i>family</i>, not a single token — rotation replaces the token
/// every 15 minutes while the session continues, so the family is what has a stable identity (§5.2).
/// <para>
/// <c>IsCurrent</c> marks the session making the request, so the UI can label it and avoid offering
/// to revoke the one you are using without warning.
/// </para>
/// </remarks>
public sealed record SessionDto(
    Guid Id,
    string? Device,
    string? IpAddress,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    bool IsCurrent);
