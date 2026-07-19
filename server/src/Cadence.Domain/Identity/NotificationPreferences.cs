using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Identity;

/// <summary>
/// Which notification kinds reach the user, and through which channel.
/// </summary>
/// <remarks>
/// Stored as two allow-lists rather than a per-kind flag matrix. A new
/// <see cref="NotificationKind"/> then defaults to off for existing users instead of silently
/// opting everyone into a channel they never chose — the safe direction for anything that sends
/// email.
/// </remarks>
public sealed class NotificationPreferences : ValueObject
{
    // Materialisation constructor for the persistence layer. EF needs a parameterless one it
    // can call before setting the mapped members; the factories below are the only path callers get.
    private NotificationPreferences()
    {
        InApp = null!;
        Email = null!;
    }

    private NotificationPreferences(
        IReadOnlyCollection<NotificationKind> inApp,
        IReadOnlyCollection<NotificationKind> email)
    {
        InApp = inApp;
        Email = email;
    }

    public IReadOnlyCollection<NotificationKind> InApp { get; private set; }

    public IReadOnlyCollection<NotificationKind> Email { get; private set; }

    public static NotificationPreferences Default() =>
        new(
            Enum.GetValues<NotificationKind>(),
            [
                NotificationKind.SummaryReady,
                NotificationKind.TaskAssigned,
                NotificationKind.Mention,
            ]);

    public static NotificationPreferences Create(
        IEnumerable<NotificationKind> inApp,
        IEnumerable<NotificationKind> email) =>
        new([.. inApp.Distinct()], [.. email.Distinct()]);

    public bool Allows(NotificationKind kind, bool viaEmail) =>
        viaEmail ? Email.Contains(kind) : InApp.Contains(kind);

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var kind in InApp.Order())
        {
            yield return kind;
        }

        yield return "|";

        foreach (var kind in Email.Order())
        {
            yield return kind;
        }
    }
}
