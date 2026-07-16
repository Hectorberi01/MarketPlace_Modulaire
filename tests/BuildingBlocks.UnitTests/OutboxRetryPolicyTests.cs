using FluentAssertions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Xunit;

namespace BuildingBlocks.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// L'OUTBOX N'AVAIT NI PLAFOND, NI TEMPORISATION, NI ISSUE.
///
/// En cas d'échec, l'ancien processeur notait l'erreur, la journalisait… et laissait
/// `ProcessedOnUtc` à null. Le message revenait donc au tour suivant. Toutes les
/// 5 secondes. Pour l'éternité.
///
/// Le vrai danger n'était pas le bruit dans les logs, mais le BLOCAGE DE TÊTE DE FILE :
/// le lot est trié par date et plafonné à 50. Chaque message empoisonné confisquait une
/// place, définitivement. À 50 messages morts, plus AUCUN événement ne passait — plus une
/// commande confirmée, plus un vendeur crédité, plus une notification envoyée.
///
/// Ces tests gardent les deux propriétés qui ferment cela : le backoff (qui sort le
/// message du lot) et le plafond (qui finit par l'enterrer, visiblement).
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class OutboxRetryPolicyTests
{
    private static readonly Random Deterministic = new(1234);

    [Fact]
    public void Le_delai_croit_de_maniere_exponentielle()
    {
        var policy = new OutboxRetryPolicy
        {
            BaseDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromMinutes(30),
        };

        var first = policy.DelayFor(1, Deterministic);
        var third = policy.DelayFor(3, Deterministic);
        var sixth = policy.DelayFor(6, Deterministic);

        // ±20 % de gigue : on vérifie des ORDRES DE GRANDEUR, pas des égalités.
        first.TotalSeconds.Should().BeInRange(8, 12);      // ~10 s
        third.TotalSeconds.Should().BeInRange(32, 48);     // ~40 s
        sixth.TotalSeconds.Should().BeInRange(256, 384);   // ~320 s
    }

    [Fact]
    public void Le_delai_est_PLAFONNE__une_panne_longue_ne_repousse_pas_le_rejeu_a_la_semaine_prochaine()
    {
        var policy = new OutboxRetryPolicy
        {
            BaseDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromMinutes(30),
        };

        var late = policy.DelayFor(15, Deterministic);

        // 2^14 × 10 s ferait 45 heures. Le plafond ramène à 30 min (± gigue).
        late.Should().BeLessThan(TimeSpan.FromMinutes(37));
        late.Should().BeGreaterThan(TimeSpan.FromMinutes(23));
    }

    [Fact]
    public void Un_nombre_de_tentatives_absurde_ne_produit_JAMAIS_de_delai_negatif()
    {
        // Le piège : 2^n en ticks déborde très vite. Un délai négatif signifierait
        // « rejouable immédiatement » — c'est-à-dire EXACTEMENT la boucle infinie qu'on
        // vient de fermer, réintroduite par un dépassement arithmétique silencieux.
        var policy = new OutboxRetryPolicy();

        foreach (var attempt in new[] { 1, 10, 50, 1_000, int.MaxValue })
        {
            var delay = policy.DelayFor(attempt, Deterministic);

            delay.Should().BeGreaterThan(TimeSpan.Zero,
                $"un délai négatif ou nul (tentative {attempt}) ramènerait la boucle infinie");
            delay.Should().BeLessThan(TimeSpan.FromHours(1));
        }
    }

    [Fact]
    public void La_gigue_etale_les_rejeux__pas_de_troupeau_qui_charge()
    {
        // Sans gigue, tous les messages tombés pendant la même panne — souvent des
        // centaines — reviendraient EXACTEMENT au même instant et achèveraient le service
        // qui vient à peine de se relever. La gigue doit donc réellement varier.
        var policy = new OutboxRetryPolicy();
        var random = new Random(7);

        var delays = Enumerable.Range(0, 50)
            .Select(_ => policy.DelayFor(4, random).TotalMilliseconds)
            .ToList();

        delays.Distinct().Should().HaveCountGreaterThan(40,
            "les délais doivent être étalés, pas tous identiques");
    }

    [Fact]
    public void Le_plafond_par_defaut_laisse_environ_deux_heures_avant_d_abandonner()
    {
        // Garde l'équilibre entre les deux erreurs symétriques :
        //   • trop court → on enterre des messages sains pendant une panne passagère
        //     (un vendeur jamais crédité parce que Resend a hoqueté 3 minutes) ;
        //   • trop long  → on retombe dans la boucle sans fin.
        var policy = new OutboxRetryPolicy();

        policy.MaxAttempts.Should().Be(10);

        var total = Enumerable.Range(1, policy.MaxAttempts - 1)
            .Aggregate(TimeSpan.Zero, (acc, n) => acc + policy.DelayFor(n, Deterministic));

        total.Should().BeGreaterThan(TimeSpan.FromMinutes(45),
            "abandonner en quelques minutes enterrerait des messages sains lors d'une panne ordinaire");
        total.Should().BeLessThan(TimeSpan.FromHours(6),
            "insister des jours reviendrait à la boucle qu'on vient de fermer");
    }
}
