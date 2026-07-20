using Cadence.Domain.Identity;

namespace Cadence.Application.Modules.Users;

internal static class PreferencesMapping
{
    public static PreferencesDto ToDto(this UserPreferences preferences) =>
        new(
            preferences.Theme,
            preferences.SidebarCollapsed,
            preferences.MeetingsView,
            preferences.KnowledgeView,
            preferences.CalendarView,
            preferences.TasksView,
            preferences.Language,
            preferences.Density,
            preferences.RecentMeetingIds,
            preferences.RecentSearches,
            new NotificationPreferencesDto(preferences.Notifications.InApp, preferences.Notifications.Email),
            new AiPreferencesDto(
                preferences.Ai.SummaryLength,
                preferences.Ai.AutoSummarise,
                preferences.Ai.AutoExtractActionItems,
                preferences.Ai.RequireActionItemReview,
                preferences.Ai.OutputLanguage));

    /// <summary>
    /// Applies a submitted document to the stored aggregate.
    /// </summary>
    /// <remarks>
    /// <c>RecentMeetingIds</c> and <c>RecentSearches</c> are deliberately <b>not</b> applied. They
    /// are usage history the server maintains as meetings are opened and searches run; letting a
    /// settings save overwrite them would let a stale tab wind the list back.
    /// </remarks>
    public static void Apply(this UserPreferences preferences, PreferencesDto dto)
    {
        preferences.UpdateAppearance(dto.Theme, dto.Density, dto.Language, dto.SidebarCollapsed);
        preferences.UpdateViews(dto.MeetingsView, dto.KnowledgeView, dto.CalendarView, dto.TasksView);

        preferences.UpdateNotifications(
            NotificationPreferences.Create(dto.Notifications.InApp, dto.Notifications.Email));

        preferences.UpdateAi(AiPreferences.Create(
            dto.Ai.SummaryLength,
            dto.Ai.AutoSummarise,
            dto.Ai.AutoExtractActionItems,
            dto.Ai.RequireActionItemReview,
            dto.Ai.OutputLanguage));
    }
}
