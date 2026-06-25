namespace Marketplace.Modules.Catalog.Contracts;

public sealed record CategorySummary(
    Guid Id,
    Guid? ParentId,
    string Name,
    string Slug,
    string Path,
    string Status,
    string? ImageUrl);
