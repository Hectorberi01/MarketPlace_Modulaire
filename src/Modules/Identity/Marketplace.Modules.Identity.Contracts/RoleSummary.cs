namespace Marketplace.Modules.Identity.Contracts;

public sealed record RoleSummary(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions);
