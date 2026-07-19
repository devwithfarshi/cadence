using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Identity;

/// <summary>
/// Per-organization configuration, owned by <see cref="Organization"/>.
/// </summary>
/// <remarks>
/// A value object rather than an entity: it has no identity of its own and is always replaced
/// wholesale, never mutated field by field. Persisted as an owned type on the organization row.
/// </remarks>
public sealed class WorkspaceSettings : ValueObject
{
    // Materialisation constructor for the persistence layer. EF needs a parameterless one it
    // can call before setting the mapped members; the factories below are the only path callers get.
    private WorkspaceSettings() => Name = null!;

    private WorkspaceSettings(string name, MeetingVisibility defaultVisibility, RetentionPeriod retention)
    {
        Name = name;
        DefaultVisibility = defaultVisibility;
        Retention = retention;
    }

    public string Name { get; private set; }

    public MeetingVisibility DefaultVisibility { get; private set; }

    public RetentionPeriod Retention { get; private set; }

    public static WorkspaceSettings Default(string name) =>
        new(name, MeetingVisibility.Workspace, RetentionPeriod.TwelveMonths);

    public static WorkspaceSettings Create(
        string name,
        MeetingVisibility defaultVisibility,
        RetentionPeriod retention)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "Workspace name cannot be empty.");

        return new WorkspaceSettings(name.Trim(), defaultVisibility, retention);
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Name;
        yield return DefaultVisibility;
        yield return Retention;
    }
}
