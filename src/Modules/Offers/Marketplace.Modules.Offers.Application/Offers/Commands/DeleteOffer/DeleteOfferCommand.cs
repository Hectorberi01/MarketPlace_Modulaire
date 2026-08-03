using Marketplace.BuildingBlocks.Application.Messaging;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.DeleteOffer;

/// <summary>
/// Retire définitivement une mise en vente.
///
/// À distinguer de <c>ChangeOfferStatusCommand("Paused")</c>, qui suspend la vente en
/// conservant l'offre : la suspension est un geste courant (rupture, congés), la
/// suppression est définitive et sert quand le vendeur cesse de proposer l'article.
///
/// Les commandes déjà passées ne sont PAS affectées : le module Ordering fige ses
/// propres lignes (SKU, prix payé, identifiant d'offre) à la commande et ne relit
/// jamais l'offre ensuite.
/// </summary>
public sealed record DeleteOfferCommand(Guid OfferId) : ICommand;
