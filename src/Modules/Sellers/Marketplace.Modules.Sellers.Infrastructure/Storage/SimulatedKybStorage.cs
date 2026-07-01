using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Sellers.Application.Abstractions;

namespace Marketplace.Modules.Sellers.Infrastructure.Storage;

/// <summary>
/// Stub hors-ligne du stockage KYB : génère une URL déterministe sans appel
/// réseau. Actif tant que « Storage:CloudflareR2 » n'est pas configuré, pour
/// permettre le dev/les tests sans compte Cloudflare.
/// </summary>
public sealed class SimulatedKybStorage : IKybDocumentStorage
{
    public Task<Result<StoredKybFile>> UploadAsync(KybFileUpload file, CancellationToken cancellationToken = default)
    {
        var key = $"kyb/{Guid.NewGuid():N}/{file.FileName}";
        return Task.FromResult<Result<StoredKybFile>>(
            new StoredKybFile($"https://kyb.local/{key}", key));
    }

    public Result<string> CreatePresignedGetUrl(string objectKey, int expiresSeconds = 300)
        => Result.Success($"https://kyb.local/{objectKey}");
}
