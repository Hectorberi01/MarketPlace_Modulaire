namespace Marketplace.Modules.Identity.Domain.Users;

/// <summary>Statut d'un compte (cf. dossier, User).</summary>
public enum UserStatus
{
    PendingVerification = 0,
    Active = 1,
    Suspended = 2,

    /// <summary>
    /// Compte SUPPRIMÉ à la demande de son titulaire — anonymisé, et définitivement
    /// inutilisable.
    ///
    /// ─────────────────────────────────────────────────────────────────────────────
    /// POURQUOI « ANONYMISÉ » ET NON « EFFACÉ »
    ///
    /// La ligne n'est PAS supprimée de la base, et ce n'est pas une paresse : c'est une
    /// obligation. Les commandes doivent être conservées pour la comptabilité et le
    /// fisc — plusieurs années. Or une commande référence son acheteur. Effacer la
    /// ligne « utilisateur » briserait ces références et rendrait vos livres
    /// incohérents, donc inexploitables en cas de contrôle.
    ///
    /// Ce que la loi et Apple exigent, ce n'est pas la disparition de la LIGNE, c'est
    /// la disparition des DONNÉES PERSONNELLES. C'est exactement ce que fait
    /// User.Anonymize() : le nom, l'e-mail et le téléphone sont remplacés, le mot de
    /// passe est détruit, la connexion devient impossible. Il reste une coquille
    /// comptable, qui ne désigne plus personne.
    ///
    /// IRRÉVERSIBLE. Aucune méthode ne fait sortir de cet état — c'est délibéré :
    /// les données d'origine n'existent plus, il n'y a rien à restaurer.
    /// ─────────────────────────────────────────────────────────────────────────────
    /// </summary>
    Deleted = 3
}
