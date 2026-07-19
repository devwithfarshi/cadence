using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Identity;

/// <summary>
/// One user's settings, shared across every device they sign in on.
/// </summary>
/// <remarks>
/// <para>
/// A separate row rather than columns on <c>users</c>: preferences are written far more often than
/// the identity record (every sidebar toggle) and are read on a different path, so keeping them
/// apart avoids churning the row that authentication depends on.
/// </para>
/// <para>
/// Global to the user, not per-organization. Someone who belongs to two workspaces expects dark mode
/// in both; per-workspace preferences would be surprising and would multiply the write path.
/// </para>
/// </remarks>
public sealed class UserPreferences : Entity
{
    private const int MaxRecentMeetings = 10;
    private const int MaxRecentSearches = 8;

    private readonly List<Guid> _recentMeetingIds = [];
    private readonly List<string> _recentSearches = [];

    private UserPreferences()
    {
        Language = null!;
        Notifications = null!;
        Ai = null!;
    }

    private UserPreferences(Guid userId)
    {
        UserId = userId;
        Theme = ThemeMode.System;
        MeetingsView = ViewMode.List;
        KnowledgeView = ViewMode.Grid;
        CalendarView = CalendarView.Month;
        TasksView = TasksView.List;
        Language = "en";
        Density = UiDensity.Comfortable;
        Notifications = NotificationPreferences.Default();
        Ai = AiPreferences.Default();
    }

    public Guid UserId { get; private set; }

    public ThemeMode Theme { get; private set; }

    public bool SidebarCollapsed { get; private set; }

    public ViewMode MeetingsView { get; private set; }

    public ViewMode KnowledgeView { get; private set; }

    public CalendarView CalendarView { get; private set; }

    public TasksView TasksView { get; private set; }

    public string Language { get; private set; }

    public UiDensity Density { get; private set; }

    public NotificationPreferences Notifications { get; private set; }

    public AiPreferences Ai { get; private set; }

    /// <summary>Most recent first, capped — this is a shortcut list, not a history.</summary>
    public IReadOnlyList<Guid> RecentMeetingIds => _recentMeetingIds.AsReadOnly();

    public IReadOnlyList<string> RecentSearches => _recentSearches.AsReadOnly();

    public static UserPreferences CreateDefault(Guid userId) => new(userId);

    public void UpdateAppearance(
        ThemeMode theme,
        UiDensity density,
        string language,
        bool sidebarCollapsed)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(language), "Language is required.");

        Theme = theme;
        Density = density;
        Language = language.Trim();
        SidebarCollapsed = sidebarCollapsed;
    }

    public void UpdateViews(
        ViewMode meetingsView,
        ViewMode knowledgeView,
        CalendarView calendarView,
        TasksView tasksView)
    {
        MeetingsView = meetingsView;
        KnowledgeView = knowledgeView;
        CalendarView = calendarView;
        TasksView = tasksView;
    }

    public void UpdateNotifications(NotificationPreferences notifications) =>
        Notifications = notifications;

    public void UpdateAi(AiPreferences ai) => Ai = ai;

    public void RecordMeetingVisit(Guid meetingId) =>
        PushCapped(_recentMeetingIds, meetingId, MaxRecentMeetings);

    public void RecordSearch(string term)
    {
        var trimmed = term.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        PushCapped(_recentSearches, trimmed, MaxRecentSearches);
    }

    public void ClearRecentSearches() => _recentSearches.Clear();

    /// <summary>Move-to-front with a cap: re-visiting an entry promotes it rather than duplicating it.</summary>
    private static void PushCapped<T>(List<T> list, T value, int cap)
    {
        list.Remove(value);
        list.Insert(0, value);

        if (list.Count > cap)
        {
            list.RemoveRange(cap, list.Count - cap);
        }
    }
}
