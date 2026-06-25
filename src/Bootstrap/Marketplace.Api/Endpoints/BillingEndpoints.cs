using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Billing.Application.Commissions;
using Marketplace.Modules.Billing.Application.Invoices;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Billing : règles de commission (combien la plateforme
/// prélève) et factures de frais vendeur. Back-office, réservé aux administrateurs.
/// </summary>
public static class BillingEndpoints
{
    /// <summary>Enregistre les routes de commission et de facturation.</summary>
    public static IEndpointRouteBuilder MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var billing = app.MapGroup("/api/billing").WithTags("Billing")
            .RequireAuthorization(p => p.RequireRole("Admin"));

        billing.MapPost("/commission-rules", CreateRuleAsync).WithName("CreateCommissionRule");
        billing.MapGet("/commission-rules", ListRulesAsync).WithName("ListCommissionRules");
        billing.MapPost("/commission-rules/{id:guid}/deactivate", DeactivateRuleAsync).WithName("DeactivateCommissionRule");
        billing.MapGet("/commission/preview", PreviewAsync).WithName("PreviewCommission");

        billing.MapPost("/invoices", CreateInvoiceAsync).WithName("CreateInvoice");
        billing.MapPost("/invoices/{id:guid}/lines", AddLineAsync).WithName("AddInvoiceLine");
        billing.MapPost("/invoices/{id:guid}/issue", IssueAsync).WithName("IssueInvoice");
        billing.MapPost("/invoices/{id:guid}/pay", PayAsync).WithName("MarkInvoicePaid");
        billing.MapGet("/invoices/{id:guid}", GetInvoiceAsync).WithName("GetInvoice");
        billing.MapGet("/sellers/{sellerId:guid}/invoices", ListInvoicesAsync).WithName("ListInvoicesBySeller");

        return app;
    }

    /// <summary>Crée une règle de commission.</summary>
    private static async Task<IResult> CreateRuleAsync(CreateRuleRequest request, ISender sender, CancellationToken ct)
    {
        var command = new CreateCommissionRuleCommand(
            request.Scope, request.TargetId, request.Rate, request.FixedFee, request.Currency,
            request.MinFee, request.MaxFee, request.EffectiveFromUtc ?? DateTime.UtcNow);
        var result = await sender.Send(command, ct);
        return result.Match(id => Results.Created($"/api/billing/commission-rules/{id}", new { id }));
    }

    /// <summary>Liste les règles de commission.</summary>
    private static async Task<IResult> ListRulesAsync(ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListCommissionRulesQuery(), ct);
        return result.Match(rules => Results.Ok(rules));
    }

    /// <summary>Désactive une règle de commission.</summary>
    private static async Task<IResult> DeactivateRuleAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new DeactivateCommissionRuleCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Calcule la commission applicable à un montant brut (aperçu).</summary>
    private static async Task<IResult> PreviewAsync(Guid sellerId, Guid categoryId, decimal gross, string? currency, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ComputeCommissionQuery(sellerId, categoryId, gross, currency ?? "XOF"), ct);
        return result.Match(r => Results.Ok(r));
    }

    /// <summary>Crée une facture brouillon.</summary>
    private static async Task<IResult> CreateInvoiceAsync(CreateInvoiceRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateInvoiceCommand(request.SellerId, request.PeriodStartUtc, request.PeriodEndUtc, request.Currency), ct);
        return result.Match(id => Results.Created($"/api/billing/invoices/{id}", new { id }));
    }

    /// <summary>Ajoute une ligne à une facture brouillon.</summary>
    private static async Task<IResult> AddLineAsync(Guid id, AddLineRequest request, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new AddInvoiceLineCommand(id, request.Description, request.Amount), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Émet la facture.</summary>
    private static async Task<IResult> IssueAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new IssueInvoiceCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Marque la facture comme payée.</summary>
    private static async Task<IResult> PayAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new MarkInvoicePaidCommand(id), ct);
        return result.Match(() => Results.NoContent());
    }

    /// <summary>Récupère une facture.</summary>
    private static async Task<IResult> GetInvoiceAsync(Guid id, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetInvoiceQuery(id), ct);
        return result.Match(invoice => Results.Ok(invoice));
    }

    /// <summary>Liste les factures d'un vendeur.</summary>
    private static async Task<IResult> ListInvoicesAsync(Guid sellerId, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new ListInvoicesBySellerQuery(sellerId), ct);
        return result.Match(invoices => Results.Ok(invoices));
    }

    public sealed record CreateRuleRequest(
        string Scope, Guid? TargetId, decimal Rate, decimal FixedFee, string Currency,
        decimal? MinFee, decimal? MaxFee, DateTime? EffectiveFromUtc);

    public sealed record CreateInvoiceRequest(Guid SellerId, DateTime PeriodStartUtc, DateTime PeriodEndUtc, string Currency);
    public sealed record AddLineRequest(string Description, decimal Amount);
}
