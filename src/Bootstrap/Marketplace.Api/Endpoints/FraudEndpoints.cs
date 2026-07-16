using MediatR;
using Marketplace.Api.Extensions;
using Marketplace.Modules.Fraud.Application.Risk;

namespace Marketplace.Api.Endpoints;

/// <summary>
/// Endpoints HTTP du module Fraud : évaluation du risque d'un sujet (commande,
/// utilisateur, vendeur) par signaux pondérés et consultation de la dernière
/// décision. Réservé aux administrateurs / systèmes anti-fraude.
/// </summary>
public static class FraudEndpoints
{
    /// <summary>Enregistre les routes anti-fraude.</summary>
    public static IEndpointRouteBuilder MapFraudEndpoints(this IEndpointRouteBuilder app)
    {
        var fraud = app.MapAdminGroup("/api/fraud").WithTags("Fraud");

        fraud.MapPost("/assess", AssessAsync).WithName("AssessRisk").RequireAuthorization(p => p.RequireRole("Admin"));
        fraud.MapGet("/assessments/{subjectType}/{subjectId:guid}", LatestAsync).WithName("GetRiskAssessment").RequireAuthorization(p => p.RequireRole("Admin"));

        return app;
    }

    /// <summary>Évalue le risque d'un sujet à partir de signaux pondérés.</summary>
    private static async Task<IResult> AssessAsync(AssessBody body, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(
            new AssessRiskCommand(body.SubjectType, body.SubjectId, body.Signals ?? new Dictionary<string, double>()), ct);
        return result.Match(id => Results.Ok(new { id }));
    }

    /// <summary>Dernière évaluation de risque d'un sujet.</summary>
    private static async Task<IResult> LatestAsync(string subjectType, Guid subjectId, ISender sender, CancellationToken ct)
        => (await sender.Send(new GetRiskAssessmentQuery(subjectType, subjectId), ct)).Match(a => Results.Ok(a));

    public sealed record AssessBody(string SubjectType, Guid SubjectId, IReadOnlyDictionary<string, double>? Signals);
}
