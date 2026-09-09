using FluentAssertions;
using Marketplace.Modules.Payments.Infrastructure.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Real;
using Xunit;

namespace Payments.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// LES DEUX PASSERELLES RÉELLEMENT DÉPLOYÉES, TESTÉES DIRECTEMENT.
///
/// Un test existait déjà, intitulé « LE test qui compte vraiment ». Il exerçait
/// `GatewayWebhook.VerifySignature` — le helper générique. Or FedaPay et Stripe
/// SURCHARGENT `ParseWebhookAsync` et ne traversent jamais ce helper : leurs propres
/// vérificateurs retournaient `true` sur secret vide, et le test ne pouvait pas le
/// voir. Un test qui verrouille un chemin que le code n'emprunte pas est pire qu'une
/// absence de test : il rassure.
///
/// D'où ces tests-ci, qui appellent les fonctions par leur nom.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class SignatureDesPasserellesTests
{
    private const string Secret = "whsec_test_0123456789";
    private const string Body = "{\"name\":\"transaction.approved\",\"entity\":{\"id\":\"42\"}}";

    // ── Secret manquant ─────────────────────────────────────────────────────────

    [Fact]
    public void FedaPay_sans_secret_et_sans_mode_permissif_rejette()
    {
        WithPermissiveMode(false, () =>
        {
            // Le scénario réel : clé d'API renseignée, secret de webhook oublié dans le
            // vault. La passerelle réelle est enregistrée et doit refuser tout rappel.
            FedaPayHttpGateway.VerifyFedaPaySignature(Body, signatureHeader: null, secret: string.Empty)
                .Should().BeFalse(
                    "un rappel non signé sans secret configuré ne doit JAMAIS marquer une commande payée");

            // Y compris avec un header : sans secret, rien n'est vérifiable.
            FedaPayHttpGateway.VerifyFedaPaySignature(Body, "t=1,s=deadbeef", string.Empty)
                .Should().BeFalse();
        });
    }

    [Fact]
    public void FedaPay_sans_secret_en_mode_permissif_accepte()
    {
        // Le bac à sable, et lui seul. Ce test existe pour que le mode permissif reste
        // un choix explicite du démarrage, pas un comportement par défaut oublié.
        WithPermissiveMode(true, () =>
            FedaPayHttpGateway.VerifyFedaPaySignature(Body, signatureHeader: null, secret: string.Empty)
                .Should().BeTrue());
    }

    // ── Signature valide, horodatage valide ─────────────────────────────────────

    [Fact]
    public void FedaPay_accepte_une_signature_juste_et_recente()
    {
        var header = SignFedaPay(Body, Secret, DateTimeOffset.UtcNow);

        FedaPayHttpGateway.VerifyFedaPaySignature(Body, header, Secret).Should().BeTrue();
    }

    [Fact]
    public void FedaPay_refuse_un_corps_modifie()
    {
        var header = SignFedaPay(Body, Secret, DateTimeOffset.UtcNow);

        FedaPayHttpGateway.VerifyFedaPaySignature(Body.Replace("42", "43"), header, Secret)
            .Should().BeFalse();
    }

    [Fact]
    public void FedaPay_refuse_un_rejeu_ancien()
    {
        // Signature parfaitement valide, mais horodatée d'il y a une heure : c'est un
        // corps capturé et rejoué. L'horodatage était signé et jamais regardé.
        var header = SignFedaPay(Body, Secret, DateTimeOffset.UtcNow.AddHours(-1));

        FedaPayHttpGateway.VerifyFedaPaySignature(Body, header, Secret).Should().BeFalse();
    }

    [Fact]
    public void FedaPay_refuse_un_horodatage_dans_le_futur()
    {
        // Une horloge prestataire très en avance est aussi suspecte qu'un rejeu :
        // l'accepter rendrait la fenêtre unilatérale, donc contournable.
        var header = SignFedaPay(Body, Secret, DateTimeOffset.UtcNow.AddHours(1));

        FedaPayHttpGateway.VerifyFedaPaySignature(Body, header, Secret).Should().BeFalse();
    }

    // ── La fenêtre d'horloge, isolément ─────────────────────────────────────────

    [Theory]
    [InlineData(0, true)]
    [InlineData(-240, true)]
    [InlineData(-360, false)]
    [InlineData(240, true)]
    [InlineData(360, false)]
    public void La_fenetre_d_horloge_est_symetrique(int decalageSecondes, bool attendu)
    {
        var reference = DateTimeOffset.UtcNow;
        var horodatage = reference.AddSeconds(decalageSecondes).ToUnixTimeSeconds().ToString();

        GatewayWebhook.IsWithinClockSkew(horodatage, reference).Should().Be(attendu);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bientot")]
    public void Un_horodatage_illisible_est_refuse(string? valeur)
    {
        // « Je ne comprends pas, donc j'accepte » est la forme la plus courante du
        // fail-open. Il n'y a aucune raison légitime d'envoyer autre chose qu'un
        // temps Unix en secondes.
        GatewayWebhook.IsWithinClockSkew(valeur).Should().BeFalse();
    }

    // ── Montage ─────────────────────────────────────────────────────────────────

    private static string SignFedaPay(string body, string secret, DateTimeOffset sentAt)
    {
        var t = sentAt.ToUnixTimeSeconds().ToString();
        var signature = GatewayWebhook.ComputeSignature($"{t}.{body}", secret);
        return $"t={t},s={signature}";
    }

    private static void WithPermissiveMode(bool permissive, Action assertion)
    {
        var previous = GatewayWebhook.AllowUnsignedWhenSecretMissing;
        try
        {
            GatewayWebhook.AllowUnsignedWhenSecretMissing = permissive;
            assertion();
        }
        finally
        {
            GatewayWebhook.AllowUnsignedWhenSecretMissing = previous;
        }
    }
}
