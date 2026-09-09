using System.Security.Claims;
using Marketplace.Modules.Delivery.Contracts;

namespace Marketplace.Bff.Driver.Endpoints;

/// <summary>
/// Utilitaires partagés de la surface /driver.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// PAS DE RÔLE « COURSIER », ET C'EST DÉLIBÉRÉ.
///
/// Il aurait été naturel d'ajouter un rôle au jeton, comme le BFF Vendeur exige
/// « Seller ». Mais un rôle est une donnée RECOPIÉE : il faudrait le poser à
/// l'activation du coursier, le retirer à sa suspension, et il vivrait dans un jeton
/// déjà émis, encore valide plusieurs heures après cette suspension. Un coursier suspendu
/// continuerait d'être vu comme coursier par le serveur, exactement le temps que dure son
/// jeton — c'est-à-dire au pire moment.
///
/// ÊTRE ENREGISTRÉ COMME COURSIER EST L'AUTORISATION. On lit l'utilisateur du jeton, on
/// demande au module Delivery s'il a un compte coursier, et la réponse est vraie au moment
/// où on la pose. Rien à synchroniser, rien qui puisse dériver.
///
/// La contrepartie est que le contrôle vit dans CHAQUE route et non à la porte. D'où
/// <see cref="WithCourierAsync"/> : un seul chemin, et aucune route qui manipule le
/// portefeuille ne résout le coursier autrement.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal static class DriverBff
{
    /// <summary>
    /// Groupe protégé de la surface driveur : authentifié, sans exigence de rôle.
    /// L'appartenance réelle est vérifiée route par route — voir ci-dessus.
    /// </summary>
    public static RouteGroupBuilder MapDriverGroup(this IEndpointRouteBuilder app, string prefix, string tag)
        => app.MapGroup(prefix).WithTags(tag).RequireAuthorization();

    /// <summary>Identifiant du COMPTE connecté (claim « sub » / NameIdentifier).</summary>
    public static Guid? CurrentUserId(ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal.FindFirstValue("sub");
        return Guid.TryParse(sub, out var id) ? id : null;
    }

    /// <summary>
    /// Résout le coursier depuis le JETON, jamais depuis la requête.
    ///
    /// Accepter un identifiant de coursier fourni par l'appelant reviendrait à laisser
    /// n'importe qui consulter — et vider — le portefeuille de n'importe quel livreur.
    ///
    /// DEUX IDENTIFIANTS, ET C'EST LE SECOND QU'ON REND. Le jeton porte le COMPTE
    /// utilisateur ; le portefeuille est adressé par l'identifiant de BÉNÉFICIAIRE, que
    /// seul le module Delivery connaît. Les confondre, c'est la panne qu'on a déjà eue
    /// entre la marketplace et le service de dispatch.
    /// </summary>
    public static async Task<IResult> WithCourierAsync(
        ClaimsPrincipal user,
        IDeliveryModuleApi couriers,
        CancellationToken cancellationToken,
        Func<Guid, Task<IResult>> action)
    {
        if (CurrentUserId(user) is not { } userId)
        {
            return Results.Unauthorized();
        }

        var courier = await couriers.GetCourierByUserAsync(userId, cancellationToken);
        if (courier is null)
        {
            // 404 et non 403 : ce compte n'est pas un coursier, point. Distinguer
            // « pas coursier » de « coursier sans droits » n'apprendrait rien d'utile
            // à l'appelant légitime, et renseignerait les autres.
            return Results.NotFound(new { error = "not_a_courier" });
        }

        return await action(courier.Id);
    }
}
