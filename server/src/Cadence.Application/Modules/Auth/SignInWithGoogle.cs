using Cadence.Application.Common.Abstractions;
using Cadence.Application.Common.Models;
using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using FluentValidation;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Cadence.Application.Modules.Auth;

/// <summary>
/// Exchanges a verified Google ID token for a Cadence session.
/// </summary>
/// <remarks>
/// The only way into the system. There is no password grant, so there is no password hash, no reset
/// token and no credential-stuffing surface (§4.1).
/// </remarks>
public sealed record SignInWithGoogleCommand(string IdToken, SessionContext Session)
    : ICommand<Result<AuthResult>>;

/// <summary>Everything the endpoint needs to answer a sign-in or refresh.</summary>
/// <remarks>
/// <c>RefreshToken</c> is carried here so the endpoint can set the cookie. It is never serialised
/// into the response body — that is the whole point of an HttpOnly cookie.
/// </remarks>
public sealed record AuthResult(
    AuthResponse Response,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt);

internal sealed class SignInWithGoogleValidator : AbstractValidator<SignInWithGoogleCommand>
{
    public SignInWithGoogleValidator() =>
        RuleFor(command => command.IdToken)
            .NotEmpty()
            .WithMessage("A Google ID token is required.");
}

public sealed class SignInWithGoogleHandler(
    ICadenceDbContext context,
    IGoogleIdTokenValidator googleValidator,
    ITokenService tokens,
    IDateTime clock,
    ILogger<SignInWithGoogleHandler> logger)
    : ICommandHandler<SignInWithGoogleCommand, Result<AuthResult>>
{
    public async ValueTask<Result<AuthResult>> Handle(
        SignInWithGoogleCommand command,
        CancellationToken cancellationToken)
    {
        var identity = await googleValidator.ValidateAsync(command.IdToken, cancellationToken);

        if (identity is null)
        {
            // Deliberately vague. Distinguishing "expired" from "wrong audience" from "bad
            // signature" tells a prober how far they got.
            return Result.Failure<AuthResult>(
                Error.Unauthorized("auth.invalid_token", "That Google sign-in could not be verified."));
        }

        var resolved = await ResolveUserAsync(identity, cancellationToken);

        if (resolved.IsFailure)
        {
            return Result.Failure<AuthResult>(resolved.Error);
        }

        var (user, provisioned) = resolved.Value;

        if (user.Status == UserStatus.Suspended)
        {
            return Result.Failure<AuthResult>(
                Error.Forbidden("auth.suspended", "This account has been suspended."));
        }

        // A membership created moments ago is tracked but not yet in the database, and a query
        // would go to the database and find nothing. So the freshly provisioned one is carried
        // through rather than looked up.
        var membership = provisioned ?? await context.OrganizationMembers
            .IgnoreQueryFilters()
            .Where(member => member.UserId == user.Id)
            .OrderBy(member => member.JoinedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (membership is null)
        {
            // Every user has at least their own workspace, created on first sign-in, so this means
            // the account was left unusable rather than provisioning failing loudly.
            logger.LogError("User {UserId} signed in with no organization membership", user.Id);

            return Result.Failure<AuthResult>(
                Error.Failure("auth.no_workspace", "This account has no workspace."));
        }

        user.Touch();

        var session = await IssueSessionAsync(user, membership, command.Session, cancellationToken);

        await context.SaveChangesAsync(cancellationToken);

        return Result.Success(session);
    }

    /// <summary>
    /// Finds the account this Google identity belongs to, creating one on first sign-in.
    /// </summary>
    /// <returns>
    /// The user, plus their membership when it was just created — see the note at the call site.
    /// </returns>
    private async Task<Result<(User User, OrganizationMember? Provisioned)>> ResolveUserAsync(
        GoogleIdentity identity,
        CancellationToken cancellationToken)
    {
        // Keyed on Google's `sub`, never the email. An address can be reassigned within a Workspace
        // domain; `sub` cannot, so matching on email would eventually hand over the wrong account.
        var linked = await context.ExternalLogins
            .IgnoreQueryFilters()
            .Where(login => login.Provider == "google" && login.Subject == identity.Subject)
            .Select(login => login.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (linked != Guid.Empty)
        {
            var existing = await context.Users
                .IgnoreQueryFilters()
                .FirstAsync(user => user.Id == linked, cancellationToken);

            // Google is the source of truth for these, so a changed display name or photo follows.
            existing.UpdateAvatar(identity.PictureUrl);
            return Result.Success<(User, OrganizationMember?)>((existing, null));
        }

        var byEmail = await context.Users
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(user => user.Email == identity.Email, cancellationToken);

        if (byEmail is not null)
        {
            // Linking an existing account by address is gated on email_verified. Skipping that check
            // is a known account-takeover vector: anyone able to mint an unverified token for
            // victim@corp.com would inherit their account (§4.2).
            if (!identity.EmailVerified)
            {
                // Refused outright rather than given a second account of its own. Email is the
                // identity key, so two accounts sharing one would break the premise — and the unique
                // index would reject the insert anyway, turning a security decision into a 500.
                logger.LogWarning(
                    "Rejected an unverified Google identity claiming an address that already has an account");

                return Result.Failure<(User, OrganizationMember?)>(Error.Unauthorized(
                    "auth.email_unverified",
                    "Verify your email address with Google, then sign in again."));
            }

            byEmail.LinkExternalLogin(
                ExternalLogin.ForGoogle(byEmail.Id, identity.Subject, identity.Email));

            logger.LogInformation(
                "Linked Google identity to existing account {UserId} by verified email",
                byEmail.Id);

            return Result.Success<(User, OrganizationMember?)>((byEmail, null));
        }

        var (provisionedUser, membership) = await ProvisionAsync(identity, cancellationToken);

        return Result.Success<(User, OrganizationMember?)>((provisionedUser, membership));
    }

    /// <summary>
    /// Creates the account, its personal workspace, and its preferences.
    /// </summary>
    /// <remarks>
    /// All three in one unit of work. A user without a workspace cannot see anything and a user
    /// without preferences breaks the settings screen, so a partial provision is worse than a failed
    /// one — the pipeline's transaction is what makes this all-or-nothing.
    /// </remarks>
    private async Task<(User User, OrganizationMember Membership)> ProvisionAsync(
        GoogleIdentity identity,
        CancellationToken cancellationToken)
    {
        var user = User.Create(identity.Email, identity.Name, identity.PictureUrl);
        user.LinkExternalLogin(ExternalLogin.ForGoogle(user.Id, identity.Subject, identity.Email));

        var organization = Organization.CreatePersonal(WorkspaceNameFor(identity), user.Id);

        await context.Users.AddAsync(user, cancellationToken);
        await context.Organizations.AddAsync(organization, cancellationToken);
        await context.UserPreferences.AddAsync(
            Domain.Identity.UserPreferences.CreateDefault(user.Id),
            cancellationToken);

        logger.LogInformation(
            "Provisioned user {UserId} with workspace {OrganizationId}",
            user.Id,
            organization.Id);

        // Organization.Create makes the creator an owning member, so this is always present.
        return (user, organization.Members.Single());
    }

    /// <summary>
    /// Issues the access token and opens a new refresh-token family.
    /// </summary>
    private async Task<AuthResult> IssueSessionAsync(
        User user,
        OrganizationMember membership,
        SessionContext sessionContext,
        CancellationToken cancellationToken)
    {
        var refresh = tokens.CreateRefreshToken();

        var refreshToken = RefreshToken.Issue(
            user.Id,
            refresh.Hash,
            refresh.ExpiresAt - clock.UtcNow,
            device: sessionContext.Device,
            ipAddress: sessionContext.IpAddress);

        await context.RefreshTokens.AddAsync(refreshToken, cancellationToken);

        var access = tokens.CreateAccessToken(new AccessTokenRequest(
            user.Id,
            user.Email,
            user.Name,
            user.AvatarUrl,
            membership.OrganizationId,
            membership.Role,
            // The family, not this token: the session survives rotation, so a session listed in the
            // UI keeps its identity as its token is replaced.
            refreshToken.FamilyId));

        return new AuthResult(
            new AuthResponse(access.Value, access.ExpiresInSeconds, user.ToDto(membership)),
            refresh.Value,
            refresh.ExpiresAt);
    }

    /// <summary>
    /// Names the personal workspace after the person, or their company for a Workspace account.
    /// </summary>
    private static string WorkspaceNameFor(GoogleIdentity identity) =>
        string.IsNullOrWhiteSpace(identity.HostedDomain)
            ? $"{identity.Name}'s workspace"
            : identity.HostedDomain;
}
