using HBA.Deliveries.Application.Partners.Commands;
using HBA.Deliveries.Application.Partners.Queries;
using Marketplace.Api.Extensions;
using MediatR;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// ADMINISTRATION DES PARTENAIRES HBA DELIVERY.
///
/// C'est ici qu'on ouvre et qu'on ferme les portes de l'API publique. Ces routes
/// n'existaient pas, et sans elles l'API v1 était inaccessible : le domaine savait
/// émettre des clés, personne ne pouvait le lui demander.
///
/// GROUPE ADMINISTRATEUR, PAS SIMPLEMENT AUTHENTIFIÉ
///
/// Les cinq hôtes du monolithe partagent la même clé de signature JWT : un jeton
/// obtenu par n'importe quel acheteur sur l'application mobile est valide ici. Un
/// groupe seulement « authentifié » laisserait donc le premier client venu émettre
/// une clé d'API à son nom — et créer des livraisons facturées à HBA.
///
/// D'où <see cref="ApiAuthorization.MapAdminGroup"/>, conformément à la règle
/// posée dans ce fichier : jamais de MapGroup nu sur cette API.
///
/// DEUX RÉPONSES DE CE FICHIER CONTIENNENT UN SECRET EN CLAIR.
///
/// L'émission de clé et la configuration de webhook renvoient une valeur qui
/// n'existera plus jamais ensuite. Toute journalisation de corps de réponse
/// activée sur cette API la capturerait — et les journaux se conservent bien plus
/// longtemps que les clés.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class PartnerAdminEndpoints
{
    public static IEndpointRouteBuilder MapPartnerAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var partners = app.MapAdminGroup("/api/delivery/partners")
            .WithTags("HBA Delivery · Administration partenaires");

        partners.MapGet("/", ListAsync).WithName("ListDeliveryPartners");
        partners.MapGet("/{id:guid}", GetAsync).WithName("GetDeliveryPartner");
        partners.MapPost("/", RegisterAsync).WithName("RegisterDeliveryPartner");

        partners.MapPost("/{id:guid}/activate", ActivateAsync).WithName("ActivateDeliveryPartner");
        partners.MapPost("/{id:guid}/suspend", SuspendAsync).WithName("SuspendDeliveryPartner");

        partners.MapPost("/{id:guid}/api-keys", IssueKeyAsync).WithName("IssueDeliveryPartnerApiKey");
        partners.MapDelete("/{id:guid}/api-keys/{keyId:guid}", RevokeKeyAsync)
            .WithName("RevokeDeliveryPartnerApiKey");

        partners.MapPut("/{id:guid}/webhook", ConfigureWebhookAsync)
            .WithName("ConfigureDeliveryPartnerWebhook");

        return app;
    }

    private static async Task<IResult> ListAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPartnersQuery(), ct)).Match(partners => Results.Ok(partners));

    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetPartnerQuery(id), ct)).Match(partner => Results.Ok(partner));

    private static async Task<IResult> RegisterAsync(
        RegisterPartnerRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new RegisterPartnerCommand(request.Name, request.ContactEmail, request.DailyQuota), ct))
            .Match(id => Results.Created(
                $"/api/delivery/partners/{id}",
                new
                {
                    partnerId = id,
                    // Dit explicitement ce que la création NE fait PAS. Un
                    // administrateur qui croirait le partenaire opérationnel
                    // chercherait la panne du mauvais côté.
                    status = "Pending",
                    next = "Activez le partenaire, puis émettez une clé d'API."
                }));

    private static async Task<IResult> ActivateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ActivatePartnerCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> SuspendAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new SuspendPartnerCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// Émet une clé. La valeur renvoyée est la SEULE occurrence de cette clé en
    /// clair dans tout le système — elle n'est stockée nulle part et ne pourra
    /// jamais être relue, y compris par nous.
    /// </summary>
    private static async Task<IResult> IssueKeyAsync(
        Guid id, IssueApiKeyRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new IssuePartnerApiKeyCommand(id, request.Environment, request.Label), ct))
            .Match(issued => Results.Ok(new
            {
                apiKey = issued.ApiKey,
                prefix = issued.Prefix,
                warning = "Cette clé ne sera plus jamais affichée. Transmettez-la maintenant au partenaire "
                          + "par un canal sûr, et ne la conservez pas ailleurs."
            }));

    private static async Task<IResult> RevokeKeyAsync(
        Guid id, Guid keyId, ISender sender, CancellationToken ct)
        => (await sender.Send(new RevokePartnerApiKeyCommand(id, keyId), ct))
            .Match(() => Results.NoContent());

    /// <summary>
    /// Configure ou efface le rappel. Le secret de signature est ENGENDRÉ ici,
    /// jamais fourni : un secret choisi par un humain est faible, et un secret qui
    /// traverse un formulaire finit dans un journal.
    ///
    /// Reconfigurer produit un NOUVEAU secret et invalide l'ancien — c'est ainsi
    /// qu'on fait tourner un secret compromis, et le partenaire devra redéployer
    /// sa vérification de signature.
    /// </summary>
    private static async Task<IResult> ConfigureWebhookAsync(
        Guid id, ConfigureWebhookRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ConfigurePartnerWebhookCommand(id, request.Url), ct))
            .Match(configured => configured.Enabled
                ? Results.Ok(new
                {
                    url = configured.Url,
                    secret = configured.Secret,
                    warning = "Ce secret ne sera plus jamais affiché. Le partenaire s'en sert pour vérifier "
                              + "la signature de nos appels : sans lui, n'importe qui peut lui annoncer une "
                              + "livraison terminée."
                })
                : Results.Ok(new { webhook = "désactivé" }));

    // ── Corps de requête ────────────────────────────────────────────────────

    public sealed record RegisterPartnerRequest(string? Name, string? ContactEmail, int DailyQuota = 0);

    /// <summary><c>Environment</c> vaut « live » ou « test ». C'est ce que l'intégrateur lit dans sa clé.</summary>
    public sealed record IssueApiKeyRequest(string? Environment, string? Label = null);

    /// <summary>URL absolue en HTTPS. Vide ou nulle pour désactiver le rappel.</summary>
    public sealed record ConfigureWebhookRequest(string? Url);
}
