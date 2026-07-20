using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class InvitationTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly Guid InviterId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiresAt = Now.AddDays(7);

    [Fact]
    public void AnInvitationCannotGrantOwnership()
    {
        Should.Throw<DomainException>(() => Create(role: UserRole.Owner));
    }

    [Fact]
    public void TheEmailIsNormalised_SoLookupIsCaseInsensitive()
    {
        var invitation = Create(email: "  Sam.Okafor@Northwind.io ");

        invitation.Email.ShouldBe("sam.okafor@northwind.io");
    }

    [Fact]
    public void AcceptingWithADifferentGoogleAccount_IsRejected()
    {
        // The link only names the invitation; the Google-verified email is what authorises it. A
        // forwarded invitation must not be redeemable by whoever received the forward.
        var invitation = Create(email: "sam.okafor@northwind.io");

        Should.Throw<DomainException>(
            () => invitation.Accept(Guid.CreateVersion7(), "someone.else@northwind.io", Now));
    }

    [Fact]
    public void AcceptingWithTheInvitedAccount_Succeeds_RegardlessOfCasing()
    {
        var invitation = Create(email: "sam.okafor@northwind.io");
        var userId = Guid.CreateVersion7();

        invitation.Accept(userId, "Sam.Okafor@Northwind.io", Now);

        invitation.Status.ShouldBe(InvitationStatus.Accepted);
        invitation.AcceptedByUserId.ShouldBe(userId);
    }

    [Fact]
    public void AnExpiredInvitation_IsNotRedeemable_WithoutAnythingHavingRunOnASchedule()
    {
        var invitation = Create();

        invitation.IsRedeemable(Now.AddDays(8)).ShouldBeFalse();
        Should.Throw<DomainException>(
            () => invitation.Accept(Guid.CreateVersion7(), "sam.okafor@northwind.io", Now.AddDays(8)));
    }

    [Fact]
    public void ARevokedInvitation_CannotBeAccepted()
    {
        var invitation = Create();
        invitation.Revoke();

        Should.Throw<DomainException>(
            () => invitation.Accept(Guid.CreateVersion7(), "sam.okafor@northwind.io", Now));
    }

    [Fact]
    public void AnAcceptedInvitation_CannotBeRevoked()
    {
        var invitation = Create();
        invitation.Accept(Guid.CreateVersion7(), "sam.okafor@northwind.io", Now);

        Should.Throw<DomainException>(invitation.Revoke);
    }

    [Fact]
    public void AnInvitationCannotBeCreatedAlreadyExpired()
    {
        var act = () => Invitation.Create(
            OrganizationId,
            "sam.okafor@northwind.io",
            UserRole.Member,
            InviterId,
            tokenHash: "5f4dcc3b5aa765d61d8327deb882cf99",
            expiresAt: Now.AddMinutes(-1),
            now: Now);

        Should.Throw<DomainException>(act);
    }

    [Fact]
    public void MarkExpired_OnlyTouchesPendingInvitations()
    {
        var accepted = Create();
        accepted.Accept(Guid.CreateVersion7(), "sam.okafor@northwind.io", Now);

        accepted.MarkExpired(Now.AddDays(8));

        accepted.Status.ShouldBe(InvitationStatus.Accepted);
    }

    [Fact]
    public void Reissue_ReplacesTheTokenSoTheOldLinkStopsWorking()
    {
        // Resending because a link went astray must invalidate the link that went astray, or the
        // resend hands out a second working invitation rather than replacing the first.
        var invitation = Create();

        invitation.Reissue("9c1185a5c5e9fc54612808977ee8f548", Now.AddDays(14), Now);

        invitation.TokenHash.ShouldBe("9c1185a5c5e9fc54612808977ee8f548");
        invitation.Status.ShouldBe(InvitationStatus.Pending);
    }

    [Fact]
    public void Reissue_RevivesAnExpiredInvitation()
    {
        var invitation = Create();
        invitation.MarkExpired(Now.AddDays(30));

        invitation.Reissue("9c1185a5c5e9fc54612808977ee8f548", Now.AddDays(44), Now.AddDays(30));

        invitation.IsRedeemable(Now.AddDays(30)).ShouldBeTrue();
    }

    [Fact]
    public void Reissue_RefusesAnAcceptedOrRevokedInvitation()
    {
        // Reviving either would silently re-grant access that somebody deliberately ended.
        var accepted = Create();
        accepted.Accept(Guid.CreateVersion7(), "sam.okafor@northwind.io", Now);

        var revoked = Create();
        revoked.Revoke();

        Should.Throw<DomainException>(
            () => accepted.Reissue("9c1185a5c5e9fc54612808977ee8f548", Now.AddDays(14), Now));
        Should.Throw<DomainException>(
            () => revoked.Reissue("9c1185a5c5e9fc54612808977ee8f548", Now.AddDays(14), Now));
    }

    private static Invitation Create(
        string email = "sam.okafor@northwind.io",
        UserRole role = UserRole.Member) =>
        Invitation.Create(
            OrganizationId,
            email,
            role,
            InviterId,
            tokenHash: "5f4dcc3b5aa765d61d8327deb882cf99",
            expiresAt: ExpiresAt,
            now: Now);
}
