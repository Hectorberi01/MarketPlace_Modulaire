using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Contracts;
using Marketplace.Modules.Settlement.Application.Batches;
using Marketplace.Modules.Settlement.Application.Wallets;
using Marketplace.Modules.Settlement.Contracts;
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
        settlement.MapPost("/batches/{id:guid}/cancel", CancelBatchAsync);

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

    private static async Task<IResult> ListBatchesAsync(ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var result = await sender.Send(new ListSettlementBatchesQuery(), ct);
        if (result.IsFailure)
        {
            return result.Match(b => Results.Ok(b));
        }

        // Résolution des noms de boutique : un lot n'expose que des SellerId, or la
        // console doit montrer À QUI le lot verse. Cache local pour ne pas relire deux
        // fois le même vendeur sur plusieurs lots.
        var names = new Dictionary<Guid, string>();
        var batches = new List<object>(result.Value.Count);
        foreach (var b in result.Value)
        {
            batches.Add(await EnrichBatchAsync(b, sellers, names, ct));
        }

        return Results.Ok(batches);
    }

    private static async Task<IResult> GetBatchAsync(Guid id, ISender sender, ISellerModuleApi sellers, CancellationToken ct)
    {
        var result = await sender.Send(new GetSettlementBatchQuery(id), ct);
        return result.IsSuccess
            ? Results.Ok(await EnrichBatchAsync(result.Value, sellers, new Dictionary<Guid, string>(), ct))
            : result.Match(b => Results.Ok(b));
    }

    /// <summary>Ajoute le nom de boutique à chaque reversement du lot.</summary>
    private static async Task<object> EnrichBatchAsync(
        SettlementBatchSummary batch, ISellerModuleApi sellers, Dictionary<Guid, string> names, CancellationToken ct)
    {
        var payouts = new List<object>(batch.Payouts.Count);
        foreach (var p in batch.Payouts)
        {
            if (!names.TryGetValue(p.SellerId, out var name))
            {
                name = (await sellers.GetSellerAsync(p.SellerId, ct))?.ShopName ?? "Vendeur";
                names[p.SellerId] = name;
            }

            payouts.Add(new
            {
                id = p.Id,
                payoutId = p.Id,
                sellerId = p.SellerId,
                sellerName = name,
                grossAmount = p.GrossAmount,
                commissionAmount = p.CommissionAmount,
                netAmount = p.NetAmount,
                amount = p.NetAmount,
                currency = p.Currency,
                status = p.Status,
                providerRef = p.ProviderRef,
                paidAtUtc = p.PaidAtUtc,
            });
        }

        return new
        {
            id = batch.Id,
            periodStartUtc = batch.PeriodStartUtc,
            periodEndUtc = batch.PeriodEndUtc,
            currency = batch.Currency,
            totalNet = batch.TotalNet,
            status = batch.Status,
            createdAtUtc = batch.CreatedAtUtc,
            payouts,
        };
    }

    /// <summary>Annule un lot encore en attente (ses gains redeviennent payables).</summary>
    private static async Task<IResult> CancelBatchAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new CancelSettlementBatchCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Génère un lot de reversements sur une période (un payout net par vendeur).</summary>
    private static async Task<IResult> RunAsync(RunSettlementRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new RunSettlementCommand(request.PeriodStartUtc, request.PeriodEndUtc, request.SellerId), ct))
            .Match(id => Results.Ok(new { batchId = id }));

    /// <summary>Marque un reversement comme versé (réf. transfert MoMo/banque).</summary>
    private static async Task<IResult> MarkPaidAsync(Guid batchId, Guid payoutId, MarkPaidRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new MarkPayoutPaidCommand(batchId, payoutId, request.ProviderRef), ct)).Match(() => Results.NoContent());

    /// <summary>
    /// Rembourse un CLIENT sur une commande (hors flux retour), CHAQUE PART PAR SON CANAL.
    ///
    /// Ce qui a été encaissé en espèces repart en payout FedaPay MoMo ; ce qui avait été
    /// réglé avec la cagnotte y retourne. La réponse détaille les deux parts — l'écran
    /// doit les montrer, sinon un administrateur croira que 13 000 sont partis sur le
    /// téléphone du client alors que 5 000 sont revenus en cagnotte.
    ///
    /// `msisdn` et `provider` ne sont requis que s'il y a une part en espèces.
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

    /// <summary>
    /// Modifie une règle de commission.
    ///
    /// ATTENTION : `EffectiveFromUtc` est transmis TEL QUEL, sans `?? DateTime.UtcNow`.
    ///
    /// À la création, ce repli est légitime : une règle neuve sans date entre en vigueur
    /// tout de suite. À la modification, il était destructeur — il transformait « le client
    /// n'a pas envoyé la date » en « la date est maintenant », et faisait basculer en
    /// vigueur immédiate toute règle programmée dès qu'on en corrigeait le taux. C'est au
    /// handler de décider, et il préserve la date existante.
    /// </summary>
    private static async Task<IResult> UpdateCommissionRuleAsync(Guid id, CommissionRuleRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateCommissionRuleCommand(
                id, request.Rate, request.FixedFee, request.Currency,
                request.MinFee, request.MaxFee, request.EffectiveFromUtc), ct))
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
    /// <summary>`Msisdn` et `Provider` peuvent être vides si la commande a été réglée
    /// entièrement avec la cagnotte : aucun versement n'a alors lieu.</summary>
    public sealed record CustomerRefundRequest(
        Guid OrderId, decimal Amount, string Reason, string Msisdn = "", string Provider = "");
    public sealed record CommissionRuleRequest(
        string Scope, Guid? TargetId, decimal Rate, decimal FixedFee, string Currency,
        decimal? MinFee, decimal? MaxFee, DateTime? EffectiveFromUtc);
    public sealed record CreateInvoiceRequest(Guid SellerId, DateTime PeriodStartUtc, DateTime PeriodEndUtc, string Currency);
    public sealed record InvoiceLineRequest(string Description, decimal Amount);
}
