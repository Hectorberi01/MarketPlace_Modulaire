using FluentValidation;

namespace Marketplace.Modules.Catalog.Application.Categories.Commands.ImportCategories;

/// <summary>
/// Garde-fous de volume. Les erreurs de CONTENU (segment invalide, profondeur
/// excessive) sont traitées ligne par ligne par le gestionnaire, qui les rapporte
/// sans interrompre l'import : un fichier de 300 lignes ne doit pas être rejeté en
/// bloc pour une coquille.
/// </summary>
public sealed class ImportCategoriesCommandValidator : AbstractValidator<ImportCategoriesCommand>
{
    /// <summary>
    /// Un fichier de taxonomie dépasse rarement quelques centaines de lignes.
    /// Au-delà, on tient tout l'arbre en mémoire dans une seule transaction : la
    /// limite protège autant la base que le temps de réponse.
    /// </summary>
    private const int MaxRows = 2000;

    public ImportCategoriesCommandValidator()
    {
        RuleFor(c => c.Rows)
            .NotEmpty().WithMessage("Le fichier ne contient aucune ligne exploitable.")
            .Must(r => r.Count <= MaxRows)
            .WithMessage($"Fichier trop volumineux : {MaxRows} lignes au maximum.");

        RuleForEach(c => c.Rows).ChildRules(row =>
        {
            row.RuleFor(r => r.Path).NotEmpty().MaximumLength(1000);
            row.RuleFor(r => r.ImageUrl)
                .MaximumLength(2000)
                .When(r => !string.IsNullOrWhiteSpace(r.ImageUrl));
        });
    }
}
