namespace Marketplace.Modules.Sellers.Domain.Sellers;

/// <summary>
/// Informations société du vendeur, saisies à l'auto-inscription et stockées en
/// jsonb (colonne nullable, null par défaut).
///
/// DONNÉE DÉCLARATIVE, PAS UNE PREUVE. Ce n'est pas parce qu'un vendeur a tapé un
/// numéro RCCM qu'il est vérifié : la vérification reste le KYB (pièces
/// justificatives + validation admin). Cette structure ne fait que porter ce que le
/// vendeur déclare de lui-même, pour pré-remplir le dossier et l'afficher.
///
/// Tous les champs sont optionnels : la structure est un GABARIT typé, pas un
/// contrat rigide. Un vendeur peut s'inscrire d'abord et compléter ensuite.
/// </summary>
public sealed record SellerCompanyInfo(
    string? LegalName = null,    // raison sociale
    string? Rccm = null,         // registre du commerce et du crédit mobilier
    string? Ifu = null,          // identifiant fiscal unique
    string? Address = null,
    // Commune de la boutique : CODE d'une des 77 communes (« abomey-calavi »), pas un
    // libellé libre. Remplace l'ancien champ « City », où « Cotonou », « cotonou » et
    // « COTONOU » cohabitaient. Reste facultatif comme le reste de cette structure : c'est
    // du déclaratif de pré-remplissage, pas une adresse de livraison.
    string? Commune = null,
    string? Activity = null,     // secteur / activité déclarée
    string? ManagerName = null,  // gérant / représentant légal
    string? Phone = null);
