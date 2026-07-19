using Cadence.Domain.Common;
using Cadence.Domain.Enums;

namespace Cadence.Domain.Integrations;

/// <summary>
/// A third-party service connected to one workspace.
/// </summary>
/// <remarks>
/// The provider's OAuth tokens are deliberately <b>not</b> on this entity. They are secrets with a
/// different lifetime and a different blast radius, so they live encrypted in a separate store keyed
/// by this integration's id (blueprint §15.4). This row is safe to return from the API; the token
/// record never is.
/// </remarks>
public sealed class Integration : AggregateRoot, ITenantScoped
{
    private Integration()
    {
        Key = null!;
        Name = null!;
        Description = null!;
    }

    private Integration(
        Guid organizationId,
        string key,
        string name,
        string description,
        IntegrationCategory category)
    {
        OrganizationId = organizationId;
        Key = key;
        Name = name;
        Description = description;
        Category = category;
        Status = IntegrationStatus.Disconnected;
    }

    public Guid OrganizationId { get; private set; }

    /// <summary>Stable provider identifier — <c>zoom</c>, <c>google-calendar</c>, <c>slack</c>.</summary>
    public string Key { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public IntegrationCategory Category { get; private set; }

    public IntegrationStatus Status { get; private set; }

    public DateTimeOffset? ConnectedAt { get; private set; }

    /// <summary>The account shown in the UI, e.g. the connected mailbox. Never a token.</summary>
    public string? AccountLabel { get; private set; }

    /// <summary>Why the connection broke, when <see cref="Status"/> is <see cref="IntegrationStatus.Error"/>.</summary>
    public string? LastError { get; private set; }

    public static Integration Create(
        Guid organizationId,
        string key,
        string name,
        string description,
        IntegrationCategory category)
    {
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(key), "Integration key is required.");
        DomainException.ThrowIf(string.IsNullOrWhiteSpace(name), "Integration name is required.");

        return new Integration(
            organizationId,
            key.Trim().ToLowerInvariant(),
            name.Trim(),
            description.Trim(),
            category);
    }

    public void Connect(string accountLabel, DateTimeOffset connectedAt)
    {
        DomainException.ThrowIf(
            string.IsNullOrWhiteSpace(accountLabel),
            "A connected integration must name the account it is connected to.");

        Status = IntegrationStatus.Connected;
        AccountLabel = accountLabel.Trim();
        ConnectedAt = connectedAt;
        LastError = null;
    }

    public void Disconnect()
    {
        Status = IntegrationStatus.Disconnected;
        AccountLabel = null;
        ConnectedAt = null;
        LastError = null;
    }

    /// <summary>
    /// Records a failure without dropping the connection — a revoked or expired grant is usually
    /// recoverable by re-authorising, and clearing the account label would lose the context the user
    /// needs to do that.
    /// </summary>
    public void MarkErrored(string error)
    {
        Status = IntegrationStatus.Error;
        LastError = error.Trim();
    }
}
