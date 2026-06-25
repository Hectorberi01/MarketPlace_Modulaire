using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Tax.Application.Taxes;
using Marketplace.Modules.Marketing.Application.Content;
using Marketplace.Modules.Marketing.Application.Campaigns;
using Marketplace.Modules.Recommendations.Application.Recommendations;
using Marketplace.Modules.Fraud.Application.Risk;
using Marketplace.Modules.Analytics.Application.Metrics;
using Marketplace.Modules.Inventory.Application.Locations.Commands;
using Marketplace.Modules.Inventory.Application.Stock.Queries;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>
/// BFF Admin — configuration plateforme : taxes, marketing (contenus &amp;
/// campagnes), recommandations, fraude, analytics et stock (lieux, ruptures).
/// </summary>
public static class AdminPlatformEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlatformEndpoints(this IEndpointRouteBuilder app)
    {
        var tax = app.MapAdminGroup("/admin/tax/rules", "Admin · Taxes");
        tax.MapGet("/", ListTaxRulesAsync);
        tax.MapPost("/", CreateTaxRuleAsync);
        tax.MapPost("/{id:guid}/deactivate", DeactivateTaxRuleAsync);

        var content = app.MapAdminGroup("/admin/content", "Admin · Contenus");
        content.MapGet("/slot/{slot}", ListContentBySlotAsync);
        content.MapPost("/", CreateContentAsync);
        content.MapPost("/{id:guid}/publish", PublishContentAsync);
        content.MapPost("/{id:guid}/archive", ArchiveContentAsync);

        var campaigns = app.MapAdminGroup("/admin/campaigns", "Admin · Campagnes");
        campaigns.MapGet("/", ListCampaignsAsync);
        campaigns.MapGet("/{id:guid}", GetCampaignAsync);
        campaigns.MapPost("/", CreateCampaignAsync);
        campaigns.MapPost("/{id:guid}/promotions", AddPromotionAsync);
        campaigns.MapPost("/{id:guid}/schedule", ScheduleCampaignAsync);
        campaigns.MapPost("/{id:guid}/activate", ActivateCampaignAsync);
        campaigns.MapPost("/{id:guid}/end", EndCampaignAsync);

        var reco = app.MapAdminGroup("/admin/recommendations", "Admin · Recommandations");
        reco.MapPost("/", UpsertRecommendationAsync);

        var fraud = app.MapAdminGroup("/admin/fraud", "Admin · Fraude");
        fraud.MapPost("/assess", AssessRiskAsync);
        fraud.MapGet("/assessments/{subjectType}/{subjectId:guid}", GetRiskAssessmentAsync);

        var analytics = app.MapAdminGroup("/admin/analytics", "Admin · Analytics");
        analytics.MapGet("/report", GetReportAsync);

        var inventory = app.MapAdminGroup("/admin/inventory", "Admin · Stock");
        inventory.MapGet("/low-stock", ListLowStockAsync);
        inventory.MapPost("/locations", CreateLocationAsync);

        return app;
    }

    // --- Taxes ---
    private static async Task<IResult> ListTaxRulesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListTaxRulesQuery(), ct)).Match(r => Results.Ok(r));

    private static async Task<IResult> CreateTaxRuleAsync(TaxRuleRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateTaxRuleCommand(request.Jurisdiction, request.CategoryId, request.Rate, request.Type, request.EffectiveFromUtc), ct)).Match(id => Results.Ok(new { ruleId = id }));

    private static async Task<IResult> DeactivateTaxRuleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeactivateTaxRuleCommand(id), ct)).Match(() => Results.NoContent());

    // --- Contenus ---
    private static async Task<IResult> ListContentBySlotAsync(string slot, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListContentBySlotQuery(slot), ct)).Match(c => Results.Ok(c));

    private static async Task<IResult> CreateContentAsync(ContentRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateContentBlockCommand(request.Type, request.Slot, request.Position, request.Content, request.StartUtc, request.EndUtc), ct)).Match(id => Results.Ok(new { contentBlockId = id }));

    private static async Task<IResult> PublishContentAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new PublishContentBlockCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ArchiveContentAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ArchiveContentBlockCommand(id), ct)).Match(() => Results.NoContent());

    // --- Campagnes ---
    private static async Task<IResult> ListCampaignsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCampaignsQuery(), ct)).Match(c => Results.Ok(c));

    private static async Task<IResult> GetCampaignAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetCampaignQuery(id), ct)).Match(c => Results.Ok(c));

    private static async Task<IResult> CreateCampaignAsync(CampaignRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateCampaignCommand(request.Name, request.Type, request.StartUtc, request.EndUtc, request.TargetSegment), ct)).Match(id => Results.Ok(new { campaignId = id }));

    private static async Task<IResult> AddPromotionAsync(Guid id, PromotionRefRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new AddPromotionToCampaignCommand(id, request.PromotionId), ct)).Match(() => Results.NoContent());

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
                request.Type, request.OwnerId, request.Line, request.City, request.Country, request.Latitude, request.Longitude), ct))
            .Match(id => Results.Ok(new { locationId = id }));

    public sealed record TaxRuleRequest(string Jurisdiction, Guid? CategoryId, decimal Rate, string Type, DateTime? EffectiveFromUtc);
    public sealed record ContentRequest(string Type, string Slot, int Position, string Content, DateTime? StartUtc, DateTime? EndUtc);
    public sealed record CampaignRequest(string Name, string Type, DateTime StartUtc, DateTime EndUtc, string? TargetSegment);
    public sealed record PromotionRefRequest(Guid PromotionId);
    public sealed record RecommendationRequest(string Type, Guid? ContextProductId, Guid? UserId, IReadOnlyList<Guid> RecommendedProductIds, double Score);
    public sealed record AssessRiskRequest(string SubjectType, Guid SubjectId, IReadOnlyDictionary<string, double> Signals);
    public sealed record LocationRequest(string Type, Guid? OwnerId, string Line, string City, string Country, double? Latitude, double? Longitude);
}
