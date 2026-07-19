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
}
