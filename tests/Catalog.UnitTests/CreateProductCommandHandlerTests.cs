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
    public async Task Handle_refuse_un_slug_deja_pris()
    {
        var repository = new FakeProductRepository { SlugAlreadyExists = true };
        var handler = new CreateProductCommandHandler(repository, new FakeUnitOfWork());

        var command = new CreateProductCommand(Guid.NewGuid(), Guid.NewGuid(), "Doublon", "x");

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("catalog.product.slug_taken");
    }

    private sealed class FakeProductRepository : IProductRepository
    {
        public List<Product> Products { get; } = new();
        public bool SlugAlreadyExists { get; init; }

        public Task AddAsync(Product product, CancellationToken cancellationToken = default)
        {
            Products.Add(product);
            return Task.CompletedTask;
        }

        public Task<Product?> GetByIdAsync(ProductId id, CancellationToken cancellationToken = default)
            => Task.FromResult(Products.FirstOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<Product>> ListBySellerAsync(Guid sellerId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Product>>(Products.Where(p => p.SellerId == sellerId).ToList());

        public Task<IReadOnlyList<Product>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Product>>(Products.Take(take).ToList());

        public Task<(IReadOnlyList<Product> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
            int page, int pageSize, string? search, ProductStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default)
            => Task.FromResult(((IReadOnlyList<Product>)Products.ToList(), Products.Count, (IReadOnlyDictionary<string, int>)new Dictionary<string, int>()));

        public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
            => Task.FromResult(SlugAlreadyExists);

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
