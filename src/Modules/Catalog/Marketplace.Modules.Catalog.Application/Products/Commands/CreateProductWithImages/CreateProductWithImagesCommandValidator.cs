using FluentValidation;

namespace Marketplace.Modules.Catalog.Application.Products.Commands.CreateProductWithImages;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE CHEMIN DE CRÉATION RÉEL DE LA PLATEFORME N'AVAIT AUCUN VALIDATEUR.
///
/// `CreateProductWithImagesCommand` est la commande qu'empruntent la console vendeur
/// ET l'app mobile — autrement dit tous les produits de la plateforme. C'était le seul
/// des trois chemins de création/modification de produit sans `AbstractValidator`.
///
/// CE QUE COÛTAIT SON ABSENCE, ET CE N'EST PAS THÉORIQUE. Les longueurs n'étaient
/// contrôlées qu'au niveau des COLONNES PostgreSQL (nom 200, description 4000, GTIN et
/// EAN 14). Or le handler téléverse les images AVANT toute écriture en base :
///
///   1. le vendeur remplit la fiche, choisit huit photos, attend le détourage ;
///   2. les images partent chez le prestataire média — facturées, et rien ne les
///      supprime si la suite échoue ;
///   3. l'`INSERT` casse sur une chaîne trop longue ;
///   4. le vendeur reçoit une erreur de base de données, sans savoir quel champ,
///      et recommence tout.
///
/// Le `ValidationBehavior` court-circuite le handler AVANT l'étape 1. C'est tout
/// l'intérêt : le refus ne coûte plus ni attente, ni stockage objet orphelin.
///
/// LES MESSAGES SONT EN FRANÇAIS ET NOMMENT LE CHAMP. Ils remontent tels quels jusqu'au
/// bandeau d'erreur du vendeur (`ValidationBehavior` les joint dans le `detail` du
/// ProblemDetails) : un message par défaut de FluentValidation, en anglais, aurait
/// remplacé une erreur incompréhensible par une autre.
///
/// CE VALIDATEUR CONTRÔLE LA FORME, PAS LE MÉTIER. La catégorie feuille, le schéma
/// d'attributs et l'unicité du slug restent l'affaire du handler et de l'agrégat : ils
/// demandent la base, ce qu'un validateur ne doit pas faire.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class CreateProductWithImagesCommandValidator
    : AbstractValidator<CreateProductWithImagesCommand>
{
    public CreateProductWithImagesCommandValidator()
    {
        RuleFor(c => c.SellerId).NotEmpty();
        RuleFor(c => c.CategoryId).NotEmpty();

        // Les bornes reprennent EXACTEMENT les colonnes (`ProductConfiguration`).
        // Les faire diverger reviendrait à déplacer l'erreur, pas à la supprimer.
        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("Le nom du produit est obligatoire.")
            .MaximumLength(200).WithMessage("Le nom du produit doit faire 200 caractères au plus.");

        RuleFor(c => c.Description)
            .MaximumLength(4000).WithMessage("La description doit faire 4 000 caractères au plus.");

        // GTIN ET EAN. Le validateur voisin (`CreateProductCommandValidator`) contrôlait
        // le GTIN et OUBLIAIT l'EAN — deux champs de même type, de même longueur, dans
        // la même colonne. L'asymétrie a été corrigée là-bas aussi : un contrôle qui
        // couvre un champ sur deux est plus trompeur qu'un contrôle absent, parce qu'on
        // le croit complet.
        RuleFor(c => c.Gtin)
            .MaximumLength(14).WithMessage("Le GTIN doit faire 14 caractères au plus.")
            .When(c => !string.IsNullOrWhiteSpace(c.Gtin));

        RuleFor(c => c.Ean)
            .MaximumLength(14).WithMessage("L'EAN doit faire 14 caractères au plus.")
            .When(c => !string.IsNullOrWhiteSpace(c.Ean));

        // AU MOINS UNE IMAGE, ET C'EST LA COMMANDE QUI LE DIT.
        //
        // Le BFF vendeur le vérifie déjà et rend un message plus riche ; il passe donc
        // en premier et cette règle ne se déclenche pas pour lui. Elle vaut pour tout
        // AUTRE appelant : sans elle, un produit naîtrait sans photo, invisible en
        // vitrine, et personne ne saurait dire pourquoi.
        RuleFor(c => c.Images)
            .NotEmpty().WithMessage("Au moins une image est requise.");
    }
}
