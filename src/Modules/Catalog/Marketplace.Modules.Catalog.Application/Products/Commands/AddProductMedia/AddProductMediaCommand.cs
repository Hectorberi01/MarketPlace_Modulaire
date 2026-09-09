using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.AddProductMedia;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// ATTACHE UN MÉDIA DÉJÀ HÉBERGÉ. AUCUNE CLÉ DE STOCKAGE N'EST ACCEPTÉE ICI.
///
/// Cette commande portait un `ExternalId` fourni par le client. Cet identifiant sert
/// de CLÉ DE SUPPRESSION sur le stockage objet, et l'adaptateur R2 acceptait aussi
/// bien une clé qu'une URL publique — dont il extrayait la clé. Les URL des images
/// produit étant publiques, la chaîne était complète :
///
///   le vendeur A lit l'URL d'une image du vendeur B → il l'attache comme
///   `externalId` sur SON PROPRE produit, ce qui passe tous les contrôles de
///   propriété → il supprime ce média → l'objet de B disparaît du stockage.
///
/// La victime se retrouvait avec des images mortes sur ses fiches, sans trace ni
/// erreur : la suppression est volontairement tolérante à l'échec.
///
/// Un média attaché par URL n'appartient pas à la plateforme au sens du stockage :
/// il n'a donc PAS de clé, et la suppression ne doit rien tenter à distance. Seul
/// <c>UploadProductImageCommand</c> produit une clé, parce que c'est lui qui écrit
/// l'objet — et elle vient alors du service de stockage, jamais d'une requête.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record AddProductMediaCommand(
    Guid ProductId,
    string Url,
    string Type = "Image",
    string? AltText = null,
    bool IsPrimary = false) : ICommand<Guid>;
