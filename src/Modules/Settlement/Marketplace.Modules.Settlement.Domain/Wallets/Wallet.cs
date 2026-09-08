using Marketplace.BuildingBlocks.Domain.Primitives;
using Marketplace.BuildingBlocks.Domain.Results;

namespace Marketplace.Modules.Settlement.Domain.Wallets;

/// <summary>
/// Solde d'un sous-compte. Entité enfant du portefeuille : elle n'existe jamais seule,
/// et c'est l'agrégat qui garantit qu'il n'y en a qu'une par compte.
/// </summary>
public sealed class WalletAccountBalance : Entity<Guid>
{
    private WalletAccountBalance()
    {
    }

    internal WalletAccountBalance(Guid id, WalletAccount account)
        : base(id)
    {
        Account = account;
        Balance = 0m;
    }

    public WalletAccount Account { get; private set; }
    public decimal Balance { get; private set; }

    internal void Apply(decimal signedAmount) => Balance += signedAmount;
}

/// <summary>
/// Portefeuille d'un acteur — vendeur, client, livreur ou plateforme. Un seul agrégat
/// pour les quatre, distingués par <see cref="OwnerType"/> et par les sous-comptes que
/// leur accorde <see cref="WalletPolicy"/>.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// POURQUOI UN SEUL AGRÉGAT PLUTÔT QUE QUATRE.
///
/// Le grand livre (WalletTransaction) modélisait DÉJÀ le monde ainsi : une écriture y
/// porte un OwnerId, un OwnerType et un Account. Les soldes, eux, vivaient en colonnes
/// sur deux classes distinctes — SellerWallet et PlatformWallet. Deux représentations
/// du même fait, tenues à jour par deux chemins de code différents : rien ne garantissait
/// qu'elles restent d'accord, et rien ne le vérifiait.
///
/// Ici, chaque mouvement passe par Credit / Debit / Transfer, et chacun de ces trois
/// points écrit le solde. L'invariant « somme des écritures d'un compte = son solde »
/// devient vrai par construction — donc testable, et vérifiable en production.
///
/// Le prix à payer est assumé : les règles propres à chaque acteur ne sont plus portées
/// par des classes différentes mais par WalletPolicy. C'est un endroit de moins où se
/// tromper, pas un de plus.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class Wallet : AggregateRoot<WalletId>
{
    /// <summary>
    /// Propriétaire du portefeuille de la plateforme. Elle n'a pas d'identifiant métier :
    /// elle est unique, et cette constante lui en tient lieu. Valeur reprise à l'identique
    /// de l'ancien PlatformWallet pour que la reprise de données soit un simple copier.
    /// </summary>
    public static readonly Guid PlatformOwnerId = new("00000000-0000-0000-0000-0000000000A1");

    private readonly List<WalletAccountBalance> _accounts = new();

    private Wallet()
    {
    }

    private Wallet(WalletId id, WalletOwnerType ownerType, Guid ownerId, string currency)
        : base(id)
    {
        OwnerType = ownerType;
        OwnerId = ownerId;
        Currency = currency;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;

        // Les comptes sont matérialisés dès la création, à zéro. Les créer paresseusement
        // au premier mouvement obligerait chaque lecture à distinguer « compte absent » de
        // « compte à zéro » — deux notions qui n'ont aucune différence métier.
        foreach (var account in WalletPolicy.AccountsFor(ownerType))
        {
            _accounts.Add(new WalletAccountBalance(Guid.NewGuid(), account));
        }
    }

    public WalletOwnerType OwnerType { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Currency { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<WalletAccountBalance> Accounts => _accounts.AsReadOnly();

    public static Wallet Create(WalletOwnerType ownerType, Guid ownerId, string currency)
        => new(WalletId.New(), ownerType, ownerId, Normalize(currency));

    public static Wallet CreatePlatform(string currency)
        => Create(WalletOwnerType.Platform, PlatformOwnerId, currency);

    /// <summary>Solde d'un compte. Zéro si l'acteur ne possède pas ce compte.</summary>
    public decimal Balance(WalletAccount account)
        => _accounts.FirstOrDefault(a => a.Account == account)?.Balance ?? 0m;

    /// <summary>Crédite un compte. Un montant nul ou négatif n'est pas une erreur : rien ne bouge.</summary>
    public Result Credit(WalletAccount account, decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Success();
        }

        var target = Find(account);
        if (target is null)
        {
            return Result.Failure(WalletPolicy.UnknownAccount(OwnerType, account));
        }

        target.Apply(amount);
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Débite un compte. Échoue si le solde deviendrait négatif ET que la politique de
    /// l'acteur l'interdit — voir la règle du négatif dans <see cref="WalletPolicy"/>.
    /// </summary>
    public Result Debit(WalletAccount account, decimal amount)
    {
        if (amount <= 0m)
        {
            return Result.Success();
        }

        var target = Find(account);
        if (target is null)
        {
            return Result.Failure(WalletPolicy.UnknownAccount(OwnerType, account));
        }

        if (target.Balance < amount && !WalletPolicy.AllowsNegative(OwnerType, account))
        {
            return Result.Failure(Error.Validation(
                "wallet.insufficient_funds",
                $"Solde « {account} » insuffisant : {target.Balance} disponible, {amount} demandé."));
        }

        target.Apply(-amount);
        Touch();
        return Result.Success();
    }

    /// <summary>
    /// Déplace un montant d'un compte vers un autre, DANS LA LIMITE du solde source.
    ///
    /// Le plafonnement est délibéré : c'est la libération d'un gain à la livraison, et
    /// l'événement qui la déclenche peut être rejoué ou arriver après une contre-passation
    /// qui a déjà vidé le solde à venir. Transférer plus que ce qui existe créerait de
    /// l'argent. On déplace ce qu'il y a, et on renvoie combien.
    /// </summary>
    /// <returns>Le montant réellement déplacé.</returns>
    public decimal Transfer(WalletAccount from, WalletAccount to, decimal amount)
    {
        if (amount <= 0m)
        {
            return 0m;
        }

        var source = Find(from);
        var destination = Find(to);
        if (source is null || destination is null)
        {
            return 0m;
        }

        var moved = Math.Min(amount, source.Balance);
        if (moved <= 0m)
        {
            return 0m;
        }

        source.Apply(-moved);
        destination.Apply(moved);
        Touch();
        return moved;
    }

    /// <summary>
    /// Débite en CASCADE : on prend d'abord sur <paramref name="first"/>, puis le reste
    /// sur <paramref name="second"/> — quitte à rendre ce dernier négatif si la politique
    /// de l'acteur l'y autorise.
    ///
    /// C'est la contre-passation d'un gain remboursé. L'ordre n'est pas indifférent : on
    /// prend d'abord sur le solde à venir, parce que ce gain n'a pas encore été libéré et
    /// que le reprendre ne retire rien à l'acteur — il n'y avait pas encore droit. Le
    /// reliquat va sur le disponible, et c'est là que la dette devient réelle et visible.
    ///
    /// Deux montants sont renvoyés, et non un total : l'appelant écrit DEUX lignes au
    /// grand livre. Un débit unique et opaque ne permet jamais d'expliquer à un vendeur
    /// pourquoi son solde a bougé.
    /// </summary>
    public (decimal FromFirst, decimal FromSecond) DebitCascade(
        WalletAccount first, WalletAccount second, decimal amount)
    {
        if (amount <= 0m)
        {
            return (0m, 0m);
        }

        var primary = Find(first);
        var fallback = Find(second);
        if (primary is null || fallback is null)
        {
            return (0m, 0m);
        }

        var fromFirst = Math.Max(0m, Math.Min(amount, primary.Balance));
        if (fromFirst > 0m)
        {
            primary.Apply(-fromFirst);
        }

        var fromSecond = amount - fromFirst;
        if (fromSecond > 0m)
        {
            if (fallback.Balance < fromSecond && !WalletPolicy.AllowsNegative(OwnerType, second))
            {
                // L'acteur n'a pas le droit d'être en dette : on ne prend que ce qui existe.
                fromSecond = Math.Max(0m, fallback.Balance);
            }

            if (fromSecond > 0m)
            {
                fallback.Apply(-fromSecond);
            }
        }

        Touch();
        return (fromFirst, fromSecond);
    }

    private WalletAccountBalance? Find(WalletAccount account)
        => _accounts.FirstOrDefault(a => a.Account == account);

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;

    private static string Normalize(string currency)
        => string.IsNullOrWhiteSpace(currency) ? "XOF" : currency.Trim().ToUpperInvariant();
}
