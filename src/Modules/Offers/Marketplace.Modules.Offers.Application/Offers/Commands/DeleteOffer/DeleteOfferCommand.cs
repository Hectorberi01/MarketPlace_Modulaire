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
///
/// EN REVANCHE, LES PANIERS EN COURS NE SONT PAS NETTOYÉS. `CartItem` porte un
/// `OfferId` et un prix figés à l'ajout, et rien sur le chemin d'achat ne revalide
/// l'offre : ni `CartPricer` à la valorisation, ni le placement de commande dans le
/// BFF Client. Une commande peut donc naître sur une offre supprimée, au
/// prix d'avant. Ce trou PRÉEXISTE à cette commande — une offre simplement mise en
/// pause produit déjà le même effet — mais il reste à combler côté Cart/Ordering, pas
/// ici : le module Offers n'a pas à connaître les paniers.
/// </summary>
public sealed record DeleteOfferCommand(Guid OfferId) : ICommand;
