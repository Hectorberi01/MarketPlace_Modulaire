namespace Marketplace.Modules.Identity.Domain.PaymentMethods;

/// <summary>Identifiant fort d'un moyen de paiement enregistré.</summary>
public readonly record struct PaymentMethodId(Guid Value)
{
    public static PaymentMethodId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
