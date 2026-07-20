using Cadence.Domain.Enums;

namespace Cadence.Application.Modules.Auth;

/// <summary>The Google ID token obtained in the browser via Google Identity Services.</summary>
public sealed record GoogleSignInRequest(string IdToken);

/// <summary>
/// What a successful sign-in or refresh returns.
/// </summary>
/// <remarks>
/// The refresh token is <b>not</b> in the body — it travels as an <c>HttpOnly</c> cookie the browser
/// cannot read, which is what keeps a cross-site scripting flaw from turning into a stolen session.
/// The access token goes in the body precisely so the client holds it in memory only, never in
/// <c>localStorage</c> (§4.3).
/// </remarks>
public sealed record AuthResponse(string AccessToken, int ExpiresIn, UserDto User);

/// <summary>
/// A user as the client renders them.
/// </summary>
/// <remarks>
/// Mirrors the client's <c>User</c> shape, so the mock layer can be swapped for HTTP without a
/// component changing (§6). <c>Role</c> is the caller's role in the <i>current</i> workspace, not a
/// global property of the person.
/// </remarks>
public sealed record UserDto(
    Guid Id,
    string Email,
    string Name,
    string? AvatarUrl,
    string JobTitle,
    string Department,
    string Timezone,
    UserStatus Status,
    UserRole Role,
    Guid OrganizationId,
    DateTimeOffset LastActiveAt);

/// <summary>
/// Where a refresh token was issued and last used.
/// </summary>
/// <remarks>
/// Captured so the sessions list is meaningful — "Chrome on macOS, London, 2 minutes ago" is what
/// lets someone recognise a session that is not theirs (§5.2).
/// </remarks>
public sealed record SessionContext(string? Device, string? IpAddress);
