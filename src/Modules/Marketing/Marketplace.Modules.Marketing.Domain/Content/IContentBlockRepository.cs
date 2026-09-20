namespace Marketplace.Modules.Marketing.Domain.Content;

public interface IContentBlockRepository
{
    Task AddAsync(ContentBlock block, CancellationToken cancellationToken = default);

    void Remove(ContentBlock block);

    Task<ContentBlock?> GetByIdAsync(ContentBlockId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Blocs d'un slot RÉELLEMENT VISIBLES à l'instant <paramref name="asOfUtc"/> : publiés,
    /// et dans leur fenêtre d'affichage. Triés par position.
    /// </summary>
    /// <param name="asOfUtc">
    /// L'instant de référence, EN UTC, passé par l'appelant.
    ///
    /// CE PARAMÈTRE N'A PAS DE VALEUR PAR DÉFAUT, ET C'EST DÉLIBÉRÉ. Un
    /// `DateTime asOfUtc = default` aurait valu `DateTime.MinValue` chez l'appelant qui
    /// l'oublie : toutes les fenêtres à venir auraient été considérées comme pas encore
    /// ouvertes, et le slot serait revenu vide sans la moindre erreur. Le compilateur
    /// force donc chaque appelant à dire de quelle horloge il parle.
    /// </param>
    Task<IReadOnlyList<ContentBlock>> ListPublishedBySlotAsync(string slot, DateTime asOfUtc, CancellationToken cancellationToken = default);

    /// <summary>Tous les blocs de contenu, tous slots et statuts (back-office admin / CMS).</summary>
    Task<IReadOnlyList<ContentBlock>> ListAllAsync(CancellationToken cancellationToken = default);
}
