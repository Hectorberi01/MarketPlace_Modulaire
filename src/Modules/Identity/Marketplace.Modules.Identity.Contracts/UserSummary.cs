namespace Marketplace.Modules.Identity.Contracts;

/// <summary>Vue publique d'un compte, exposée aux autres modules et au front.</summary>
public sealed record UserSummary(
    Guid Id,
    string FirstName,
    string LastName,
    string Email,
    string PhoneNumber,
    string Status,
    bool EmailVerified,
    bool MfaEnabled,
    IReadOnlyList<Guid> RoleIds,
    // Version des conditions générales acceptée par ce compte, et date. Null = rien
    // n'a jamais été accepté. C'est le CLIENT qui compare cette valeur à la version
    // qu'il embarque : le serveur n'a pas à connaître la rédaction en vigueur dans
    // chaque application publiée sur les stores.
    string? AcceptedTermsVersion = null,
    DateTime? AcceptedTermsOnUtc = null,
    // Renseignée = l'e-mail a été marqué vérifié PAR UN ADMINISTRATEUR, sur attestation,
    // et non par le titulaire cliquant un lien. La console doit pouvoir le montrer :
    // « Oui » et « Oui, sur parole » ne valent pas la même chose.
    DateTime? EmailVerifiedByAdminOnUtc = null);
