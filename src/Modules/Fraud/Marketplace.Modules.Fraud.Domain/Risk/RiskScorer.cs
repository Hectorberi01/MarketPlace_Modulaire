namespace Marketplace.Modules.Fraud.Domain.Risk;

/// <summary>
/// Moteur de scoring par règles : agrège les contributions des signaux (vélocité,
/// géoloc, montant inhabituel…) en un score 0–1 et en déduit une décision. Une
/// implémentation ML remplacerait ce calcul sans changer le contrat.
/// </summary>
public static class RiskScorer
{
    public const double ReviewThreshold = 0.4d;
    public const double BlockThreshold = 0.7d;

    public static (double Score, RiskDecision Decision) Evaluate(IReadOnlyDictionary<string, double> signals)
    {
        var score = signals is null || signals.Count == 0
            ? 0d
            : Math.Clamp(signals.Values.Sum(), 0d, 1d);

        var decision = score >= BlockThreshold ? RiskDecision.Block
            : score >= ReviewThreshold ? RiskDecision.Review
            : RiskDecision.Allow;

        return (score, decision);
    }
}
