using Cadence.Domain.Common;
using Cadence.Domain.Identity;
using Shouldly;

namespace Cadence.UnitTests.Domain;

/// <summary>
/// The session's workspace scope. Rotation and reuse detection are covered by the auth
/// integration suite, which exercises them against a real database.
/// </summary>
public class RefreshTokenTests
{
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly Guid OrganizationId = Guid.CreateVersion7();

    [Fact]
    public void Issue_ScopesTheSessionToAWorkspace()
    {
        var token = Issue();

        token.OrganizationId.ShouldBe(OrganizationId);
    }

    [Fact]
    public void Issue_RefusesASessionWithNoWorkspace()
    {
        // Guid.Empty is what an unauthenticated context reads as, and the tenant filter matches it
        // against nothing. A session carrying it would refresh into a workspace with no rows and no
        // explanation, so it is refused at the point it would be created.
        Should.Throw<DomainException>(() => RefreshToken.Issue(
            UserId,
            Guid.Empty,
            tokenHash: "5f4dcc3b5aa765d61d8327deb882cf99",
            lifetime: TimeSpan.FromDays(30)));
    }

    [Fact]
    public void ScopeTo_MovesTheSessionWithoutRotatingIt()
    {
        // Nothing about the credential changes on a switch, only what it resolves to. Rotating would
        // invalidate the cookie the client is holding and buy nothing.
        var token = Issue();
        var destination = Guid.CreateVersion7();

        token.ScopeTo(destination);

        token.OrganizationId.ShouldBe(destination);
        token.IsRotated.ShouldBeFalse();
        token.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void ScopeTo_RefusesASessionThatIsNoLongerActive()
    {
        var token = Issue();
        token.Revoke();

        Should.Throw<DomainException>(() => token.ScopeTo(Guid.CreateVersion7()));
    }

    [Fact]
    public void RotationKeepsTheFamily_SoTheSessionSurvives()
    {
        var token = Issue();

        var successor = RefreshToken.Issue(
            UserId,
            OrganizationId,
            tokenHash: "9c1185a5c5e9fc54612808977ee8f548",
            lifetime: TimeSpan.FromDays(30),
            familyId: token.FamilyId);

        token.RotateTo(successor);

        successor.FamilyId.ShouldBe(token.FamilyId);
        token.IsRotated.ShouldBeTrue();
    }

    private static RefreshToken Issue() => RefreshToken.Issue(
        UserId,
        OrganizationId,
        tokenHash: "5f4dcc3b5aa765d61d8327deb882cf99",
        lifetime: TimeSpan.FromDays(30));
}
