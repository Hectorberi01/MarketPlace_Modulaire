using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Platform.Application.Abstractions;
using Marketplace.Modules.Platform.Contracts;
using Marketplace.Modules.Platform.Domain.Providers;

namespace Marketplace.Modules.Platform.Application.Providers;

/// <summary>
/// Catalogue des fournisseurs pilotables et des clés qu'ils exigent. L'écran
/// d'administration construit son formulaire à partir de là : ajouter un champ à un
/// adaptateur ne demande aucune modification de la console.
/// </summary>
public sealed record ListProviderCatalogQuery : IQuery<IReadOnlyList<ProviderCatalogEntry>>;

/// <summary>Fournisseurs enregistrés, secrets masqués. Canal optionnel.</summary>
public sealed record ListProvidersQuery(string? Channel = null) : IQuery<IReadOnlyList<ProviderSummary>>;

/// <summary>Journal des changements, du plus récent au plus ancien.</summary>
public sealed record ListProviderChangeLogQuery(int Limit = 100) : IQuery<IReadOnlyList<ProviderChangeLogEntry>>;

internal sealed class ListProviderCatalogQueryHandler
    : IQueryHandler<ListProviderCatalogQuery, IReadOnlyList<ProviderCatalogEntry>>
{
    public Task<Result<IReadOnlyList<ProviderCatalogEntry>>> Handle(
        ListProviderCatalogQuery query, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProviderCatalogEntry> entrees = ProviderCatalog.Descriptors
            .Select(d => new ProviderCatalogEntry(
                d.Code, d.DisplayName, d.Channel.ToString(),
                d.RequiredSettings, d.RequiredSecrets, d.OptionalSettings, d.SupportsSandbox))
            .ToList();

        return Task.FromResult(Result.Success(entrees));
    }
}

internal sealed class ListProvidersQueryHandler
    : IQueryHandler<ListProvidersQuery, IReadOnlyList<ProviderSummary>>
{
    private readonly IProviderRepository _repository;
    private readonly ISecretProtector _protector;

    public ListProvidersQueryHandler(IProviderRepository repository, ISecretProtector protector)
    {
        _repository = repository;
        _protector = protector;
    }

    public async Task<Result<IReadOnlyList<ProviderSummary>>> Handle(
        ListProvidersQuery query, CancellationToken cancellationToken)
    {
        ProviderChannel? canal = null;
        if (!string.IsNullOrWhiteSpace(query.Channel))
        {
            if (!Enum.TryParse<ProviderChannel>(query.Channel, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
            {
                return Result.Failure<IReadOnlyList<ProviderSummary>>(
                    Error.Validation("platform.channel_invalid", "Canal inconnu."));
            }

            canal = parsed;
        }

        var fournisseurs = await _repository.ListAsync(canal, cancellationToken);
        IReadOnlyList<ProviderSummary> resumes = fournisseurs.Select(p => ToSummary(p, _protector)).ToList();
        return Result.Success(resumes);
    }

    private static ProviderSummary ToSummary(Provider provider, ISecretProtector protector)
    {
        var descriptor = ProviderCatalog.Find(provider.Channel, provider.Code);
        var secrets = provider.ReadProtectedSecrets();

        // Les clés ATTENDUES sont celles du catalogue, pas celles présentes en base :
        // un secret jamais saisi doit apparaître à l'écran, vide, pour qu'on sache
        // qu'il manque. Ne lister que ce qui existe cacherait précisément le trou.
        var apercus = (descriptor?.RequiredSecrets ?? Array.Empty<string>())
            .ToDictionary(
                cle => cle,
                cle => secrets.TryGetValue(cle, out var valeur) ? protector.Preview(valeur) : string.Empty,
                StringComparer.Ordinal);

        return new ProviderSummary(
            provider.Id.Value,
            provider.Channel.ToString(),
            provider.Code,
            descriptor?.DisplayName ?? provider.Code,
            provider.Mode.ToString(),
            provider.IsEnabled,
            provider.Priority,
            provider.ReadSettings(),
            apercus,
            descriptor is null ? new[] { "adaptateur absent" } : provider.Missing(descriptor),
            provider.UpdatedAtUtc,
            provider.UpdatedByUserId);
    }
}

internal sealed class ListProviderChangeLogQueryHandler
    : IQueryHandler<ListProviderChangeLogQuery, IReadOnlyList<ProviderChangeLogEntry>>
{
    private readonly IProviderRepository _repository;

    public ListProviderChangeLogQueryHandler(IProviderRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IReadOnlyList<ProviderChangeLogEntry>>> Handle(
        ListProviderChangeLogQuery query, CancellationToken cancellationToken)
    {
        var limite = Math.Clamp(query.Limit, 1, 500);
        var lignes = await _repository.ListLogAsync(limite, cancellationToken);

        IReadOnlyList<ProviderChangeLogEntry> entrees = lignes
            .Select(l => new ProviderChangeLogEntry(
                l.Id, l.ProviderId.Value, l.Channel.ToString(), l.Code,
                l.Action.ToString(), l.Detail, l.ActorUserId, l.AtUtc))
            .ToList();

        return Result.Success(entrees);
    }
}
