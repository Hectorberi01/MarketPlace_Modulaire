namespace Marketplace.Modules.Marketing.Domain.Legal;

public interface ILegalDocumentRepository
{
    Task AddAsync(LegalDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Le document AVEC ses versions. C'est la seule lecture valable pour une écriture :
    /// les invariants de l'agrégat (un seul brouillon, numéro jamais réemployé,
    /// publication croissante) se défendent tous en regardant la collection entière.
    /// </summary>
    Task<LegalDocument?> GetByIdAsync(LegalDocumentId id, CancellationToken cancellationToken = default);

    /// <summary>Le document désigné par son adresse stable, versions comprises.</summary>
    Task<LegalDocument?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);

    /// <summary>Tous les documents, versions comprises (console d'administration).</summary>
    Task<IReadOnlyList<LegalDocument>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Une adresse est-elle déjà prise ? Vérifié avant création, pas après.</summary>
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);
}

public interface ILegalAcceptanceRepository
{
    Task AddAsync(LegalAcceptance acceptance, CancellationToken cancellationToken = default);

    /// <summary>
    /// L'acceptation déjà enregistrée pour cette personne ET cette version, s'il y en a
    /// une. C'est la garde d'idempotence : un client qui rejoue son appel ne doit pas
    /// produire une seconde ligne, ni surtout écraser la date d'origine — qui est
    /// exactement la donnée qu'on conserve.
    /// </summary>
    Task<LegalAcceptance?> FindAsync(
        Guid userId,
        LegalDocumentVersionId versionId,
        CancellationToken cancellationToken = default);

    /// <summary>Tout ce qu'une personne a accepté, le plus récent d'abord.</summary>
    Task<IReadOnlyList<LegalAcceptance>> ListByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>Les acceptations recueillies sur une version donnée (dossier de litige).</summary>
    Task<IReadOnlyList<LegalAcceptance>> ListByVersionAsync(
        LegalDocumentVersionId versionId,
        CancellationToken cancellationToken = default);
}
