namespace Marketplace.Modules.Catalog.Contracts;

public sealed record BrandSummary(
    Guid Id,
    string Name,
    string Slug,
    string Status,
    string? LogoUrl,
    string? Description);
