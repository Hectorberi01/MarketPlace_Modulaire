using FluentValidation;
using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.Modules.Offers.Domain.Offers;

namespace Marketplace.Modules.Offers.Application.Offers.Commands.CreateOffer;

public sealed class CreateOfferCommandValidator : AbstractValidator<CreateOfferCommand>
{
    public CreateOfferCommandValidator()
    {
        RuleFor(c => c.ProductId).NotEmpty();
        RuleFor(c => c.SellerId).NotEmpty();
        RuleFor(c => c.Sku).NotEmpty().MaximumLength(64);
        // > 0, et non >= 0 : une offre gratuite n'a pas de sens (elle engage logistique,
        // commission et fidélité). L'invariant est aussi posé dans le domaine ; ce validator
        // donne juste un message d'erreur immédiat.
        RuleFor(c => c.SellerPrice).GreaterThan(0);
        // La devise n'avait AUCUNE règle ici, alors qu'elle pilote tout le calcul
        // monétaire de l'offre : commission, frais, prix acheteur, net reversé. Un
        // code inconnu passait, `Money` lui prêtait deux décimales par défaut, et
        // l'incohérence n'éclatait qu'à l'encaissement. L'invariant est également
        // posé dans le domaine (aucun appelant ne peut le contourner) ; cette règle
        // rend le refus immédiat et nomme les devises acceptées.
        RuleFor(c => c.Currency)
            .Must(Money.IsSupported)
            .WithMessage($"Devise non prise en charge. Devises acceptées : {string.Join(", ", Money.SupportedCurrencies)}.");
        RuleFor(c => c.ShipFromLocationId).NotEmpty();
        RuleFor(c => c.HandlingTime).GreaterThanOrEqualTo(0);
        // Insensible à la casse, comme le fait le handler (Enum.TryParse ignoreCase).
        // Le validator était strict sur la casse : l'app envoie « new » / « fbs » en
        // minuscules, ce qui était refusé AVANT même d'atteindre le handler.
        RuleFor(c => c.Condition)
            .Must(v => Enum.TryParse<OfferCondition>(v, ignoreCase: true, out _))
            .WithMessage("Condition invalide (New, Used, Refurbished).");
        RuleFor(c => c.FulfillmentType)
            .Must(v => Enum.TryParse<FulfillmentType>(v, ignoreCase: true, out _))
            .WithMessage("Mode de fulfillment invalide (Fbs, Fbp).");
    }
}
