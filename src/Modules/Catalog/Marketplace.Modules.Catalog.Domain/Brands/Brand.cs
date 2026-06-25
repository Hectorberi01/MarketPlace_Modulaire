using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Catalog.Domain.Brands.Events;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Domain.Brands;

public enum BrandStatus
{
    Pending = 0,
    Active = 1,
    Archived = 2
}

/// <summary>
/// Marque, désormais entité à part entière (et non un simple champ texte).
/// Référencée par les produits, mutualisée entre vendeurs (cf. dossier, Brand).
/// Une nouvelle marque démarre en Pending (modération).
/// </summary>
public sealed class Brand : AggregateRoot<BrandId>
{
    private Brand()
    {
    }

    private Brand(BrandId id, string name, Slug slug, string? logoUrl, string? description)
        : base(id)
    {
        Name = name;
        Slug = slug;
        LogoUrl = logoUrl;
        Description = description;
        Status = BrandStatus.Pending;

        Raise(new BrandCreatedDomainEvent(id.Value, name, slug.Value));
    }

    public string Name { get; private set; } = default!;
    public Slug Slug { get; private set; } = default!;
    public string? LogoUrl { get; private set; }
    public string? Description { get; private set; }
    public BrandStatus Status { get; private set; }

    public static Result<Brand> Create(string name, string? logoUrl = null, string? description = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Error.Validation("catalog.brand.name_required", "Le nom de la marque est obligatoire.");
        }

        var slugResult = Slug.Create(name);
        if (slugResult.IsFailure)
        {
            return Result.Failure<Brand>(slugResult.Error);
        }

        return new Brand(
            BrandId.New(),
            name.Trim(),
            slugResult.Value,
            string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim(),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim());
    }

    /// <summary>Approuve la marque après modération (Pending -> Active). C'est l'action « publier ».</summary>
    public Result Approve()
    {
        if (Status == BrandStatus.Archived)
        {
            return Result.Failure(Error.Conflict("catalog.brand.archived", "Une marque archivée ne peut pas être approuvée."));
        }

        Status = BrandStatus.Active;
        return Result.Success();
    }

    /// <summary>Archive la marque (la retire du catalogue actif).</summary>
    public Result Archive()
    {
        Status = BrandStatus.Archived;
        return Result.Success();
    }

    /// <summary>
    /// Met à jour le nom (slug recalculé), le logo et la description. L'unicité
    /// éventuelle du slug est vérifiée par l'Application.
    /// </summary>
    public Result Update(string name, string? logoUrl, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure(Error.Validation("catalog.brand.name_required", "Le nom de la marque est obligatoire."));
        }

        var slugResult = Slug.Create(name);
        if (slugResult.IsFailure)
        {
            return Result.Failure(slugResult.Error);
        }

        Name = name.Trim();
        Slug = slugResult.Value;
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();

        return Result.Success();
    }
}
