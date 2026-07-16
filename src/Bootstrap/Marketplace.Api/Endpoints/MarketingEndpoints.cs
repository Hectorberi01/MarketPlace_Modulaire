using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Marketing.Application.Campaigns;
using Marketplace.Modules.Marketing.Application.Content;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Marketing/CMS : campagnes promotionnelles et blocs
/// éditoriaux. Gestion réservée aux administrateurs ; le rendu vitrine (contenu
/// publié par slot) est public.
/// </summary>
public static class MarketingEndpoints
{
    /// <summary>Enregistre les routes marketing et de contenu.</summary>
    public static IEndpointRouteBuilder MapMarketingEndpoints(this IEndpointRouteBuilder app)
    {
        var campaigns = app.MapAdminGroup("/api/marketing/campaigns").WithTags("Marketing")
            .RequireAuthorization(p => p.RequireRole("Admin"));

        campaigns.MapPost("/", CreateCampaignAsync).WithName("CreateCampaign");
        campaigns.MapGet("/", ListCampaignsAsync).WithName("ListCampaigns");
        campaigns.MapGet("/{id:guid}", GetCampaignAsync).WithName("GetCampaign");
        campaigns.MapPost("/{id:guid}/promotions", AddPromotionAsync).WithName("AddPromotionToCampaign");
        campaigns.MapPost("/{id:guid}/schedule", ScheduleAsync).WithName("ScheduleCampaign");
        campaigns.MapPost("/{id:guid}/activate", ActivateAsync).WithName("ActivateCampaign");
        campaigns.MapPost("/{id:guid}/end", EndAsync).WithName("EndCampaign");

        var content = app.MapAdminGroup("/api/marketing/content").WithTags("Marketing · CMS");

        content.MapPost("/", CreateContentAsync).WithName("CreateContentBlock").RequireAuthorization(p => p.RequireRole("Admin"));
        content.MapPost("/{id:guid}/publish", PublishContentAsync).WithName("PublishContentBlock").RequireAuthorization(p => p.RequireRole("Admin"));
        content.MapPost("/{id:guid}/archive", ArchiveContentAsync).WithName("ArchiveContentBlock").RequireAuthorization(p => p.RequireRole("Admin"));
        content.MapGet("/slot/{slot}", BySlotAsync).WithName("ListContentBySlot").AllowAnonymous();

        return app;
    }

    /// <summary>Crée une campagne (brouillon).</summary>
    private static async Task<IResult> CreateCampaignAsync(CreateCampaignBody body, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateCampaignCommand(body.Name, body.Type, body.StartUtc, body.EndUtc, body.TargetSegment), ct);
        return result.Match(id => Results.Created($"/api/marketing/campaigns/{id}", new { id }));
    }

    /// <summary>Liste les campagnes.</summary>
    private static async Task<IResult> ListCampaignsAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCampaignsQuery(), ct)).Match(c => Results.Ok(c));

    /// <summary>Récupère une campagne.</summary>
    private static async Task<IResult> GetCampaignAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetCampaignQuery(id), ct)).Match(c => Results.Ok(c));

    /// <summary>Rattache une promotion à la campagne.</summary>
    private static async Task<IResult> AddPromotionAsync(Guid id, PromotionBody body, ISender sender, CancellationToken ct)
        => (await sender.Send(new AddPromotionToCampaignCommand(id, body.PromotionId), ct)).Match(() => Results.NoContent());

    /// <summary>Planifie la campagne.</summary>
    private static async Task<IResult> ScheduleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ScheduleCampaignCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Active la campagne.</summary>
    private static async Task<IResult> ActivateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ActivateCampaignCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Termine la campagne.</summary>
    private static async Task<IResult> EndAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new EndCampaignCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Crée un bloc de contenu (brouillon).</summary>
    private static async Task<IResult> CreateContentAsync(CreateContentBody body, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateContentBlockCommand(body.Type, body.Slot, body.Position, body.Content, body.StartUtc, body.EndUtc), ct);
        return result.Match(id => Results.Created($"/api/marketing/content/{id}", new { id }));
    }

    /// <summary>Publie un bloc de contenu.</summary>
    private static async Task<IResult> PublishContentAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new PublishContentBlockCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Archive un bloc de contenu.</summary>
    private static async Task<IResult> ArchiveContentAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ArchiveContentBlockCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Renvoie les blocs publiés d'un slot (rendu vitrine).</summary>
    private static async Task<IResult> BySlotAsync(string slot, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListContentBySlotQuery(slot), ct)).Match(blocks => Results.Ok(blocks));

    public sealed record CreateCampaignBody(string Name, string Type, DateTime StartUtc, DateTime EndUtc, string? TargetSegment);
    public sealed record PromotionBody(Guid PromotionId);
    public sealed record CreateContentBody(string Type, string Slot, int Position, string Content, DateTime? StartUtc, DateTime? EndUtc);
}
