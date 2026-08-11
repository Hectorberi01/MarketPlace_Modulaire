using HBA.Deliveries.Application.Deliveries.Commands;
using HBA.Deliveries.Application.Deliveries.Queries;
using HBA.Deliveries.Application.Pricing.Queries;
using HBA.Deliveries.Domain.Deliveries;
using HBA.Deliveries.Domain.Drivers;
using HBA.Deliveries.Domain.Pricing;
using Marketplace.Api.Extensions;
using MediatR;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// API PUBLIQUE HBA DELIVERY — VERSION 1.
///
/// C'est la surface vendue à des marchands tiers, et elle obéit à des règles que
/// l'API interne n'a pas à suivre.
///
/// ELLE EST VERSIONNÉE, ET LA VERSION EST DANS LE CHEMIN
///
/// Un partenaire déploie son intégration une fois et n'y revient pas pendant des
/// années. « /api/v1/ » nous laisse publier une v2 sans casser personne. Une
/// version portée par un en-tête serait oubliée par la moitié des intégrateurs,
/// et le défaut retomberait sur la version la plus récente — c'est-à-dire sur
/// celle qui change.
///
/// LE PARTENAIRE N'EST JAMAIS DANS LE CORPS DE REQUÊTE
///
/// Il vient de la clé d'API, et de nulle part ailleurs. Chaque commande émise ici
/// force le <c>PartnerId</c> authentifié, et chaque lecture exige que la course
/// lui appartienne. Un champ « partnerId » dans le JSON transformerait toute
/// l'API en portail d'accès aux données des concurrents.
///
/// LES ÉTAPES D'EXÉCUTION NE SONT PAS EXPOSÉES
///
/// Ni collecte, ni transit, ni remise : ce sont les gestes du livreur, constatés
/// par l'application livreur. Un partenaire qui pourrait déclarer une course
/// livrée pourrait clore une livraison qui n'a pas eu lieu. Il crée, il consulte,
/// il annule tant que le colis n'est pas pris en charge. C'est tout.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public static class PartnerDeliveryEndpoints
{
    public static IEndpointRouteBuilder MapPartnerDeliveryEndpoints(this IEndpointRouteBuilder app)
    {
        // ⚠️ PAS DE RequireAuthorization() ICI.
        //
        // Ce groupe n'est PAS authentifié par jeton utilisateur : il l'est par clé
        // d'API, via le filtre. Ajouter RequireAuthorization() exigerait en plus un
        // JWT, qu'aucun partenaire ne possède — l'API serait inaccessible.
        //
        // ⚠️ LIMITATION DE DÉBIT : CES ROUTES SONT PARTITIONNÉES PAR IP, PAS PAR
        // PARTENAIRE. C'est une faiblesse connue, pas un oubli.
        //
        // Le limiteur global partitionne par identifiant de compte quand il y en a
        // un, et retombe sur l'IP sinon. Un partenaire n'ayant pas de compte
        // utilisateur, il tombe dans le seau de son adresse — 300 requêtes par
        // minute, PARTAGÉES avec tout ce qui sort de la même adresse. Deux
        // partenaires hébergés chez le même fournisseur se volent donc leur quota,
        // exactement comme le CGNAT le fait pour les utilisateurs mobiles — le
        // raisonnement est déjà écrit dans AuthRateLimiter, il n'a simplement pas
        // été étendu aux clés d'API.
        //
        // Le correctif est de partitionner par PRÉFIXE DE CLÉ (lisible sans base,
        // avant tout accès), CHAÎNÉ à un plafond par IP — sans le chaînage, un
        // préfixe inventé suffirait à s'offrir un seau neuf. À faire avant
        // d'ouvrir l'API à plus d'un partenaire.
        var v1 = app.MapGroup("/api/v1/deliveries")
            .WithTags("HBA Delivery · API partenaires")

            // ⚠️ AllowAnonymous() EST INDISPENSABLE, ET CE N'EST PAS UN RELÂCHEMENT.
            //
            // L'API pose une FallbackPolicy qui exige un utilisateur AUTHENTIFIÉ sur
            // toute route ne déclarant rien — un excellent réglage, qui transforme
            // l'oubli en 401 plutôt qu'en faille. Mais « authentifié » y signifie
            // « porteur d'un JWT », et un partenaire n'en a pas.
            //
            // Sans cette ligne, l'API partenaire répondrait 401 à TOUTE requête, clé
            // parfaitement valide comprise, et le message d'erreur ne parlerait même
            // pas de clé. On aurait cherché du côté du filtre pendant des heures.
            //
            // « Anonyme » ne vaut donc que pour la mécanique ASP.NET : la vraie
            // authentification est faite par le filtre ci-dessous, qui s'exécute
            // avant tout handler et refuse tout ce qui ne présente pas une clé
            // valide.
            .AllowAnonymous()

            .AddEndpointFilter<PartnerApiKeyFilter>();

        v1.MapPost("/quote", QuoteAsync).WithName("PartnerQuote");
        v1.MapPost("/", CreateAsync).WithName("PartnerCreateDelivery");
        v1.MapGet("/{id:guid}", GetAsync).WithName("PartnerGetDelivery");
        v1.MapGet("/{id:guid}/tracking", TrackAsync).WithName("PartnerTrackDelivery");
        v1.MapPost("/{id:guid}/cancel", CancelAsync).WithName("PartnerCancelDelivery");

        return app;
    }

    private static async Task<IResult> QuoteAsync(
        PartnerQuoteRequest request, HttpContext http, ISender sender, CancellationToken ct)
    {
        var partner = PartnerApiKeyFilter.Require(http);

        var query = new GetQuoteQuery(
            request.Pickup.Latitude,
            request.Pickup.Longitude,
            request.Dropoff.Latitude,
            request.Dropoff.Longitude,
            request.WeightKg,
            request.Size,
            request.Type,
            request.Vehicle,
            // Le devis est ÉMIS AU NOM du partenaire, et la création vérifiera
            // qu'il lui appartient. Sans cela, un partenaire pourrait consommer
            // le devis d'un autre — donc son prix.
            PartnerId: partner.Id);

        return (await sender.Send(query, ct)).Match(quote => Results.Ok(quote));
    }

    private static async Task<IResult> CreateAsync(
        PartnerCreateDeliveryRequest request, HttpContext http, ISender sender, CancellationToken ct)
    {
        var partner = PartnerApiKeyFilter.Require(http);

        var command = new CreateDeliveryCommand(
            request.Reference,
            // Forcé, jamais lu depuis la requête : c'est la seule source de
            // vérité sur l'origine d'une course externe.
            DeliverySource.ExternalPartner,
            request.Type,
            ToStop(request.Pickup),
            ToStop(request.Dropoff),
            new DeliveryPackageInput(
                request.Package.Description, request.Package.WeightKg,
                request.Package.IsFragile, request.Package.IsPerishable),
            request.RequiredProof,
            PartnerId: partner.Id,
            QuoteId: request.QuoteId,
            ScheduledForUtc: request.ScheduledForUtc);

        // 200 et non 201 : la création est IDEMPOTENTE par référence. Un
        // partenaire qui rejoue sa requête après un délai dépassé retrouve SA
        // course — annoncer « créé » serait faux, et l'inciterait à croire qu'il
        // en a deux.
        return (await sender.Send(command, ct)).Match(id => Results.Ok(new { deliveryId = id }));
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new GetDeliveryQuery(id, RequiredPartnerId: PartnerApiKeyFilter.Require(http).Id), ct))
            .Match(delivery => Results.Ok(delivery));

    private static async Task<IResult> TrackAsync(Guid id, HttpContext http, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new GetDeliveryTrackingQuery(id, RequiredPartnerId: PartnerApiKeyFilter.Require(http).Id), ct))
            .Match(tracking => Results.Ok(tracking));

    private static async Task<IResult> CancelAsync(
        Guid id, PartnerCancelRequest? request, HttpContext http, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new CancelDeliveryCommand(id, request?.Reason, PartnerApiKeyFilter.Require(http).Id), ct))
            .Match(() => Results.NoContent());

    private static DeliveryStopInput ToStop(PartnerStopRequest stop)
        => new(stop.ContactName, stop.Phone, stop.Commune, stop.Quartier,
               stop.Landmark, stop.Instructions, stop.Latitude, stop.Longitude);

    // ── Corps de requête ────────────────────────────────────────────────────
    //
    // Volontairement DISTINCTS de ceux de l'API interne, alors qu'ils s'en
    // approchent. Les partager ferait d'un ajout de champ interne une
    // modification du contrat public — et le jour où l'interne évolue, tous les
    // partenaires en héritent sans l'avoir demandé. Une API publique est un
    // engagement ; sa forme doit pouvoir rester figée pendant que le reste bouge.

    /// <summary>Un point de la course. Repère, téléphone et position sont obligatoires.</summary>
    public sealed record PartnerStopRequest(
        string? ContactName,
        string? Phone,
        string? Commune,
        string? Landmark,
        double? Latitude,
        double? Longitude,
        string? Quartier = null,
        string? Instructions = null);

    /// <summary>Un point, réduit à sa position — pour le devis, où le reste est inutile.</summary>
    public sealed record PartnerPoint(double Latitude, double Longitude);

    public sealed record PartnerPackageRequest(
        string? Description,
        decimal? WeightKg = null,
        bool IsFragile = false,
        bool IsPerishable = false);

    /// <summary>
    /// Demande de devis.
    ///
    /// <c>Vehicle</c> est facultatif et vaut mieux être omis : nous retenons alors
    /// le plus petit véhicule capable de porter le colis, qui est aussi le moins
    /// cher. Un partenaire n'a pas à connaître la charge utile d'un tricycle.
    /// </summary>
    public sealed record PartnerQuoteRequest(
        PartnerPoint Pickup,
        PartnerPoint Dropoff,
        decimal WeightKg,
        PackageSize Size = PackageSize.Small,
        DeliveryType Type = DeliveryType.Standard,
        VehicleType? Vehicle = null);

    /// <summary>
    /// Création d'une course.
    ///
    /// <c>Reference</c> est VOTRE référence de commande : elle est renvoyée telle
    /// quelle dans les webhooks, et elle sert de clé d'idempotence. Rejouer la
    /// même référence renvoie la même course plutôt que d'en créer une seconde.
    /// Elle doit donc être unique dans VOTRE système.
    /// </summary>
    public sealed record PartnerCreateDeliveryRequest(
        string Reference,
        PartnerStopRequest Pickup,
        PartnerStopRequest Dropoff,
        PartnerPackageRequest Package,
        string? QuoteId = null,
        DeliveryType Type = DeliveryType.Standard,
        ProofOfDeliveryKind RequiredProof = ProofOfDeliveryKind.None,

        // Heure de livraison souhaitée, en UTC. Obligatoire si Type vaut
        // « Scheduled », refusée sinon.
        DateTime? ScheduledForUtc = null);

    public sealed record PartnerCancelRequest(string? Reason);
}
