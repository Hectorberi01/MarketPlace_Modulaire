namespace Marketplace.Api.Extensions;

/// <summary>
/// Rôles et garde-fous d'autorisation de l'API monolithe.
///
/// ─────────────────────────────────────────────────────────────────────────────
/// POURQUOI CE FICHIER EXISTE
///
/// Les 5 hôtes (API + 4 BFF) partagent la MÊME clé de signature JWT. Un jeton obtenu
/// par n'importe quel acheteur sur /mobile/auth/login est donc parfaitement valide
/// sur cette API.
///
/// La FallbackPolicy (voir Program.cs) ferme la porte aux ANONYMES. Elle ne dit rien
/// des utilisateurs authentifiés : sans rôle exigé, un acheteur lambda pouvait
/// changer le prix d'un vendeur, manipuler le stock, valider un dossier KYB ou
/// toucher au règlement.
///
/// D'où cette seconde couche : les groupes sensibles exigent un RÔLE.
///
/// RÈGLE À TENIR : tout nouveau groupe de l'API part de `MapAdminGroup` ou
/// `MapAuthenticatedGroup`. Jamais de `MapGroup` nu. Le jour où l'on en écrira un par
/// distraction, la FallbackPolicy le rendra au moins authentifié — mais elle ne le
/// rendra pas administrateur, et c'est bien là le piège qu'on vient de refermer.
/// ─────────────────────────────────────────────────────────────────────────────
/// </summary>
public static class ApiAuthorization
{
    public const string AdminRole = "Admin";
    public const string ModeratorRole = "Moderator";
    public const string SellerRole = "Seller";

    /// <summary>
    /// Groupe réservé au BACK-OFFICE (Admin / Modérateur).
    ///
    /// À utiliser pour tout ce qui gouverne la plateforme : catalogue de référence,
    /// utilisateurs, rôles, KYB, règlement, fraude, campagnes, analytique.
    ///
    /// Ces opérations sont accessibles aux vendeurs et aux acheteurs par leurs BFF
    /// respectifs, avec les contrôles de propriété qui vont avec. L'API monolithe,
    /// elle, n'a aucune raison de leur être ouverte.
    /// </summary>
    public static RouteGroupBuilder MapAdminGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix)
            .RequireAuthorization(policy => policy.RequireRole(AdminRole, ModeratorRole));

    /// <summary>
    /// Groupe exigeant simplement un utilisateur AUTHENTIFIÉ (sans rôle particulier).
    ///
    /// Pour les ressources dont le propriétaire est vérifié DANS le handler (une
    /// commande n'est lisible que par son acheteur, un avis n'est modifiable que par
    /// son auteur…). L'authentification ne suffit jamais seule : elle dit QUI parle,
    /// pas ce qu'il a le droit de toucher.
    /// </summary>
    public static RouteGroupBuilder MapAuthenticatedGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix).RequireAuthorization();

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════
    /// GROUPE RÉSERVÉ AUX VENDEURS.
    ///
    /// La constante <see cref="SellerRole"/> existait depuis toujours et n'avait
    /// AUCUN usage dans le dépôt : le groupe `/api/sellers` partait de
    /// <see cref="MapAuthenticatedGroup"/>, donc sans rôle. Or les cinq hôtes
    /// partagent émetteur, audience et clé de signature — un jeton d'acheteur
    /// ouvrait la surface vendeur de l'API.
    ///
    /// L'authentification ne suffit pas plus ici qu'ailleurs : elle dit qui parle.
    /// Les handlers vérifient ensuite que la boutique est bien la sienne.
    /// ═══════════════════════════════════════════════════════════════════════════
    /// </summary>
    public static RouteGroupBuilder MapSellerGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix).RequireAuthorization(policy => policy.RequireRole(SellerRole));
}
