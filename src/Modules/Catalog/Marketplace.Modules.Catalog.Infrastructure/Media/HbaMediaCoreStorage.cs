using System.Net.Http.Headers;
using System.Net.Http.Json;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Application.Abstractions;

namespace Marketplace.Modules.Catalog.Infrastructure.Media;

/// <summary>
/// Adaptateur RÉEL du service média (https://hbamediacore.fr). Téléverse via
/// « POST {base}/api/media/upload/batch » (multipart, champ « files », en-tête
/// « x-client-id ») et renvoie l'URL publique. Le service convertit les images en
/// WebP et répond par un tableau d'objets média (on lit le premier élément).
/// </summary>
internal sealed class HbaMediaCoreStorage : IMediaStorage
{
    public const string ClientName = "catalog-media";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MediaServiceOptions _options;

    public HbaMediaCoreStorage(IHttpClientFactory httpClientFactory, MediaServiceOptions options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    public async Task<Result<StoredMedia>> UploadAsync(MediaUpload file, CancellationToken cancellationToken = default)
    {
        var result = await UploadManyAsync(new[] { file }, cancellationToken);
        if (result.IsFailure)
        {
            return Result.Failure<StoredMedia>(result.Error);
        }

        var first = result.Value.FirstOrDefault();
        return first is null
            ? Error.Failure("catalog.media.upload_failed", "Réponse du service média invalide (aucun média).")
            : Result.Success(first);
    }

    public async Task<Result<IReadOnlyList<StoredMedia>>> UploadManyAsync(
        IReadOnlyList<MediaUpload> files, CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            return Result.Success<IReadOnlyList<StoredMedia>>(Array.Empty<StoredMedia>());
        }

        var client = _httpClientFactory.CreateClient(ClientName);

        using var content = new MultipartFormDataContent();
        // Tous les fichiers sont portés par le champ « files » (tableau), répété.
        foreach (var file in files)
        {
            var fileContent = new ByteArrayContent(file.Content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(
                string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
            content.Add(fileContent, "files", file.FileName);
        }
        content.Add(new StringContent(_options.DefaultVisibility), "visibility");
        var firstAlt = files.Select(f => f.AltText).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
        if (!string.IsNullOrWhiteSpace(firstAlt))
        {
            content.Add(new StringContent(firstAlt), "alt");
        }

        // BaseAddress du HttpClient = Media:BaseUrl ; on utilise un chemin relatif.
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/media/upload/batch") { Content = content };
        request.Headers.Add("x-client-id", _options.ClientId);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return Error.Failure("catalog.media.unreachable", $"Service média injoignable : {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Error.Failure("catalog.media.upload_failed",
                $"Échec de l'upload média ({(int)response.StatusCode}). {body}");
        }

        // La réponse du batch est un TABLEAU d'objets média (même ordre que l'envoi).
        var medias = await response.Content.ReadFromJsonAsync<List<MediaFileResponse>>(cancellationToken: cancellationToken);
        if (medias is null || medias.Count == 0 || medias.Any(m => string.IsNullOrWhiteSpace(ResolveUrl(m))))
        {
            return Error.Failure("catalog.media.upload_failed", "Réponse du service média invalide (URL absente).");
        }

        // On stocke l'URL publique (champ « url ») et l'« id » distant (pour la
        // suppression) ; le « path » relatif n'est jamais persisté tel quel.
        IReadOnlyList<StoredMedia> stored = medias
            .Select((m, i) => new StoredMedia(
                ResolveUrl(m)!,
                m.id ?? string.Empty,
                m.mimeType ?? files[Math.Min(i, files.Count - 1)].ContentType))
            .ToList();
        return Result.Success(stored);
    }

    /// <summary>
    /// URL ABSOLUE à stocker. Le service peut renvoyer « url » (ou « path ») sous
    /// forme RELATIVE (« /uploads/images/x.webp ») : dans ce cas on la préfixe par
    /// la base, sinon l'image ne se résout pas côté navigateur. On ne stocke jamais
    /// un chemin relatif tel quel.
    /// </summary>
    private string? ResolveUrl(MediaFileResponse m)
    {
        var raw = !string.IsNullOrWhiteSpace(m.url) ? m.url : m.path;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return raw;
        }

        return $"{_options.BaseUrl.TrimEnd('/')}/{raw.TrimStart('/')}";
    }

    public async Task<Result> DeleteAsync(string externalId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalId))
        {
            return Result.Success();
        }

        var client = _httpClientFactory.CreateClient(ClientName);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/media/{Uri.EscapeDataString(externalId)}");
        request.Headers.Add("x-client-id", _options.ClientId);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return Result.Failure(Error.Failure("catalog.media.unreachable", $"Service média injoignable : {ex.Message}"));
        }

        // 404 = déjà absent côté service : on considère la suppression effective (idempotent).
        if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Result.Success();
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return Result.Failure(Error.Failure("catalog.media.delete_failed",
            $"Échec de la suppression média ({(int)response.StatusCode}). {body}"));
    }

    /// <summary>Projection partielle de la réponse « MediaFile » du service.</summary>
    private sealed record MediaFileResponse(string? id, string? url, string? path, string? mimeType);
}
