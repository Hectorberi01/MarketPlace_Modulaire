using FluentAssertions;
using Marketplace.Modules.Catalog.Application.Abstractions;
using Marketplace.Modules.Catalog.Application.Products.Commands.CreateProduct;
using Marketplace.Modules.Catalog.Domain.Products;
using Xunit;

namespace Catalog.UnitTests;

public class CreateProductCommandHandlerTests
{
    [Fact]
    public async Task Handle_cree_le_produit_et_renvoie_son_id()
    {
        var repository = new FakeProductRepository();
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateProductCommandHandler(repository, unitOfWork);

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
        var unitOfWork = new FakeUnitOfWork();
        var handler = new CreateProductCommandHandler(repository, unitOfWork);

        var command = new CreateProductCommand(Guid.NewGuid(), Guid.NewGuid(), "Doublon", "x");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        repository.Products.Should().ContainSingle();
        repository.Products.Single().Slug.Value.Should().Be("doublon-2");
        unitOfWork.SaveCount.Should().Be(1);
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

        public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
            => Task.FromResult(ExistingSlugs.Contains(slug));

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
