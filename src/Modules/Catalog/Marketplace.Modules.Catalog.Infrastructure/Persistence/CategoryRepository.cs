using Microsoft.EntityFrameworkCore;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence;

internal sealed class CategoryRepository : ICategoryRepository
{
    private readonly CatalogDbContext _dbContext;

    public CategoryRepository(CatalogDbContext dbContext)
        => _dbContext = dbContext;

    public async Task AddAsync(Category category, CancellationToken cancellationToken = default)
        => await _dbContext.Categories.AddAsync(category, cancellationToken);

    public void Remove(Category category)
        => _dbContext.Categories.Remove(category);

    public async Task<Category?> GetByIdAsync(CategoryId id, CancellationToken cancellationToken = default)
        => await _dbContext.Categories.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Category>> ListAllAsync(CancellationToken cancellationToken = default)
        => await _dbContext.Categories
            .AsNoTracking()
            .OrderBy(c => c.Path)
            .ToListAsync(cancellationToken);

    public async Task<bool> PathExistsAsync(
        string path, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Categories.Where(c => c.Path == path);

        if (excludeId is { } id)
        {
            var categoryId = new CategoryId(id);
            query = query.Where(c => c.Id != categoryId);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Category>> ListAncestorsAsync(
        string path, CancellationToken cancellationToken = default)
    {
        var chemins = Category.AncestorPathsOf(path);
        if (chemins.Count == 0)
        {
            // Catégorie racine : aucune requête. `IN ()` sur une liste vide est du
            // travail pour rien, et certains fournisseurs le traduisent mal.
            return Array.Empty<Category>();
        }

        // `ORDER BY path` suffit à rendre l'ordre racine → parent direct : un chemin
        // parent est un préfixe du chemin enfant, donc plus court, donc classé avant.
        // L'ordre est contractuel — voir ICategoryRepository.
        // AsNoTracking : les ancêtres ne sont JAMAIS mutés par cette voie — on les lit
        // pour composer un schéma. Les suivre ferait payer un instantané pour rien.
        return await _dbContext.Categories
            .AsNoTracking()
            .Where(c => chemins.Contains(c.Path))
            .OrderBy(c => c.Path)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LES SOUS-CATÉGORIES ARCHIVÉES NE COMPTENT PAS.
    ///
    /// Ce test décide si un produit peut naître ici. Sans le filtre, une catégorie dont
    /// l'UNIQUE sous-catégorie a été archivée restait « intermédiaire » à jamais : plus
    /// aucune création possible, et le sélecteur vendeur la masquait — sans message.
    /// Archiver une sous-catégorie condamnait donc son parent, ce que personne n'a
    /// jamais voulu dire en archivant.
    ///
    /// LES BROUILLONS, EUX, COMPTENT. Une sous-catégorie en préparation sera ouverte :
    /// y ranger les produits dès maintenant évite d'avoir à les déplacer — ce que la
    /// plateforme ne sait pas faire — le jour de son ouverture.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public async Task<bool> HasChildrenAsync(Guid categoryId, CancellationToken cancellationToken = default)
        => await _dbContext.Categories.AnyAsync(
            c => c.ParentId == categoryId && c.Status != CategoryStatus.Archived, cancellationToken);

    public async Task<IReadOnlyList<Category>> ListDescendantsAsync(
        string path, CancellationToken cancellationToken = default)
    {
        // Le séparateur final délimite la branche : sans lui, « /animaux/chiens »
        // ramasserait « /animaux/chiens-de-chasse », qui n'en est pas un descendant.
        var prefix = path.TrimEnd('/') + "/";

        return await _dbContext.Categories
            .Where(c => c.Path.StartsWith(prefix))
            .OrderBy(c => c.Path)
            .ToListAsync(cancellationToken);
    }
}
