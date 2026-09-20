namespace Marketplace.Modules.Marketing.Contracts;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════════
/// LES EMPLACEMENTS DE CONTENU, ÉCRITS UNE SEULE FOIS.
///
/// Un slot est une chaîne libre : le domaine accepte n'importe quelle valeur non vide.
/// C'est ce qui rend le système souple, et c'est aussi ce qui rend une faute de frappe
/// INDÉTECTABLE. « home_banners » au lieu de « home_banner » ne provoque aucune erreur :
/// le bloc est créé, il est publié, la console le montre en ligne, et le carrousel reste
/// vide. Rien ni personne ne signale l'écart.
///
/// Le même piège s'était présenté côté juridique et avait été traité de la même façon :
/// une table des emplacements servis, confrontée aux deux BFF.
///
/// TOUTE VALEUR AJOUTÉE ICI DOIT L'ÊTRE AUSSI dans `EMPLACEMENTS_CONTENU`
/// (`hba-express-web/apps/admin/src/types/admin.ts`), qui est ce que l'administrateur
/// voit. Les deux listes décrivent la même réalité de deux côtés de la frontière.
/// ═══════════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class ContentSlots
{
    /// <summary>
    /// Carrousel de la page d'accueil, vitrine ET application mobile acheteur.
    /// Contenu attendu : un JSON { imageUrl, linkUrl, title } par bloc.
    /// Servi par <c>GET /client/home/banners</c>.
    /// </summary>
    public const string HomeBanner = "home_banner";

    /// <summary>
    /// Questions fréquentes de la page d'aide acheteur.
    /// Contenu attendu : un JSON { question, answer } par bloc.
    /// Servi par <c>GET /client/content/help_faq</c>, avec une FAQ de repli si aucun
    /// bloc n'est publié — c'est le SEUL emplacement qui a un repli.
    /// </summary>
    public const string HelpFaq = "help_faq";

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// ANNONCES : LA PORTÉE EST L'EMPLACEMENT, PAS UN CHAMP À INTERPRÉTER.
    ///
    /// Une annonce n'a pas de champ « où m'afficher ». C'est son emplacement qui le dit,
    /// et chaque surface lit le sien. L'alternative — un emplacement unique et un champ
    /// de portée — obligerait CHAQUE client à filtrer correctement une valeur qui ne le
    /// concerne pas : le jour où l'un d'eux se trompe, une annonce destinée au tunnel de
    /// paiement s'affiche sur toutes les pages, et rien dans le serveur ne peut
    /// l'empêcher.
    ///
    /// IL N'Y A PAS D'EMPLACEMENT VENDEUR POUR L'INSTANT. Il en faudra un, mais le
    /// déclarer avant que quoi que ce soit ne le lise créerait précisément l'emplacement
    /// fantôme que cette classe existe pour empêcher : l'administrateur y publierait, et
    /// personne ne le verrait jamais.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public const string AnnouncementGlobal = "announcement_global";

    /// <summary>
    /// Panier et tunnel de commande UNIQUEMENT.
    ///
    /// Emplacement séparé parce que ce qui s'y affiche obéit à une autre règle : au-dessus
    /// d'un formulaire de paiement, une promotion est une nuisance, mais une panne de
    /// paiement est l'information la plus utile de la page. Les mélanger dans un seul
    /// emplacement revient à confier ce tri au rédacteur, à chaque annonce.
    /// </summary>
    public const string AnnouncementCheckout = "announcement_checkout";
}
