using MediatR;
using Marketplace.Host.Extensions;
using Marketplace.Modules.Tax.Application.Taxes;
using Marketplace.Modules.Marketing.Application.Content;
using Marketplace.Modules.Marketing.Application.Campaigns;
using Marketplace.Modules.Pricing.Application.Promotions.Queries;
using Marketplace.Modules.Recommendations.Application.Recommendations;
using Marketplace.Modules.Fraud.Application.Risk;
using Marketplace.Modules.Analytics.Application.Metrics;
using Marketplace.Modules.Inventory.Application.Locations.Commands;
using Marketplace.Modules.Inventory.Application.Locations.Queries;
using Marketplace.Modules.Inventory.Application.Stock.Commands;
using Marketplace.Modules.Inventory.Application.Stock.Queries;
using Marketplace.Modules.Notifications.Application.Notifications.Queries;
using Marketplace.Modules.Notifications.Application.Notifications.Commands;

namespace Marketplace.Host.Admin.Endpoints;

/// <summary>
/// BFF Admin — configuration plateforme : taxes, marketing (contenus &amp;
/// campagnes), recommandations, fraude, analytics et stock (lieux, ruptures).
/// </summary>
public static class AdminPlatformEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var tax = app.MapAdminGroup("/api/admin/tax/rules", "Admin · Taxes");
        tax.MapGet("/", ListTaxRulesAsync);
        tax.MapPost("/", CreateTaxRuleAsync);
        tax.MapPut("/{id:guid}", UpdateTaxRuleAsync);
        tax.MapPost("/{id:guid}/deactivate", DeactivateTaxRuleAsync);
        tax.MapPost("/{id:guid}/reactivate", ReactivateTaxRuleAsync);
        tax.MapDelete("/{id:guid}", DeleteTaxRuleAsync);

        var content = app.MapAdminGroup("/api/admin/content", "Admin · Contenus");
        content.MapGet("/", ListAllContentAsync);
        content.MapGet("/slot/{slot}", ListContentBySlotAsync);
        content.MapPost("/", CreateContentAsync);
        content.MapPut("/{id:guid}", UpdateContentAsync);
        content.MapPost("/{id:guid}/publish", PublishContentAsync);
        content.MapPost("/{id:guid}/archive", ArchiveContentAsync);
        content.MapDelete("/{id:guid}", DeleteContentAsync);

        var campaigns = app.MapAdminGroup("/api/admin/campaigns", "Admin · Campagnes");
        campaigns.MapGet("/", ListCampaignsAsync);
        campaigns.MapGet("/promotions", ListPromotionsAsync);
        campaigns.MapGet("/{id:guid}", GetCampaignAsync);
        campaigns.MapPost("/", CreateCampaignAsync);
        campaigns.MapPut("/{id:guid}", UpdateCampaignAsync);
        // Route SÉPARÉE de la mise à jour : voir `SetCampaignPresentationCommand`.
        // Fusionner les deux ferait effacer la présentation par tout appelant qui ne la
        // connaît pas.
        campaigns.MapPut("/{id:guid}/presentation", SetCampaignPresentationAsync);
        campaigns.MapPost("/{id:guid}/promotions", AddPromotionAsync);
        campaigns.MapDelete("/{id:guid}/promotions/{promotionId:guid}", RemovePromotionAsync);
        campaigns.MapPost("/{id:guid}/schedule", ScheduleCampaignAsync);
        campaigns.MapPost("/{id:guid}/activate", ActivateCampaignAsync);
        campaigns.MapPost("/{id:guid}/end", EndCampaignAsync);
        campaigns.MapDelete("/{id:guid}", DeleteCampaignAsync);

        var reco = app.MapAdminGroup("/api/admin/recommendations", "Admin · Recommandations");
        reco.MapPost("/", UpsertRecommendationAsync);

        var fraud = app.MapAdminGroup("/api/admin/fraud", "Admin · Fraude");
        fraud.MapPost("/assess", AssessRiskAsync);
        fraud.MapGet("/assessments/{subjectType}/{subjectId:guid}", GetRiskAssessmentAsync);

        var analytics = app.MapAdminGroup("/api/admin/analytics", "Admin · Analytics");
        analytics.MapGet("/report", GetReportAsync);

        var inventory = app.MapAdminGroup("/api/admin/inventory", "Admin · Stock");
        inventory.MapGet("/low-stock", ListLowStockAsync);
        inventory.MapGet("/by-sku/{sku}", ListInventoryBySkuAsync);
        inventory.MapGet("/locations", ListLocationsAsync);
        inventory.MapPost("/locations", CreateLocationAsync);
        inventory.MapPut("/locations/{id:guid}", UpdateLocationAsync);
        inventory.MapDelete("/locations/{id:guid}", DeleteLocationAsync);
        inventory.MapPost("/items/{id:guid}/receive", ReceiveStockAsync);
        inventory.MapPost("/items/{id:guid}/adjust", AdjustStockAsync);
        inventory.MapPost("/items/{id:guid}/threshold", SetThresholdAsync);

        var notifications = app.MapAdminGroup("/api/admin/notifications", "Admin · Notifications");
        notifications.MapGet("/", ListNotificationsAsync);
        notifications.MapPost("/", SendNotificationAsync);
        // La DIFFUSION est une route à part de l'envoi nominatif : ce ne sont pas les
        // mêmes conséquences, et une faute de frappe sur un identifiant ne doit jamais
        // pouvoir se transformer en envoi à tout le monde.
        notifications.MapPost("/broadcast", BroadcastAsync);
        notifications.MapDelete("/{id:guid}", DeleteNotificationAsync);

        return app;
    }

    private static async Task<IResult> ListNotificationsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListAllNotificationsQuery(), ct)).Match(n => Results.Ok(n));

    private static async Task<IResult> SendNotificationAsync(NotificationRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SendNotificationCommand(request.RecipientUserId, request.Channel, request.Subject, request.Body), ct)).Match(id => Results.Ok(new { notificationId = id }));

    private static async Task<IResult> BroadcastAsync(BroadcastRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new BroadcastAnnouncementCommand(request.Subject, request.Body, request.Max, request.Platform), ct))
            .Match(n => Results.Ok(new { recipients = n }));

    private static async Task<IResult> DeleteNotificationAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteNotificationCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ListAllContentAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListAllContentQuery(), ct)).Match(c => Results.Ok(c));

    // --- Taxes ---
    private static async Task<IResult> ListTaxRulesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListTaxRulesQuery(), ct)).Match(r => Results.Ok(r));

    private static async Task<IResult> CreateTaxRuleAsync(TaxRuleRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateTaxRuleCommand(request.Jurisdiction, request.CategoryId, request.Rate, request.Type, request.EffectiveFromUtc), ct)).Match(id => Results.Ok(new { ruleId = id }));

    private static async Task<IResult> UpdateTaxRuleAsync(Guid id, TaxRuleRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateTaxRuleCommand(id, request.Jurisdiction, request.CategoryId, request.Rate, request.Type, request.EffectiveFromUtc), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeactivateTaxRuleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeactivateTaxRuleCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ReactivateTaxRuleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ReactivateTaxRuleCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeleteTaxRuleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteTaxRuleCommand(id), ct)).Match(() => Results.NoContent());

    // --- Contenus ---

    /// <summary>
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// L'ADMINISTRATEUR VOIT TOUT LE SLOT, PAS SEULEMENT CE QUI EST VISIBLE AUJOURD'HUI.
    ///
    /// Cette route passait par `ListContentBySlotQuery`, la MÊME requête que la vitrine.
    /// Depuis que cette requête applique la fenêtre d'affichage, elle ne rend plus que
    /// les blocs visibles à l'instant présent : une bannière préparée pour le mois
    /// prochain, ou expirée hier, aurait disparu de la console — c'est-à-dire de l'écran
    /// où l'on va justement corriger sa date.
    ///
    /// On filtre donc la liste complète, qui ignore le statut comme la fenêtre. Le coût
    /// est celui d'un parcours de la table des blocs, qui se compte en dizaines de
    /// lignes ; la console, elle, appelle déjà `/admin/content` sans filtre.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    private static async Task<IResult> ListContentBySlotAsync(string slot, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListAllContentQuery(), ct))
            .Match(c => Results.Ok(c
                .Where(b => string.Equals(b.Slot, slot, StringComparison.OrdinalIgnoreCase))
                .OrderBy(b => b.Position)
                .ToList()));

    private static async Task<IResult> CreateContentAsync(ContentRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateContentBlockCommand(request.Type, request.Slot, request.Position, request.Content, request.StartUtc, request.EndUtc), ct)).Match(id => Results.Ok(new { contentBlockId = id }));

    private static async Task<IResult> PublishContentAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new PublishContentBlockCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ArchiveContentAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ArchiveContentBlockCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> UpdateContentAsync(Guid id, ContentRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateContentBlockCommand(id, request.Type, request.Slot, request.Position, request.Content, request.StartUtc, request.EndUtc), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeleteContentAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteContentBlockCommand(id), ct)).Match(() => Results.NoContent());

    // --- Campagnes ---
    private static async Task<IResult> ListCampaignsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCampaignsQuery(), ct)).Match(c => Results.Ok(c));

    /// <summary>Liste des promotions disponibles (pour rattachement à une campagne).</summary>
    private static async Task<IResult> ListPromotionsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListPromotionsQuery(), ct)).Match(p => Results.Ok(p));

    private static async Task<IResult> GetCampaignAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetCampaignQuery(id), ct)).Match(c => Results.Ok(c));

    private static async Task<IResult> CreateCampaignAsync(CampaignRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateCampaignCommand(request.Name, request.Type, request.StartUtc, request.EndUtc, request.TargetSegment), ct)).Match(id => Results.Ok(new { campaignId = id }));

    private static async Task<IResult> SetCampaignPresentationAsync(Guid id, CampaignPresentationRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetCampaignPresentationCommand(id, request.Slug, request.Description, request.HeroImageUrl, request.IsPublic), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> UpdateCampaignAsync(Guid id, CampaignRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateCampaignCommand(id, request.Name, request.Type, request.StartUtc, request.EndUtc, request.TargetSegment), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> AddPromotionAsync(Guid id, PromotionRefRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new AddPromotionToCampaignCommand(id, request.PromotionId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> RemovePromotionAsync(Guid id, Guid promotionId, ISender sender, CancellationToken ct)
        => (await sender.Send(new RemovePromotionFromCampaignCommand(id, promotionId), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeleteCampaignAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteCampaignCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ScheduleCampaignAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ScheduleCampaignCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ActivateCampaignAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ActivateCampaignCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> EndCampaignAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new EndCampaignCommand(id), ct)).Match(() => Results.NoContent());

    // --- Recommandations ---
    private static async Task<IResult> UpsertRecommendationAsync(RecommendationRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpsertRecommendationCommand(request.Type, request.ContextProductId, request.UserId, request.RecommendedProductIds, request.Score), ct)).Match(id => Results.Ok(new { recommendationId = id }));

    // --- Fraude ---
    private static async Task<IResult> AssessRiskAsync(AssessRiskRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new AssessRiskCommand(request.SubjectType, request.SubjectId, request.Signals), ct)).Match(id => Results.Ok(new { assessmentId = id }));

    private static async Task<IResult> GetRiskAssessmentAsync(string subjectType, Guid subjectId, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetRiskAssessmentQuery(subjectType, subjectId), ct)).Match(a => Results.Ok(a));

    // --- Analytics ---
    /// <summary>Rapport agrégé (?scope=&amp;ownerId=&amp;granularity=&amp;from=&amp;to=, clés selon la granularité).</summary>
    private static async Task<IResult> GetReportAsync(
        string scope, Guid? ownerId, string granularity, string from, string to, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetReportQuery(scope, ownerId, granularity, from, to), ct)).Match(r => Results.Ok(r));

    // --- Stock ---
    private static async Task<IResult> ListLowStockAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListLowStockQuery(), ct)).Match(i => Results.Ok(i));

    private static async Task<IResult> CreateLocationAsync(LocationRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateFulfillmentLocationCommand(
                request.Type, request.OwnerId, request.Commune, request.Quartier, request.Landmark,
                request.Line, request.Latitude, request.Longitude), ct))
            .Match(id => Results.Ok(new { locationId = id }));

    private static async Task<IResult> ListLocationsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListAllFulfillmentLocationsQuery(), ct)).Match(l => Results.Ok(l));

    private static async Task<IResult> UpdateLocationAsync(Guid id, LocationRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateLocationAddressCommand(
                id, request.Commune, request.Quartier, request.Landmark, request.Line,
                request.Latitude, request.Longitude), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> DeleteLocationAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteFulfillmentLocationCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ListInventoryBySkuAsync(string sku, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListInventoryBySkuQuery(sku), ct)).Match(i => Results.Ok(i));

    private static async Task<IResult> ReceiveStockAsync(Guid id, QuantityRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new ReceiveStockCommand(id, request.Quantity), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> AdjustStockAsync(Guid id, DeltaRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new AdjustStockCommand(id, request.Delta), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> SetThresholdAsync(Guid id, ThresholdRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetReorderThresholdCommand(id, request.Threshold), ct)).Match(() => Results.NoContent());

    public sealed record TaxRuleRequest(string Jurisdiction, Guid? CategoryId, decimal Rate, string Type, DateTime? EffectiveFromUtc);
    public sealed record ContentRequest(string Type, string Slot, int Position, string Content, DateTime? StartUtc, DateTime? EndUtc);
    public sealed record CampaignRequest(string Name, string Type, DateTime StartUtc, DateTime EndUtc, string? TargetSegment);
    public sealed record CampaignPresentationRequest(string? Slug, string? Description, string? HeroImageUrl, bool IsPublic);
    // `Max` est OBLIGATOIRE et sans valeur par défaut : l'appelant doit dire combien de
    // personnes il accepte de toucher. Un défaut, même prudent, ferait que la question
    // ne se pose plus.
    public sealed record BroadcastRequest(string Subject, string Body, int Max, string? Platform);
    public sealed record PromotionRefRequest(Guid PromotionId);
    public sealed record RecommendationRequest(string Type, Guid? ContextProductId, Guid? UserId, IReadOnlyList<Guid> RecommendedProductIds, double Score);
    public sealed record AssessRiskRequest(string SubjectType, Guid SubjectId, IReadOnlyDictionary<string, double> Signals);
    // « Commune » accepte indifféremment le code (« abomey-calavi ») ou le libellé
    // (« Abomey-Calavi ») : les consoles envoient le code, les imports des libellés.
    public sealed record LocationRequest(
        string Type, Guid? OwnerId, string? Commune, string? Quartier, string? Landmark,
        string? Line, double? Latitude, double? Longitude);
    public sealed record NotificationRequest(Guid RecipientUserId, string? Channel, string Subject, string Body);
    public sealed record QuantityRequest(int Quantity);
    public sealed record DeltaRequest(int Delta);
    public sealed record ThresholdRequest(int Threshold);
}
