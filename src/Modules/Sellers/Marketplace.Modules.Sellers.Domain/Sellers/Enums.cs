namespace Marketplace.Modules.Sellers.Domain.Sellers;

/// <summary>Statut commercial du vendeur (cf. dossier, Seller).</summary>
public enum SellerStatus
{
    Pending = 0,
    Active = 1,
    Suspended = 2,

    /// <summary>
    /// Fermeture demandée par le vendeur (suppression partielle) : ses produits
    /// sont retirés de la vente, mais le compte et son historique subsistent. La
    /// suppression définitive relève de l'admin ; le vendeur garde un accès
    /// restreint et peut demander une réactivation.
    /// </summary>
    Closed = 3,

    /// <summary>
    /// Le vendeur a demandé la réactivation de son compte fermé ; en attente de
    /// validation admin. (Nom court volontaire : la colonne Status est un varchar(20).)
    /// </summary>
    PendingReactivation = 4
}

/// <summary>Statut de la vérification KYB (Know Your Business).</summary>
public enum KybStatus
{
    NotStarted = 0,
    InReview = 1,
    Verified = 2,
    Rejected = 3
}

/// <summary>Type de pièce justificative KYB.</summary>
public enum KybDocumentType
{
    IdCard = 0,
    BusinessRegistry = 1,
    TaxId = 2,
    ProofOfAddress = 3
}

/// <summary>Canal de reversement (mobile money ou bancaire).</summary>
public enum PayoutProvider
{
    MtnMomo = 0,
    MoovMoney = 1,
    Wave = 2,
    BankAccount = 3,
    Celtis = 4
}
