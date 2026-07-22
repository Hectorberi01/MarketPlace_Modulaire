using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Shipping.Application.Carriers;

namespace Marketplace.Bff.Admin.Endpoints;

/// <summary>
/// BFF Admin — catalogue des transporteurs. L'admin gère la liste que les vendeurs
/// utilisent à l'expédition (nom, code, modèle d'URL de suivi, actif/inactif).
/// </summary>
public static class AdminShippingEndpoints
{
    public static IEndpointRouteBuilder MapAdminShippingEndpoints(this IEndpointRouteBuilder app)
    {
        var carriers = app.MapAdminGroup("/admin/carriers", "Admin · Transporteurs");

        carriers.MapGet("/", ListAsync);
        carriers.MapGet("/{id:guid}", GetAsync);
        carriers.MapPost("/", CreateAsync);
        carriers.MapPut("/{id:guid}", UpdateAsync);
        carriers.MapPost("/{id:guid}/activate", ActivateAsync);
        carriers.MapPost("/{id:guid}/deactivate", DeactivateAsync);

        return app;
    }

    private static async Task<IResult> ListAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListCarriersQuery(), ct)).Match(c => Results.Ok(c));

    private static async Task<IResult> GetAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetCarrierQuery(id), ct)).Match(c => Results.Ok(c));

    private static async Task<IResult> CreateAsync(CarrierCreateRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new CreateCarrierCommand(request.Name, request.Code, request.TrackingUrlTemplate, request.LogoUrl), ct))
            .Match(id => Results.Ok(new { carrierId = id }));

    private static async Task<IResult> UpdateAsync(Guid id, CarrierUpdateRequest request, ISender sender, CancellationToken ct)
        => (await sender.Send(new UpdateCarrierCommand(id, request.Name, request.TrackingUrlTemplate, request.LogoUrl), ct))
            .Match(() => Results.NoContent());

    private static async Task<IResult> ActivateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetCarrierActiveCommand(id, true), ct)).Match(() => Results.NoContent());

    private static async Task<IResult> DeactivateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new SetCarrierActiveCommand(id, false), ct)).Match(() => Results.NoContent());

    /// <summary>Le code est immuable après création (référencé par les expéditions).</summary>
    public sealed record CarrierCreateRequest(string Name, string Code, string? TrackingUrlTemplate, string? LogoUrl);
    public sealed record CarrierUpdateRequest(string Name, string? TrackingUrlTemplate, string? LogoUrl);
}
