using FluentAssertions;
using Marketplace.BuildingBlocks.Infrastructure.Outbox;
using Xunit;

namespace BuildingBlocks.UnitTests;

/// <summary>
/// Le cycle de vie d'un message qui échoue : temporisé, temporisé, … puis enterré.
/// </summary>
public class OutboxDeadLetterTests
{
    private static readonly Random Random = new(42);
    private static readonly DateTime Now = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

    private static OutboxMessage NewMessage() => new()
    {
        Type = "Some.Event, Some.Assembly",
        Content = "{}",
        OccurredOnUtc = Now.AddMinutes(-1),
    };

    [Fact]
    public void Un_echec_TEMPORISE_le_message__il_quitte_le_lot_courant()
    {
        // ═══════════════════════════════════════════════════════════════════════════
        // C'EST LA PROPRIÉTÉ LA PLUS IMPORTANTE DE TOUT LE MÉCANISME.
        //
        // Le lot du processeur est trié par date et plafonné à 50. Avant, un message en
        // échec restait éligible en permanence : il confisquait une place, tour après tour,
        // pour toujours. À 50 messages empoisonnés, l'outbox du module était MORTE — plus
        // une commande confirmée, plus un vendeur crédité.
        //
        // `NextAttemptAtUtc` le sort du lot. Les messages sains passent devant. C'est cette
        // seule ligne qui transforme une file bloquée en file qui avance.
        // ═══════════════════════════════════════════════════════════════════════════
        var policy = new OutboxRetryPolicy();
        var message = NewMessage();

        var deadLettered = policy.RegisterFailure(message, "boom", Random, Now);

        deadLettered.Should().BeFalse();
        message.AttemptCount.Should().Be(1);

        message.NextAttemptAtUtc.Should().NotBeNull();
        message.NextAttemptAtUtc!.Value.Should().BeAfter(Now,
            "le message doit être ÉCARTÉ du lot — s'il restait éligible immédiatement, il "
            + "continuerait de confisquer sa place et le blocage de tête de file persisterait");

        message.DeadLetteredOnUtc.Should().BeNull();
        message.ProcessedOnUtc.Should().BeNull();
    }

    [Fact]
    public void Au_bout_du_plafond__le_message_part_en_LETTRE_MORTE()
    {
        var policy = new OutboxRetryPolicy { MaxAttempts = 3 };
        var message = NewMessage();

        policy.RegisterFailure(message, "boom", Random, Now).Should().BeFalse();
        policy.RegisterFailure(message, "boom", Random, Now).Should().BeFalse();

        var deadLettered = policy.RegisterFailure(message, "boom", Random, Now);

        deadLettered.Should().BeTrue();
        message.DeadLetteredOnUtc.Should().Be(Now);
        message.AttemptCount.Should().Be(3);

        // Un message enterré n'a plus de « prochaine tentative » : il n'en aura plus.
        // Laisser une date ici laisserait croire qu'il va revenir.
        message.NextAttemptAtUtc.Should().BeNull();
    }

    [Fact]
    public void Un_message_enterre_n_est_PAS_marque_traite__la_perte_doit_rester_visible()
    {
        // Une lettre morte est une perte métier : un e-mail jamais envoyé, un gain jamais
        // crédité. La marquer « traitée » la ferait disparaître des radars — on aurait
        // remplacé une boucle bruyante par une perte silencieuse. Ce serait PIRE.
        var policy = new OutboxRetryPolicy { MaxAttempts = 1 };
        var message = NewMessage();

        policy.RegisterFailure(message, "boom", Random, Now).Should().BeTrue();

        message.ProcessedOnUtc.Should().BeNull(
            "une lettre morte doit rester visible dans /admin/outbox/dead-letters, pas être classée");
        message.Error.Should().Be("boom");
    }

    [Fact]
    public void Un_message_de_erreur_gigantesque_est_tronque__il_ne_fait_pas_exploser_la_ligne()
    {
        var policy = new OutboxRetryPolicy();
        var message = NewMessage();
        var huge = new string('x', 50_000);

        policy.RegisterFailure(message, huge, Random, Now);

        message.Error!.Length.Should().Be(2000);
    }

    [Fact]
    public void Le_delai_croit_a_chaque_echec_successif()
    {
        var policy = new OutboxRetryPolicy
        {
            BaseDelay = TimeSpan.FromSeconds(10),
            MaxDelay = TimeSpan.FromHours(1),
        };
        var message = NewMessage();

        policy.RegisterFailure(message, "boom", Random, Now);
        var firstDelay = message.NextAttemptAtUtc!.Value - Now;

        policy.RegisterFailure(message, "boom", Random, Now);
        var secondDelay = message.NextAttemptAtUtc!.Value - Now;

        policy.RegisterFailure(message, "boom", Random, Now);
        var thirdDelay = message.NextAttemptAtUtc!.Value - Now;

        secondDelay.Should().BeGreaterThan(firstDelay);
        thirdDelay.Should().BeGreaterThan(secondDelay);
    }
}
