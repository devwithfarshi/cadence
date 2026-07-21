using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Cadence.Application.Common.Abstractions;

namespace Cadence.IntegrationTests;

/// <summary>
/// A stand-in for Cloudinary, holding what "landed" in memory.
/// </summary>
/// <remarks>
/// <para>
/// The third and last thing faked in this suite, and named by §18's mocking policy alongside the
/// model and the Google validator. The reason is the same: it is a port to a paid third party, and a
/// test that uploaded real files would be slow, billed and non-deterministic.
/// </para>
/// <para>
/// What it deliberately does <b>not</b> fake is agreement between the client and the store. A test
/// stages what the provider will report — a size, a format, or nothing at all — independently of what
/// the request claims, because every check worth having in this module is exactly that disagreement.
/// </para>
/// </remarks>
public sealed class FakeFileStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, StoredFile> _assets = new(StringComparer.Ordinal);

    /// <summary>Keys handed to <see cref="DeleteAsync"/>, in order.</summary>
    public Collection<string> Destroyed { get; } = [];

    /// <summary>Every key this store has signed an upload for.</summary>
    public Collection<string> Signed { get; } = [];

    /// <summary>Makes <see cref="GetAsync"/> report an asset at <paramref name="storageKey"/>.</summary>
    public void Land(string storageKey, long sizeBytes = 2048, string? format = null) =>
        _assets[storageKey] = new StoredFile(
            storageKey,
            $"https://files.test/{storageKey}",
            sizeBytes,
            format ?? Path.GetExtension(storageKey).TrimStart('.'));

    /// <summary>Removes an asset without going through the port, as a provider-side loss would.</summary>
    public void Lose(string storageKey) => _assets.TryRemove(storageKey, out _);

    public bool Holds(string storageKey) => _assets.ContainsKey(storageKey);

    public Task<SignedUpload> CreateSignedUploadAsync(
        string folder,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        // The same shape the Cloudinary adapter produces: the server chooses the key, the extension
        // survives, and the folder it was told to use is a prefix of it.
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var storageKey = $"cadence/test/{folder}/{Guid.CreateVersion7()}{extension}";

        Signed.Add(storageKey);

        return Task.FromResult(new SignedUpload(
            "https://files.test/upload",
            storageKey,
            "signature",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            "test-api-key",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["public_id"] = storageKey,
                ["type"] = "authenticated",
            }));
    }

    public Task<StoredFile?> GetAsync(string storageKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(_assets.GetValueOrDefault(storageKey));

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        Destroyed.Add(storageKey);
        _assets.TryRemove(storageKey, out _);

        return Task.CompletedTask;
    }

    public Task<string> GetDownloadUrlAsync(
        string storageKey,
        TimeSpan expiresIn,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(
            $"https://files.test/{storageKey}?expires={DateTimeOffset.UtcNow.Add(expiresIn).ToUnixTimeSeconds()}");
}
