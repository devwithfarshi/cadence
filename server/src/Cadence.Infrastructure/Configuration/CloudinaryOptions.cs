using System.ComponentModel.DataAnnotations;

namespace Cadence.Infrastructure.Configuration;

/// <summary>
/// Settings for the file store behind <c>IFileStorage</c> (§12, §23.2).
/// </summary>
/// <remarks>
/// Only the two values that are genuinely deployment-specific. The upload size ceiling, the format
/// allow-list and the delivery-URL lifetime are <b>not</b> here — they are library policy rather than
/// provider configuration, and they live with the module that enforces them (<c>DocumentPolicy</c>)
/// so there is one copy of each rule rather than one per adapter.
/// </remarks>
public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    /// <summary>
    /// The whole credential, as <c>cloudinary://api_key:api_secret@cloud_name</c>.
    /// </summary>
    /// <remarks>
    /// Empty disables file storage rather than failing at startup, for the same reason
    /// <c>Ai:ApiKey</c> does: a developer reading meetings should not need a Cloudinary account.
    /// Signing and registration then fail loudly with a configuration error — they never pretend to
    /// have stored a file.
    /// </remarks>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// The deployment's folder segment, so staging and production can share one cloud account
    /// without writing into each other's tenants (§12.2).
    /// </summary>
    [Required]
    public string Environment { get; init; } = "development";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Url);
}
