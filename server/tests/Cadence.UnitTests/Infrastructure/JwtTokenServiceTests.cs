using System.IdentityModel.Tokens.Jwt;
using Cadence.Application.Common.Abstractions;
using Cadence.Domain.Enums;
using Cadence.Infrastructure.Authentication;
using Cadence.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Cadence.UnitTests.Infrastructure;

public class JwtTokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheAccessTokenCarriesOrganizationAndRole()
    {
        // Authorization reads these from the token, so a missing one is not a cosmetic defect —
        // the tenant filter would resolve to Guid.Empty and the caller would see nothing.
        var token = Read(Issue());

        token.Claims.Single(claim => claim.Type == CadenceClaims.OrganizationId).Value
            .ShouldNotBeNullOrWhiteSpace();
        token.Claims.Single(claim => claim.Type == CadenceClaims.Role).Value.ShouldBe("admin");
    }

    [Fact]
    public void TheRoleClaimIsLowercase_MatchingThePolicies()
    {
        // The policies compare against literal "owner"/"admin"/"member". A PascalCase claim would
        // silently fail every authorization check while looking correct in a debugger.
        Read(Issue(UserRole.Owner))
            .Claims.Single(claim => claim.Type == CadenceClaims.Role).Value
            .ShouldBe("owner");
    }

    [Fact]
    public void TheSessionClaimIsTheFamilyId_NotTheTokenId()
    {
        // A session survives rotation, so it must be identified by something that does not change
        // when the refresh token is replaced.
        var sessionId = Guid.CreateVersion7();

        Read(Issue(sessionId: sessionId))
            .Claims.Single(claim => claim.Type == CadenceClaims.SessionId).Value
            .ShouldBe(sessionId.ToString());
    }

    [Fact]
    public void EachAccessTokenHasItsOwnJti()
    {
        Read(Issue()).Id.ShouldNotBe(Read(Issue()).Id);
    }

    [Fact]
    public void TheAccessTokenExpiresWhenConfigured()
    {
        var service = Create(accessTokenMinutes: 15);

        var token = service.CreateAccessToken(Request());

        token.ExpiresInSeconds.ShouldBe(900);
        token.ExpiresAt.ShouldBe(Now.AddMinutes(15));
    }

    [Fact]
    public void RefreshTokensAreUnpredictable()
    {
        var service = Create();

        var values = Enumerable.Range(0, 200)
            .Select(_ => service.CreateRefreshToken().Value)
            .ToList();

        values.Distinct().Count().ShouldBe(values.Count);
        // 32 bytes, base64url — short enough to spot a truncation, long enough to rule out guessing.
        values.ShouldAllBe(value => value.Length >= 40);
    }

    [Fact]
    public void ARefreshTokenIsNeverReturnedAlongsideItsOwnHash()
    {
        var service = Create();

        var pair = service.CreateRefreshToken();

        pair.Hash.ShouldNotBe(pair.Value);
        pair.Hash.ShouldBe(service.HashRefreshToken(pair.Value));
    }

    [Fact]
    public void HashingIsDeterministic_SoLookupIsAnIndexedEqualityMatch()
    {
        // The lookup happens on every refresh; a per-row salt would force a table scan.
        var service = Create();

        service.HashRefreshToken("token-value").ShouldBe(service.HashRefreshToken("token-value"));
        service.HashRefreshToken("token-value").ShouldNotBe(service.HashRefreshToken("other-value"));
    }

    private static string Issue(UserRole role = UserRole.Admin, Guid? sessionId = null) =>
        Create().CreateAccessToken(Request(role, sessionId)).Value;

    private static AccessTokenRequest Request(UserRole role = UserRole.Admin, Guid? sessionId = null) =>
        new(
            Guid.CreateVersion7(),
            "alex@northwind.io",
            "Alex Rivera",
            PictureUrl: null,
            Guid.CreateVersion7(),
            role,
            sessionId ?? Guid.CreateVersion7());

    private static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    private static JwtTokenService Create(int accessTokenMinutes = 15)
    {
        var clock = Substitute.For<IDateTime>();
        clock.UtcNow.Returns(Now);

        return new JwtTokenService(
            Options.Create(new JwtOptions
            {
                SigningKey = "a-test-signing-key-that-is-long-enough-to-use",
                Issuer = "cadence-test",
                Audience = "cadence-test-client",
                AccessTokenMinutes = accessTokenMinutes,
            }),
            clock);
    }
}
