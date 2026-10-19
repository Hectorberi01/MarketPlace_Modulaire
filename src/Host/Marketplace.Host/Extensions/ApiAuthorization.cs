namespace Marketplace.Host.Extensions;

/// <summary>
/// Rôles et garde-fous d'autorisation de l'API monolithe.
///
/// Les 5 hôtes partagent la MÊME clé de signature JWT : un jeton d'acheteur obtenu sur
/// un BFF est valide ici. La FallbackPolicy (Program.cs) ferme la porte aux anonymes,
/// pas aux authentifiés — d'où cette seconde couche, qui exige un RÔLE sur les groupes
/// sensibles.
///
/// RÈGLE À TENIR : tout nouveau groupe part de `MapAdminGroup`, `MapSellerGroup` ou
/// `MapAuthenticatedGroup`. Jamais de `MapGroup` nu.
/// </summary>
public static class ApiAuthorization
{
    public const string AdminRole = "Admin";
    public const string ModeratorRole = "Moderator";
    public const string SellerRole = "Seller";

    /// <summary>
    /// Groupe réservé au back-office (Admin / Modérateur) : catalogue de référence,
    /// utilisateurs, rôles, KYB, règlement, fraude, campagnes, analytique. Vendeurs et
    /// acheteurs passent par leurs BFF, avec les contrôles de propriété qui vont avec.
    /// </summary>
    public static RouteGroupBuilder MapAdminGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix)
            .RequireAuthorization(policy => policy.RequireRole(AdminRole, ModeratorRole));

    /// <summary>
    /// Groupe exigeant un utilisateur authentifié, sans rôle particulier. Pour les
    /// ressources dont le propriétaire est vérifié DANS le handler. L'authentification dit
    /// qui parle, pas ce qu'il a le droit de toucher.
    /// </summary>
    public static RouteGroupBuilder MapAuthenticatedGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix).RequireAuthorization();

    /// <summary>
    /// Groupe réservé aux vendeurs. Les cinq hôtes partageant émetteur, audience et clé de
    /// signature, un jeton d'acheteur ouvrirait sinon la surface vendeur de l'API. Les
    /// handlers vérifient ensuite que la boutique est bien la sienne.
    /// </summary>
    public static RouteGroupBuilder MapSellerGroup(this IEndpointRouteBuilder app, string prefix)
        => app.MapGroup(prefix).RequireAuthorization(policy => policy.RequireRole(SellerRole));
}
