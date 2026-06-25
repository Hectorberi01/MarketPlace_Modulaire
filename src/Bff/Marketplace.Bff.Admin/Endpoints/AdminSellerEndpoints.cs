using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.ActivateSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.ApproveKyb;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.RejectKyb;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.SetCommissionRate;
using Marketplace.Modules.Sellers.Application.Sellers.Commands.SuspendSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Queries.GetSeller;
using Marketplace.Modules.Sellers.Application.Sellers.Queries.ListSellers;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>BFF Admin — gouvernance des vendeurs : KYB, activation/suspension, commission.</summary>
public static class AdminSellerEndpoints
{
    public static IEndpointRouteBuilder MapAdminSellerEndpoints(this IEndpointRouteBuilder app)
    {
        var sellers = app.MapAdminGroup("/admin/sellers", "Admin · Vendeurs");

        sellers.MapGet("/", ListAsync);
        sellers.MapGet("/{id:guid}", GetAsync);
        sellers.MapPost("/{id:guid}/kyb/approve", ApproveKybAsync);
        sellers.MapPost("/{id:guid}/kyb/reject", RejectKybAsync);
        sellers.MapPost("/{id:guid}/activate", ActivateAsync);
        sellers.MapPost("/{id:guid}/suspend", SuspendAsync);
        sellers.MapPut("/{id:guid}/commission-rate", SetCommissionRateAsync);

        return app;
    }

    /// <summary>Liste des vendeurs (back-office).</summary>
    private static async Task<IResult> ListAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListSellersQuery(), ct)).Match(s => Results.Ok(s));

    /// <summary>Détail d'un vendeur.</summary>
    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetSellerQuery(id), ct)).Match(s => Results.Ok(s));

    /// <summary>Valide le KYB.</summary>
    private static async Task<IResult> ApproveKybAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ApproveKybCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Rejette le KYB.</summary>
    private static async Task<IResult> RejectKybAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new RejectKybCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Active le vendeur (KYB validé + compte de reversement requis).</summary>
    private static async Task<IResult> ActivateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new ActivateSellerCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Suspend le vendeur.</summary>
    private static async Task<IResult> SuspendAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new SuspendSellerCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Définit le taux de commission négocié.</summary>
    private static async Task<IResult> SetCommissionRateAsync(Guid id, CommissionRateRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetCommissionRateCommand(id, request.CommissionRate), ct)).Match(() => Results.NoContent());

    public sealed record CommissionRateRequest(decimal CommissionRate);
}
