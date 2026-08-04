using FluentAssertions;
using Marketplace.Modules.Billing.Application.Abstractions;
using Marketplace.Modules.Billing.Application.Commissions;
using Marketplace.Modules.Billing.Domain.Commissions;
using Xunit;

namespace Billing.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// CES TESTS EXISTENT POUR EMPÊCHER UN BOGUE PRÉCIS DE REVENIR.
///
/// La modification d'une règle de commission remettait sa date d'entrée en vigueur à
/// « maintenant ». Le chemin complet : la console d'administration n'envoyait pas le
/// champ (son type TypeScript ne le portait même pas), le BFF comblait le vide par
/// « ?? DateTime.UtcNow », et le handler écrivait cette date.
///
/// Deux conséquences, toutes deux silencieuses :
///   • une règle PROGRAMMÉE devenait applicable sur-le-champ
///     (<see cref="CommissionRule.IsApplicableAt"/> teste « EffectiveFromUtc &lt;= now ») ;
///   • elle passait DEVANT ses sœurs de même portée, que
///     <see cref="CommissionResolver"/> départage par date décroissante.
///
/// Corriger un taux ne doit pas déplacer une date que personne n'a touchée. C'est la
/// seule chose que ces tests vérifient — mais rien d'autre ne la vérifie.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public class UpdateCommissionRuleCommandHandlerTests
{
    private static readonly DateTime Programmee = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

    private static CommissionRule Rule(DateTime effectiveFromUtc)
        => CommissionRule.Create(CommissionScope.Global, null, 0.10m, 0m, "XOF", null, null, effectiveFromUtc).Value;

    [Fact]
    public async Task Update_sans_date_preserve_la_date_existante()
    {
        var rule = Rule(Programmee);
        var repository = new FakeCommissionRuleRepository(rule);
        var unitOfWork = new FakeBillingUnitOfWork();
        var handler = new UpdateCommissionRuleCommandHandler(repository, unitOfWork);

        // Le cas réel : l'administrateur corrige le taux, l'écran n'expose pas la date.
        var command = new UpdateCommissionRuleCommand(
            rule.Id.Value, 0.12m, 0m, "XOF", null, null, EffectiveFromUtc: null);

        var result = await handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rule.Rate.Should().Be(0.12m);
        rule.EffectiveFromUtc.Should().Be(Programmee, "une date qu'aucun écran n'a modifiée ne doit pas bouger");
        unitOfWork.SaveCount.Should().Be(1);
    }

    [Fact]
    public async Task Update_sans_date_ne_rend_pas_applicable_une_regle_programmee()
    {
        // La conséquence métier, exprimée telle qu'elle se manifestait : la règle du
        // 1er septembre s'appliquait dès le jour de la modification.
        var avantEcheance = Programmee.AddDays(-30);
        var rule = Rule(Programmee);
        var repository = new FakeCommissionRuleRepository(rule);
        var handler = new UpdateCommissionRuleCommandHandler(repository, new FakeBillingUnitOfWork());

        rule.IsApplicableAt(avantEcheance).Should().BeFalse();

        await handler.Handle(
            new UpdateCommissionRuleCommand(rule.Id.Value, 0.12m, 0m, "XOF", null, null, null),
            CancellationToken.None);

        rule.IsApplicableAt(avantEcheance).Should().BeFalse("corriger un taux n'avance pas l'entrée en vigueur");
        rule.IsApplicableAt(Programmee).Should().BeTrue();
    }

    [Fact]
    public async Task Update_avec_date_applique_la_date_fournie()
    {
        // Le pendant du test précédent : quand l'appelant DÉCIDE d'une date, elle passe.
        // Sans cette assertion, « préserver toujours » satisferait les deux autres tests.
        var nouvelle = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc);
        var rule = Rule(Programmee);
        var repository = new FakeCommissionRuleRepository(rule);
        var handler = new UpdateCommissionRuleCommandHandler(repository, new FakeBillingUnitOfWork());

        var result = await handler.Handle(
            new UpdateCommissionRuleCommand(rule.Id.Value, 0.10m, 0m, "XOF", null, null, nouvelle),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        rule.EffectiveFromUtc.Should().Be(nouvelle);
    }

    [Fact]
    public async Task Update_dune_regle_inconnue_echoue_sans_ecrire()
    {
        var repository = new FakeCommissionRuleRepository();
        var unitOfWork = new FakeBillingUnitOfWork();
        var handler = new UpdateCommissionRuleCommandHandler(repository, unitOfWork);

        var result = await handler.Handle(
            new UpdateCommissionRuleCommand(Guid.NewGuid(), 0.10m, 0m, "XOF", null, null, null),
            CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("billing.rule.not_found");
        unitOfWork.SaveCount.Should().Be(0);
    }

    // ---------------------------------------------------------------- doublures

    private sealed class FakeCommissionRuleRepository : ICommissionRuleRepository
    {
        private readonly List<CommissionRule> _rules;

        public FakeCommissionRuleRepository(params CommissionRule[] rules) => _rules = [.. rules];

        public Task AddAsync(CommissionRule rule, CancellationToken cancellationToken = default)
        {
            _rules.Add(rule);
            return Task.CompletedTask;
        }

        public void Remove(CommissionRule rule) => _rules.Remove(rule);

        public Task<CommissionRule?> GetByIdAsync(CommissionRuleId id, CancellationToken cancellationToken = default)
            => Task.FromResult(_rules.FirstOrDefault(r => r.Id == id));

        public Task<IReadOnlyList<CommissionRule>> GetCandidatesAsync(Guid sellerId, Guid categoryId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CommissionRule>>(_rules);

        public Task<IReadOnlyList<CommissionRule>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CommissionRule>>(_rules);
    }

    private sealed class FakeBillingUnitOfWork : IBillingUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }
}
