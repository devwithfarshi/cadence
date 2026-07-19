using Cadence.Domain.Common;
using Cadence.Domain.Enums;
using Cadence.Domain.Integrations;
using Shouldly;

namespace Cadence.UnitTests.Domain;

public class IntegrationTests
{
    private static readonly Guid OrganizationId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ANewIntegration_StartsDisconnected()
    {
        Create().Status.ShouldBe(IntegrationStatus.Disconnected);
    }

    [Fact]
    public void TheKeyIsNormalised_SoProviderLookupIsStable()
    {
        Create(key: " Google-Calendar ").Key.ShouldBe("google-calendar");
    }

    [Fact]
    public void AConnectedIntegration_MustNameTheAccount()
    {
        // "Connected" with no account label leaves the user unable to tell which mailbox is linked.
        var integration = Create();

        Should.Throw<DomainException>(() => integration.Connect("  ", Now));
    }

    [Fact]
    public void Disconnecting_ClearsTheAccountLabel()
    {
        var integration = Create();
        integration.Connect("alex@northwind.io", Now);

        integration.Disconnect();

        integration.AccountLabel.ShouldBeNull();
        integration.ConnectedAt.ShouldBeNull();
        integration.Status.ShouldBe(IntegrationStatus.Disconnected);
    }

    [Fact]
    public void AnError_KeepsTheAccountLabel_SoTheUserKnowsWhatToReauthorise()
    {
        var integration = Create();
        integration.Connect("alex@northwind.io", Now);

        integration.MarkErrored("Refresh token was revoked by the provider.");

        integration.Status.ShouldBe(IntegrationStatus.Error);
        integration.AccountLabel.ShouldBe("alex@northwind.io");
        integration.LastError.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Reconnecting_ClearsThePreviousError()
    {
        var integration = Create();
        integration.Connect("alex@northwind.io", Now);
        integration.MarkErrored("Refresh token was revoked by the provider.");

        integration.Connect("alex@northwind.io", Now.AddHours(1));

        integration.Status.ShouldBe(IntegrationStatus.Connected);
        integration.LastError.ShouldBeNull();
    }

    [Fact]
    public void ProviderTokensAreNotOnThisEntity()
    {
        // This row is returned by the API; secrets live in a separate encrypted store (§15.4).
        var names = typeof(Integration).GetProperties().Select(property => property.Name).ToArray();

        names.ShouldNotContain("AccessToken");
        names.ShouldNotContain("RefreshToken");
    }

    private static Integration Create(string key = "google-calendar") =>
        Integration.Create(
            OrganizationId,
            key,
            "Google Calendar",
            "Pull scheduled events into Cadence.",
            IntegrationCategory.Calendar);
}
