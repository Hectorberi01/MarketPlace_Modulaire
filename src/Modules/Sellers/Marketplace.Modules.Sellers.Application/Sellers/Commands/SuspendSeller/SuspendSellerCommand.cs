using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.SuspendSeller;

/// <summary>
/// Suspend un vendeur (Admin).
/// </summary>
/// <param name="Reason">
/// Motif transmis au vendeur dans la notification de suspension. Le domaine
/// l'accepte nul (une suspension d'urgence ne doit pas être bloquée par un champ) ;
/// le validateur de cette commande l'exige, parce qu'une sanction sans motif est une
/// sanction que sa cible ne peut pas corriger.
/// </param>
public sealed record SuspendSellerCommand(Guid SellerId, string? Reason = null) : ICommand;
