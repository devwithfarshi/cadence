using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Identity;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class OrganizationTests
{
    private static readonly Guid OwnerId = Guid.CreateVersion7();

    [Fact]
    public void Create_MakesTheCreatorAnOwningMember()
    {
        var organization = Organization.Create("Northwind Labs", OwnerId);

        organization.OwnerId.ShouldBe(OwnerId);
        organization.Members.ShouldHaveSingleItem().Role.ShouldBe(UserRole.Owner);
    }

    [Fact]
    public void Create_DerivesAUrlSafeSlug()
    {
        var organization = Organization.Create("Northwind Labs & Co.", OwnerId);

        organization.Slug.ShouldMatch("^[a-z0-9]+(-[a-z0-9]+)*$");
    }

    [Fact]
    public void TwoPersonalWorkspacesWithTheSameName_GetDifferentSlugs()
    {
        // Names repeat. Without a discriminator the second person called Alex Rivera hits the unique
        // index on first sign-in and gets a 500 instead of an account.
        var first = Organization.CreatePersonal("Alex Rivera's workspace", Guid.CreateVersion7());
        var second = Organization.CreatePersonal("Alex Rivera's workspace", Guid.CreateVersion7());

        first.Slug.ShouldNotBe(second.Slug);
    }

    [Fact]
    public void PersonalSlugsStayDistinct_EvenWhenCreatedBackToBack()
    {
        // The discriminator comes from the *end* of the id. A UUIDv7 leads with a 48-bit timestamp,
        // so a prefix is identical for everything created in the same stretch of time — which would
        // reproduce exactly the collision it is meant to prevent.
        var slugs = Enumerable.Range(0, 200)
            .Select(_ => Organization.CreatePersonal("Alex Rivera's workspace", Guid.CreateVersion7()).Slug)
            .ToList();

        slugs.Distinct().Count().ShouldBe(slugs.Count);
    }

    [Fact]
    public void ADeliberatelyNamedWorkspace_KeepsACleanSlug()
    {
        // Someone typing "Northwind" should be told the name is taken, not silently handed
        // "northwind-a91f4c". The unique index is what tells them.
        Organization.Create("Northwind Labs", OwnerId).Slug.ShouldBe("northwind-labs");
    }

    [Fact]
    public void TheLastOwner_CannotBeDemoted()
    {
        // Without this, an admin could strand a workspace with nobody able to administer it.
        var organization = Organization.Create("Northwind Labs", OwnerId);

        Should.Throw<DomainException>(() => organization.ChangeMemberRole(OwnerId, UserRole.Admin));
    }

    [Fact]
    public void TheLastOwner_CannotBeRemoved()
    {
        var organization = Organization.Create("Northwind Labs", OwnerId);

        Should.Throw<DomainException>(() => organization.RemoveMember(OwnerId));
    }

    [Fact]
    public void AnOwnerCanStepDown_OnceSomeoneElseOwnsTheWorkspace()
    {
        var organization = Organization.Create("Northwind Labs", OwnerId);
        var successor = Guid.CreateVersion7();
        organization.AddMember(successor, UserRole.Member);

        organization.ChangeMemberRole(successor, UserRole.Owner);
        organization.ChangeMemberRole(OwnerId, UserRole.Admin);

        organization.OwnerId.ShouldBe(successor);
        organization.Members.Count(member => member.Role == UserRole.Owner).ShouldBe(1);
    }

    [Fact]
    public void AddMember_RefusesSomeoneWhoIsAlreadyIn()
    {
        var organization = Organization.Create("Northwind Labs", OwnerId);
        var member = Guid.CreateVersion7();
        organization.AddMember(member, UserRole.Member);

        Should.Throw<DomainException>(() => organization.AddMember(member, UserRole.Admin));
    }

    [Fact]
    public void ChangingTheRoleOfANonMember_IsRejected()
    {
        var organization = Organization.Create("Northwind Labs", OwnerId);

        Should.Throw<DomainException>(
            () => organization.ChangeMemberRole(Guid.CreateVersion7(), UserRole.Admin));
    }

    [Fact]
    public void NewMembers_AreActiveInTheirWorkspace()
    {
        var organization = Organization.Create("Northwind Labs", OwnerId);
        var member = organization.AddMember(Guid.CreateVersion7(), UserRole.Member);

        member.Status.ShouldBe(UserStatus.Active);
        member.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void SuspendingAMember_LeavesTheirOtherWorkspacesAlone()
    {
        // Status lives on the membership rather than the user precisely for this. Were it global,
        // anyone able to create a free workspace and get an invitation accepted could disable a
        // colleague's real account.
        var person = Guid.CreateVersion7();

        var first = Organization.Create("Northwind Labs", OwnerId);
        first.AddMember(person, UserRole.Member);

        var second = Organization.Create("Contoso", Guid.CreateVersion7());
        second.AddMember(person, UserRole.Member);

        first.SetMemberStatus(person, UserStatus.Suspended);

        first.Members.Single(member => member.UserId == person).IsActive.ShouldBeFalse();
        second.Members.Single(member => member.UserId == person).IsActive.ShouldBeTrue();
    }

    [Fact]
    public void SuspendingTheOnlyOwner_IsRefused()
    {
        // Demotion is already barred from leaving a workspace nobody can administer. Blocking that
        // route and not this one would just make it a two-step.
        var organization = Organization.Create("Northwind Labs", OwnerId);

        Should.Throw<DomainException>(
            () => organization.SetMemberStatus(OwnerId, UserStatus.Suspended));
    }

    [Fact]
    public void SuspendingAnOwner_IsAllowedOnceThereIsAnother()
    {
        var organization = Organization.Create("Northwind Labs", OwnerId);
        var second = Guid.CreateVersion7();
        organization.AddMember(second, UserRole.Member);
        organization.ChangeMemberRole(second, UserRole.Owner);

        organization.SetMemberStatus(OwnerId, UserStatus.Suspended);

        organization.Members.Single(member => member.UserId == OwnerId).IsActive.ShouldBeFalse();
    }
}
