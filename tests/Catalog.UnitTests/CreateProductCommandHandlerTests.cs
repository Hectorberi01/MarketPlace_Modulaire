using FluentAssertions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Categories;
using Marketplace.Modules.Catalog.Application.Products;
using Marketplace.Modules.Catalog.Application.Products.Commands.CreateProduct;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;
using Xunit;

namespace Catalog.UnitTests;

public class CreateProductCommandHandlerTests
{
    [Fact]
    public async Task Handle_cree_le_produit_et_renvoie_son_id()
    {
        var repository = new FakeProductRepository();
        var categories = FakeCategoryRepository.SansSchema();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateProductCommandHandler(repository, Garde(categories), unitOfWork);

        var command = new CreateProductCommand(Guid.NewGuid(), Guid.NewGuid(), "Tongs plage", "Confort");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);
        repository.Products.Should().ContainSingle();
        unitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_deduplique_un_slug_deja_pris()
    {
        // Le slug de base « doublon » est déjà pris : on ne refuse plus, on suffixe.
        var repository = new FakeProductRepository();
        repository.ExistingSlugs.Add("doublon");
        var categories = FakeCategoryRepository.SansSchema();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateProductCommandHandler(repository, Garde(categories), unitOfWork);

        var command = new CreateProductCommand(Guid.NewGuid(), Guid.NewGuid(), "Doublon", "x");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.Products.Should().ContainSingle();
        repository.Products.Single().Slug.Value.Should().Be("doublon-2");
        unitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_refuse_une_categorie_inexistante()
    {
        // `Product.Create` ne contrôlait que `categoryId != Guid.Empty`, et aucun
        // handler ne chargeait la catégorie : un produit pouvait naître sur un rayon
        // qui n'existe pas, invisible dans la navigation et actif malgré tout.
        var repository = new FakeProductRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateProductCommandHandler(
            repository, Garde(FakeCategoryRepository.Introuvable()), unitOfWork);

        var command = new CreateProductCommand(Guid.NewGuid(), Guid.NewGuid(), "Tongs plage", "Confort");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.product.category_not_found");
        repository.Products.Should().BeEmpty();
        unitOfWork.SaveCount.Should().Be(0);
    }

    [Fact]
    public async Task Handle_refuse_un_attribut_obligatoire_manquant()
    {
        var repository = new FakeProductRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateProductCommandHandler(
            repository, Garde(FakeCategoryRepository.AvecSchemaChaussures()), unitOfWork);

        var command = new CreateProductCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Tongs plage", "Confort",
            Attributes: new Dictionary<string, string> { ["pointure"] = "42" });

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.product.attributes_invalid");
        repository.Products.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_normalise_les_cles_sur_celles_du_schema()
    {
        // La moitié utile que la seule validation ne fait pas : sans elle, « Couleur »
        // et « couleur » donnent deux facettes de recherche pour une même notion.
        var repository = new FakeProductRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateProductCommandHandler(
            repository, Garde(FakeCategoryRepository.AvecSchemaChaussures()), unitOfWork);

        var command = new CreateProductCommand(
            Guid.NewGuid(), Guid.NewGuid(), "Tongs plage", "Confort",
            Attributes: new Dictionary<string, string> { ["Pointure"] = "42", ["COULEUR"] = "noir" });

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var product = repository.Products.Single();
        product.Attributes.Should().ContainKey("pointure").And.ContainKey("couleur");
        product.Attributes.Should().NotContainKey("Pointure").And.NotContainKey("COULEUR");
    }

    /// <summary>
    /// Le garde-fou, avec son résolveur de branche.
    ///
    /// `ProductAttributeGuard` compose désormais le schéma de la catégorie avec ceux de
    /// ses ancêtres : il prend un `CategorySchemaResolver`, qui lit le même dépôt. Les
    /// catégories de ces tests sont des RACINES sans ancêtre, donc le schéma composé est
    /// exactement celui de la catégorie — ces tests continuent de vérifier ce qu'ils
    /// vérifiaient. L'héritage a ses propres tests.
    /// </summary>
    private static ProductAttributeGuard Garde(ICategoryRepository categories)
        => new(categories, new CategorySchemaResolver(categories));

    /// <summary>
    /// Catégorie servie au garde-fou d'attributs. Elle est renvoyée quel que soit
    /// l'identifiant demandé : le garde ne s'en sert que pour lire un statut et un
    /// schéma, jamais pour vérifier une identité.
    /// </summary>
    private sealed class FakeCategoryRepository : ICategoryRepository
    {
        private const string SchemaChaussures = """
        {
          "attributes": [
            { "key": "pointure", "label": "Pointure", "type": "number", "required": true },
            { "key": "couleur",  "label": "Couleur",  "type": "enum", "required": true,
              "values": ["rouge", "bleu", "noir"] }
          ]
        }
        """;

        private readonly Category? _category;

        private FakeCategoryRepository(Category? category) => _category = category;

        public static FakeCategoryRepository SansSchema()
            => new(Category.Create("Chaussures").Value);

        public static FakeCategoryRepository AvecSchemaChaussures()
            => new(Category.Create("Chaussures", null, null, null, SchemaChaussures).Value);

        public static FakeCategoryRepository Introuvable() => new(null);

        public Task<Category?> GetByIdAsync(CategoryId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_category);

        public Task AddAsync(Category category, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public void Remove(Category category)
        {
        }

        public Task<IReadOnlyList<Category>> ListAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Category>>(
                _category is null ? Array.Empty<Category>() : new List<Category> { _category });

        public Task<bool> PathExistsAsync(string path, Guid? excludeId = null, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<IReadOnlyList<Category>> ListDescendantsAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Category>>([]);

        // La catégorie de ces tests est une RACINE sans enfant : aucun ancêtre à
        // composer, et le contrôle de feuille passe. C'est ce qui fait que ces tests
        // continuent de porter sur ce qu'ils portaient.
        public Task<IReadOnlyList<Category>> ListAncestorsAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Category>>([]);

        public Task<bool> HasChildrenAsync(Guid categoryId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class FakeProductRepository : IProductRepository
    {
        public List<Product> Products { get; } = new();

        /// Slugs considérés comme DÉJÀ pris (le handler doit alors dédupliquer).
        public HashSet<string> ExistingSlugs { get; } = new();

        public Task AddAsync(Product product, CancellationToken cancellationToken = default)
        {
            Products.Add(product);
            return Task.CompletedTask;
        }

        public Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default)
            => Task.FromResult(Products.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<Product>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Product>>(Products.Where(p => p.SellerId == sellerId).ToList());

        public Task<IReadOnlyList<Product>> ListBySellerForUpdateAsync(Guid sellerId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Product>>(Products.Where(p => p.SellerId == sellerId).ToList());

        public Task<IReadOnlyList<Product>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Product>>(Products.Take(take).ToList());

        public Task<(IReadOnlyList<Product> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
            int page, int pageSize, string? search, ProductStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default)
            => Task.FromResult(((IReadOnlyList<Product>)Products.ToList(), Products.Count, (IReadOnlyDictionary<string, int>)new Dictionary<string, int>()));

        public Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, int>>(
                Products.GroupBy(p => p.Status).ToDictionary(g => g.Key.ToString(), g => g.Count()));

        public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
            => Task.FromResult(ExistingSlugs.Contains(slug));

        // Sert au contrôle de compatibilité d'un schéma avec les fiches existantes.
        // Ces tests ne le sollicitent pas : aucune fiche à confronter.
        public Task<IReadOnlyList<IReadOnlyDictionary<string, string>>> ListAttributesByCategoriesAsync(
            IReadOnlyCollection<Guid> categoryIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<IReadOnlyDictionary<string, string>>>([]);

        public void Remove(Product product) => Products.Remove(product);
    }

    private sealed class FakeUnitOfWork : ICatalogUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }
}
