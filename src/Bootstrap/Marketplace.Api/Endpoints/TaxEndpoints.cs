using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Tax.Application.Taxes;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Tax : règles fiscales par juridiction et calcul de
/// taxe. Gestion réservée aux administrateurs ; l'aperçu de calcul est public.
/// </summary>
public static class TaxEndpoints
{
    /// <summary>Enregistre les routes fiscales.</summary>
    public static IEndpointRouteBuilder MapTaxEndpoints(this IEndpointRouteBuilder app)
    {
        var tax = app.MapGroup("/api/tax").WithTags("Tax");

        tax.MapPost("/rules", CreateAsync).WithName("CreateTaxRule").RequireAuthorization(p => p.RequireRole("Admin"));
        tax.MapGet("/rules", ListAsync).WithName("ListTaxRules").RequireAuthorization(p => p.RequireRole("Admin"));
        tax.MapPost("/rules/{id:guid}/deactivate", DeactivateAsync).WithName("DeactivateTaxRule").RequireAuthorization(p => p.RequireRole("Admin"));
        tax.MapGet("/compute", ComputeAsync).WithName("ComputeTax").AllowAnonymous();

        return app;
    }

    /// <summary>Crée une règle fiscale.</summary>
    private static async Task<IResult> CreateAsync(CreateBody body, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new CreateTaxRuleCommand(body.Jurisdiction, body.CategoryId, body.Rate, body.Type, body.EffectiveFromUtc), ct);
        return result.Match(id => Results.Created($"/api/tax/rules/{id}", new { id }));
    }

    /// <summary>Liste les règles fiscales.</summary>
    private static async Task<IResult> ListAsync(ISender sender, CancellationToken ct)
        => (await sender.Send(new ListTaxRulesQuery(), ct)).Match(rules => Results.Ok(rules));

    /// <summary>Désactive une règle fiscale.</summary>
    private static async Task<IResult> DeactivateAsync(Guid id, ISender sender, CancellationToken ct)
        => (await sender.Send(new DeactivateTaxRuleCommand(id), ct)).Match(() => Results.NoContent());

    /// <summary>Calcule la taxe applicable à un montant (juridiction + catégorie).</summary>
    private static async Task<IResult> ComputeAsync(string jurisdiction, Guid categoryId, decimal amount, ISender sender, CancellationToken ct)
        => (await sender.Send(new ComputeTaxQuery(jurisdiction, categoryId, amount), ct)).Match(r => Results.Ok(r));

    public sealed record CreateBody(string Jurisdiction, Guid? CategoryId, decimal Rate, string Type, DateTime? EffectiveFromUtc);
}
