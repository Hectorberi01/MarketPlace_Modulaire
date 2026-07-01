using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Abstractions;

namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Stub hors-ligne du service média : génère une URL déterministe sans appel
/// réseau. Actif tant que « Media:ClientId » n'est pas renseigné, pour permettre
/// le dev/les tests sans dépendre de https://hbamediacore.fr.
/// </summary>
internal sealed class SimulatedMediaStorage : IMediaStorage
{
    private readonly MediaServiceOptions _options;

    public SimulatedMediaStorage(MediaServiceOptions options) => _options = options;

    public Task<Result<StoredMedia>> UploadAsync(MediaUpload file, CancellationToken cancellationToken = default)
        => Task.FromResult<Result<StoredMedia>>(Build());

    public Task<Result<IReadOnlyList<StoredMedia>>> UploadManyAsync(
        IReadOnlyList<MediaUpload> files, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<StoredMedia> stored = files.Select(_ => Build()).ToList();
        return Task.FromResult(Result.Success(stored));
    }

    private StoredMedia Build()
    {
        var id = Guid.NewGuid().ToString("N");
        var baseUrl = _options.BaseUrl.TrimEnd('/');
        // Le service réel convertit en WebP ; on simule la même forme d'URL.
        return new StoredMedia($"{baseUrl}/uploads/images/{id}.webp", id, "image/webp");
    }

    public Task<Result> DeleteAsync(string externalId, CancellationToken cancellationToken = default)
        => Task.FromResult(Result.Success());
}
