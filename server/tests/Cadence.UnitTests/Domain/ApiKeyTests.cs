using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class ApiKeyTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly Guid CreatorId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void AKeyWithNoScopes_IsRejected()
    {
        Should.Throw<DomainException>(() => Issue([]));
    }

    [Fact]
    public void AReadOnlyKey_DoesNotGrantWrite()
    {
        var key = Issue([ApiKeyScope.Read]);

        key.HasScope(ApiKeyScope.Read).ShouldBeTrue();
        key.HasScope(ApiKeyScope.Write).ShouldBeFalse();
    }

    [Fact]
    public void ARevokedKey_GrantsNothing()
    {
        var key = Issue([ApiKeyScope.Read, ApiKeyScope.Write]);

        key.Revoke(Now);

        key.IsActive.ShouldBeFalse();
        key.HasScope(ApiKeyScope.Read).ShouldBeFalse();
        key.HasScope(ApiKeyScope.Write).ShouldBeFalse();
    }

    [Fact]
    public void RevokingTwice_KeepsTheOriginalTimestamp()
    {
        // The audit trail should say when access was actually withdrawn, not when someone last
        // clicked the button.
        var key = Issue([ApiKeyScope.Read]);
        key.Revoke(Now);

        key.Revoke(Now.AddHours(3));

        key.RevokedAt.ShouldBe(Now);
    }

    [Fact]
    public void TheSecretIsNeverStored_OnlyItsHashAndPrefix()
    {
        var key = Issue([ApiKeyScope.Read]);

        // A compile-time guarantee as much as a test: there is no property holding the plaintext.
        typeof(ApiKey).GetProperties()
            .Select(property => property.Name)
            .ShouldNotContain("Secret");
        key.KeyHash.ShouldNotBeNullOrWhiteSpace();
        key.Prefix.ShouldBe("cad_live_9f2c");
    }

    private static ApiKey Issue(IEnumerable<ApiKeyScope> scopes) =>
        ApiKey.Issue(
            OrganizationId,
            CreatorId,
            "CI pipeline",
            "cad_live_9f2c",
            keyHash: "3a7bd3e2360a3d29eea436fcfb7e44c735d117c42d1c1835420b6b9942dd4f1b",
            scopes);
}
