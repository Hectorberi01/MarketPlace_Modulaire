using FluentAssertions;
using Marketplace.Modules.Fraud.Domain.Risk;
using Xunit;

namespace Fraud.UnitTests;

public class FraudTests
{
    [Fact]
    public void Scorer_autorise_quand_signaux_faibles()
    {
        var (score, decision) = RiskScorer.Evaluate(new Dictionary<string, double> { ["velocity"] = 0.1, ["geo"] = 0.1 });

        score.Should().BeApproximately(0.2, 0.0001);
        decision.Should().Be(RiskDecision.Allow);
    }

    [Fact]
    public void Scorer_met_en_revue_dans_la_zone_intermediaire()
    {
        var (_, decision) = RiskScorer.Evaluate(new Dictionary<string, double> { ["velocity"] = 0.5 });
        decision.Should().Be(RiskDecision.Review);
    }

    [Fact]
    public void Scorer_bloque_et_borne_le_score_a_1()
    {
        var (score, decision) = RiskScorer.Evaluate(new Dictionary<string, double> { ["a"] = 0.8, ["b"] = 0.8 });

        score.Should().Be(1.0);
        decision.Should().Be(RiskDecision.Block);
    }

    [Fact]
    public void Create_borne_le_score_et_exige_un_sujet()
    {
        RiskAssessment.Create(RiskSubjectType.Order, Guid.Empty, 0.5, "{}", RiskDecision.Review).IsFailure.Should().BeTrue();

        var ok = RiskAssessment.Create(RiskSubjectType.User, Guid.NewGuid(), 1.5, "{}", RiskDecision.Block);
        ok.IsSuccess.Should().BeTrue();
        ok.Value.Score.Should().Be(1.0);
    }
}
