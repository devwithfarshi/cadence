using Cadence.Domain.Enums;

namespace Cadence.Application.Modules.Organizations;

/// <summary>
/// A workspace, as the switcher and the team screen render it.
/// </summary>
/// <remarks>
/// <c>MemberCount</c> rather than the client mock's <c>memberIds</c> array. The only thing the UI
/// does with that array is take its length, and shipping every member id of every workspace to
/// render "12 members" is an unbounded payload for a number. This is a deliberate, documented
/// divergence from the mock shape — see the note in <c>PROGRESS.md</c>.
/// <para>
/// <c>IsCurrent</c> is derived from the caller's token, never stored. "Current" is a property of the
/// session asking the question, not of the row: two devices signed in to different workspaces would
/// otherwise fight over one boolean.
/// </para>
/// </remarks>
public sealed record OrganizationDto(
    Guid Id,
    string Name,
    string Slug,
    OrganizationPlan Plan,
    Guid OwnerId,
    int MemberCount,
    bool IsCurrent,
    UserRole Role,
    DateTimeOffset CreatedAt);

public sealed record CreateOrganizationRequest(string Name);

public sealed record RenameOrganizationRequest(string Name);

/// <summary>
/// Per-workspace configuration.
/// </summary>
/// <remarks>
/// A full replace like preferences, for the same reason: it is one small document the settings form
/// reads and writes whole, and partial updates over it need merge semantics nobody agrees on.
/// </remarks>
public sealed record WorkspaceSettingsDto(
    string Name,
    MeetingVisibility DefaultVisibility,
    RetentionPeriod Retention);

/// <summary>
/// What an admin may change about someone's membership.
/// </summary>
/// <remarks>
/// Both nullable, and both optional: the team screen changes a role or a status, never both at once,
/// and an absent field means "leave it alone" rather than "clear it".
/// </remarks>
public sealed record UpdateMemberRequest(UserRole? Role, UserStatus? Status);

public sealed record InviteMemberRequest(string Email, UserRole Role);

/// <summary>
/// A pending invitation.
/// </summary>
/// <remarks>
/// The token is absent, and not by omission: it is stored only as a hash, and the plaintext exists
/// exactly once, in the email. A list endpoint that returned it would let any admin redeem an
/// invitation addressed to someone else.
/// </remarks>
public sealed record InvitationDto(
    Guid Id,
    string Email,
    UserRole Role,
    InvitationStatus Status,
    Guid InvitedById,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);
