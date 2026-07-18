using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;

/// <summary>
/// Onboarde un vendeur rattaché à un compte Identity existant et vérifié.
/// <paramref name="Metadata"/> (infos société déclarées) est optionnelle et null
/// par défaut : l'onboarding admin ne la fournit pas, l'auto-inscription oui.
/// </summary>
public sealed record RegisterSellerCommand(
    Guid UserId,
    string ShopName,
    decimal CommissionRate = 0.10m,
    SellerCompanyInfo? Metadata = null) : ICommand<Guid>;
