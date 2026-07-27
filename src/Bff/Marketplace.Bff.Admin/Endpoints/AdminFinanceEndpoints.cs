using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Settlement.Application.Batches;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Billing.Application.Commissions;
using Marketplace.Modules.Billing.Application.Invoices;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>
/// BFF Admin — finances plateforme : lots de reversement (génération, versement),
/// règles de commission et facturation vendeur.
/// </summary>
public static class AdminFinanceEndpoints
{
    public static IEndpointRouteBuilder MapAdminFinanceEndpoints(this IEndpointRouteBuilder app)
    {
        var settlement = app.MapAdminGroup("/admin/settlement", "Admin · Reversements");
        settlement.MapGet("/batches", ListBatchesAsync);
        settlement.MapGet("/batches/{id:guid}", GetBatchAsync);
        settlement.MapPost("/run", RunAsync);
        settlement.MapPost("/batches/{batchId:guid}/payouts/{payoutId:guid}/paid", MarkPaidAsync);
        settlement.MapPost("/customer-refund", CustomerRefundAsync);

        var commissions = app.MapAdminGroup("/admin/commission-rules", "Admin · Commissions");
        commissions.MapGet("/", ListCommissionRulesAsync);
        commissions.MapPost("/", CreateCommissionRuleAsync);
        commissions.MapPut("/{id:guid}", UpdateCommissionRuleAsync);
        commissions.MapPost("/{id:guid}/deactivate", DeactivateCommissionRuleAsync);
        commissions.MapPost("/{id:guid}/reactivate", ReactivateCommissionRuleAsync);
        commissions.MapDelete("/{id:guid}", DeleteCommissionRuleAsync);

        var invoices = app.MapAdminGroup("/admin/invoices", "Admin · Facturation");
        invoices.MapGet("/{id:guid}", GetInvoiceAsync);
        invoices.MapGet("/by-seller/{sellerId:guid}", ListInvoicesBySellerAsync);
        invoices.MapPost("/", CreateInvoiceAsync);
        invoices.MapPost("/{id:guid}/lines", AddInvoiceLineAsync);
        invoices.MapPost("/{id:guid}/issue", IssueInvoiceAsync);
        invoices.MapPost("/{id:guid}/paid", MarkInvoicePaidAsync);

        return app;
    }

    private static async Task<IResult> ListBatchesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListSettlementBatchesQuery(), ct)).Match(b => Results.Ok(b));

    private static async Task<IResult> GetBatchAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetSettlementBatchQuery(id), ct)).Match(b => Results.Ok(b));

    /// <summary>Génère un lot de reversements sur une période (un payout net par vendeur).</summary>
    private static async Task<IResult> RunAsync(RunSettlementRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RunSettlementCommand(request.PeriodStartUtc, request.PeriodEndUtc, request.SellerId), ct))
            .Match(id => Results.Ok(new { batchId = id }));

    /// <summary>Marque un reversement comme versé (réf. transfert MoMo/banque).</summary>
    private static async Task<IResult> MarkPaidAsync(Guid batchId, Guid payoutId, MarkPaidRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkPayoutPaidCommand(batchId, payoutId, request.ProviderRef), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// Rembourse un CLIENT sur une commande (hors flux retour) : payout FedaPay MoMo vers
    /// son numéro, coût débité du portefeuille plateforme. Montant plafonné au total payé.
    /// </summary>
    private static async Task<IResult> CustomerRefundAsync(CustomerRefundRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(
                new InitiateCustomerRefundCommand(request.OrderId, request.Amount, request.Msisdn, request.Provider, request.Reason), ct))
            .Match(r => Results.Ok(r));

    private static async Task<IResult> ListCommissionRulesAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCommissionRulesQuery(), ct)).Match(r => Results.Ok(r));

    /// <summary>Crée une règle de commission (Global / Category / Seller).</summary>
    private static async Task<IResult> CreateCommissionRuleAsync(CommissionRuleRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateCommissionRuleCommand(
                request.Scope, request.TargetId, request.Rate, request.FixedFee, request.Currency,
                request.MinFee, request.MaxFee, request.EffectiveFromUtc ?? DateTime.UtcNow), ct))
            .Match(id => Results.Ok(new { ruleId = id }));

    private static async Task<IResult> UpdateCommissionRuleAsync(Guid id, CommissionRuleRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateCommissionRuleCommand(
                id, request.Rate, request.FixedFee, request.Currency,
                request.MinFee, request.MaxFee, request.EffectiveFromUtc ?? DateTime.UtcNow), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> DeactivateCommissionRuleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeactivateCommissionRuleCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> ReactivateCommissionRuleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ReactivateCommissionRuleCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeleteCommissionRuleAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeleteCommissionRuleCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> GetInvoiceAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetInvoiceQuery(id), ct)).Match(i => Results.Ok(i));

    private static async Task<IResult> ListInvoicesBySellerAsync(Guid sellerId, ISender sender, CancellationToken ct)
        => (await sender.Send(new ListInvoicesBySellerQuery(sellerId), ct)).Match(i => Results.Ok(i));

    /// <summary>Crée une facture vendeur pour une période.</summary>
    private static async Task<IResult> CreateInvoiceAsync(CreateInvoiceRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateInvoiceCommand(request.SellerId, request.PeriodStartUtc, request.PeriodEndUtc, request.Currency), ct)).Match(id => Results.Ok(new { invoiceId = id }));

    private static async Task<IResult> AddInvoiceLineAsync(Guid id, InvoiceLineRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new AddInvoiceLineCommand(id, request.Description, request.Amount), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> IssueInvoiceAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new IssueInvoiceCommand(id), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> MarkInvoicePaidAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkInvoicePaidCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary><c>SellerId</c> null = tous les vendeurs de la période.</summary>
    public sealed record RunSettlementRequest(DateTime PeriodStartUtc, DateTime PeriodEndUtc, Guid? SellerId = null);
    public sealed record MarkPaidRequest(string ProviderRef);
    public sealed record CustomerRefundRequest(Guid OrderId, decimal Amount, string Msisdn, string Provider, string Reason);
    public sealed record CommissionRuleRequest(
        string Scope, Guid? TargetId, decimal Rate, decimal FixedFee, string Currency,
        decimal? MinFee, decimal? MaxFee, DateTime? EffectiveFromUtc);
    public sealed record CreateInvoiceRequest(Guid SellerId, DateTime PeriodStartUtc, DateTime PeriodEndUtc, string Currency);
    public sealed record InvoiceLineRequest(string Description, decimal Amount);
}
