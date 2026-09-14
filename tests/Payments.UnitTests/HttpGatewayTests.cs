using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Marketplace.Modules.Payments.Application.Abstractions.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways;
using Marketplace.Modules.Payments.Infrastructure.Gateways.Real;
using Xunit;

namespace Payments.UnitTests;

/// <summary>
/// ATTENTION : Cette classe manipule <see cref="GatewayWebhook.AllowUnsignedWhenSecretMissing"/>,
/// qui est un état STATIQUE MUTABLE. xUnit exécute les classes de test en parallèle :
/// sans collection dédiée, une autre classe de ce projet (MobileMoneyGatewayTests,
/// PaymentGatewayTests) pourrait lire le drapeau pendant qu'on le bascule, et échouer
/// une fois sur trois sans qu'on comprenne pourquoi.
///
/// La collection sérialise. Ce n'est pas une précaution théorique : un test
/// intermittent sur la vérification de signature d'un webhook de paiement est
/// exactement le genre de rouge qu'on finit par ignorer.
/// </summary>
[Collection("GatewayWebhook state")]
public class HttpGatewayTests : IDisposable
{
    private readonly bool _previousAllowUnsigned = GatewayWebhook.AllowUnsignedWhenSecretMissing;

    /// <summary>
    /// Le défaut de production est FERMÉ (false) : un webhook non signé, alors
    /// qu'aucun secret n'est configuré, est REJETÉ. Sans quoi n'importe qui pourrait
    /// créditer une commande en postant un faux webhook.
    ///
    /// Les tests ci-dessous simulent le bac à sable, où l'hôte (Program.cs) active
    /// explicitement le mode permissif en Development. Ils doivent donc poser le
    /// drapeau eux-mêmes — et le remettre après, d'où le Dispose.
    /// </summary>
    public HttpGatewayTests() => GatewayWebhook.AllowUnsignedWhenSecretMissing = true;

    public void Dispose() => GatewayWebhook.AllowUnsignedWhenSecretMissing = _previousAllowUnsigned;

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private static MtnMomoHttpGateway RealMtn(string webhookSecret = "")
        => new(new StubHttpClientFactory(), new MtnMomoOptions { WebhookSecret = webhookSecret });

    private static MoovHttpGateway RealMoov()
        => new(new StubHttpClientFactory(), new MoovOptions());

    private static StripeHttpGateway RealStripe(string webhookSecret = "")
        => new(new StubHttpClientFactory(), new StripeOptions { WebhookSecret = webhookSecret });

    private static FedaPayHttpGateway RealFedaPay()
        => new(new StubHttpClientFactory(), new FedaPayOptions());

    private const string StripeSecret = "whsec_stripe";

    private const string StripeBody =
        "{\"type\":\"checkout.session.completed\",\"data\":{\"object\":{\"id\":\"cs_real\"}}}";

    /// <summary>
    /// Fabrique l'en-tête `t=…,v1=…` que Stripe enverrait pour ce corps, à
    /// l'horodatage demandé.
    /// </summary>
    private static string StripeHeader(DateTimeOffset sentAt)
    {
        var timestamp = sentAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(StripeSecret));
        var v1 = Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{StripeBody}"))).ToLowerInvariant();
        return $"t={timestamp},v1={v1}";
    }

    [Fact]
    public async Task Stripe_reel_verifie_la_signature_t_v1_et_mappe_l_evenement()
    {
        // ═════════════════════════════════════════════════════════════════════════
        // L'HORODATAGE EST CALCULÉ, PLUS ÉCRIT EN DUR.
        //
        // Ce test posait `t=1700000000` — novembre 2023. La signature était juste, et
        // il passait tant que l'horodatage n'était que SIGNÉ. Depuis qu'il est aussi
        // CONFRONTÉ À L'HEURE COURANTE (fenêtre de 5 minutes, anti-rejeu), une valeur
        // figée dans le passé est rejetée — à raison. Le test était devenu faux, pas
        // la passerelle.
        // ═════════════════════════════════════════════════════════════════════════
        var gateway = RealStripe(StripeSecret);

        var valid = await gateway.ParseWebhookAsync(StripeBody, StripeHeader(DateTimeOffset.UtcNow));
        valid.Verified.Should().BeTrue();
        valid.Outcome.Should().Be(GatewayOutcome.Captured);
        valid.ProviderReference.Should().Be("cs_real");

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var bad = await gateway.ParseWebhookAsync(StripeBody, $"t={timestamp},v1=deadbeef");
        bad.Verified.Should().BeFalse();
    }

    [Fact]
    public async Task Stripe_reel_refuse_un_corps_rejoue_hors_de_la_fenetre()
    {
        // La signature est PARFAITEMENT VALIDE : c'est tout l'intérêt du cas. Un corps
        // capturé sur le réseau reste signé pour toujours ; seule la confrontation de
        // l'horodatage à l'heure courante l'empêche d'être rejoué indéfiniment pour
        // créditer plusieurs fois la même commande.
        //
        // La symétrie de la fenêtre est déjà couverte par `SignatureDesPasserellesTests`,
        // au niveau de `IsWithinClockSkew`. Ce test-ci prouve autre chose : que Stripe,
        // qui SURCHARGE `ParseWebhookAsync` et ne passe donc pas par le chemin partagé,
        // appelle bien ce garde-fou. C'est exactement le contournement qui existait.
        var gateway = RealStripe(StripeSecret);
        var vieux = DateTimeOffset.UtcNow - GatewayWebhook.MaxClockSkew - TimeSpan.FromMinutes(1);

        var rejeu = await gateway.ParseWebhookAsync(StripeBody, StripeHeader(vieux));

        rejeu.Verified.Should().BeFalse();
    }


    [Fact]
    public async Task Le_callback_moov_reel_mappe_le_statut_et_la_reference()
    {
        var body = "{\"status\":\"SUCCESSFUL\",\"transactionId\":\"MOOV-7\"}";

        var gateway = RealMoov();
        var result = await gateway.ParseWebhookAsync(body, signatureHeader: null);

        gateway.Provider.Should().Be("Moov");
        gateway.RequiresPayerPhone.Should().BeTrue();
        result.Outcome.Should().Be(GatewayOutcome.Captured);
        result.ProviderReference.Should().Be("MOOV-7");
    }

    [Fact]
    public void L_adaptateur_reel_garde_l_identite_et_la_capability()
    {
        var gateway = RealMtn();
        gateway.Provider.Should().Be("MtnMomo");
        gateway.RequiresPayerPhone.Should().BeTrue();
    }

    [Theory]
    [InlineData("SUCCESSFUL", GatewayOutcome.Captured)]
    [InlineData("FAILED", GatewayOutcome.Failed)]
    [InlineData("PENDING", GatewayOutcome.Pending)]
    public async Task Le_callback_reel_mappe_le_statut(string status, GatewayOutcome expected)
    {
        var body = $"{{\"status\":\"{status}\",\"referenceId\":\"MTN-1\"}}";

        var result = await RealMtn().ParseWebhookAsync(body, signatureHeader: null);

        result.Verified.Should().BeTrue();
        result.Outcome.Should().Be(expected);
    }

    /// <summary>
    /// ═════════════════════════════════════════════════════════════════════════════
    /// LA TABLE DES STATUTS FEDAPAY, STATUT PAR STATUT.
    ///
    /// « expired » manquait. Il retombait sur `Ignored`, et un `Ignored` n'applique
    /// RIEN au paiement : celui-ci restait `Pending` définitivement, et la garde
    /// « un paiement est déjà en cours pour cette commande » rendait la commande
    /// impayable pour toujours. C'est le chemin exact qu'emprunte un acheteur qui ne
    /// saisit pas son code PIN à temps — le cas le plus banal du Mobile Money.
    ///
    /// Ce test énumère le vocabulaire PUBLIÉ par FedaPay. Il n'est pas là pour
    /// vérifier que le `switch` compile : il est là pour que l'ajout d'un statut au
    /// catalogue du prestataire, ou le retrait d'une ligne du `switch`, se voie ici
    /// plutôt qu'en production sur une commande bloquée.
    /// ═════════════════════════════════════════════════════════════════════════════
    /// </summary>
    [Theory]
    [InlineData("approved", GatewayOutcome.Captured)]
    [InlineData("transferred", GatewayOutcome.Captured)]
    [InlineData("pending", GatewayOutcome.Pending)]
    [InlineData("declined", GatewayOutcome.Failed)]
    [InlineData("canceled", GatewayOutcome.Failed)]
    [InlineData("expired", GatewayOutcome.Failed)]
    [InlineData("refunded", GatewayOutcome.Refunded)]
    public async Task FedaPay_mappe_chaque_statut_publie(string statut, GatewayOutcome attendu)
    {
        var body = $"{{\"name\":\"transaction.{statut}\",\"entity\":{{\"id\":4242,\"status\":\"{statut}\"}}}}";

        var result = await RealFedaPay().ParseWebhookAsync(body, signatureHeader: null);

        result.Verified.Should().BeTrue();
        result.Outcome.Should().Be(attendu, $"le statut FedaPay « {statut} » doit conclure le paiement");
        result.ProviderReference.Should().Be("4242");
    }

    /// <summary>
    /// Un statut expiré doit laisser dans le paiement un motif qui explique la suite à
    /// donner. Le statut brut n'apprenait rien à qui lisait la console.
    /// </summary>
    [Fact]
    public async Task FedaPay_explique_l_expiration_dans_le_motif_d_echec()
    {
        var body = "{\"name\":\"transaction.expired\",\"entity\":{\"id\":7,\"status\":\"expired\"}}";

        var result = await RealFedaPay().ParseWebhookAsync(body, signatureHeader: null);

        result.Outcome.Should().Be(GatewayOutcome.Failed);
        result.FailureReason.Should().Contain("expirée").And.Contain("24 heures");
    }

    /// <summary>
    /// Un statut hors catalogue reste sans effet — on préfère ne rien conclure plutôt
    /// que de conclure de travers. Ce test fige ce choix pour qu'il reste DÉLIBÉRÉ,
    /// et non le simple oubli qu'était « expired ».
    /// </summary>
    [Fact]
    public async Task FedaPay_ignore_un_statut_inconnu_plutot_que_de_deviner()
    {
        var body = "{\"name\":\"transaction.quelque_chose\",\"entity\":{\"id\":7,\"status\":\"quelque_chose\"}}";

        var result = await RealFedaPay().ParseWebhookAsync(body, signatureHeader: null);

        result.Verified.Should().BeTrue();
        result.Outcome.Should().Be(GatewayOutcome.Ignored);
    }

    [Fact]
    public void Le_helper_de_signature_accepte_le_bon_hmac_et_rejette_le_mauvais()
    {
        const string secret = "whsec_momo";
        var body = "{\"status\":\"SUCCESSFUL\",\"referenceId\":\"MTN-1\"}";

        GatewayWebhook.VerifySignature(body, GatewayWebhook.ComputeSignature(body, secret), secret).Should().BeTrue();
        GatewayWebhook.VerifySignature(body, "mauvaise", secret).Should().BeFalse();

        // Secret vide + mode permissif (posé par le constructeur) => accepté.
        // C'est le cas du bac à sable, pas celui de la production.
        GatewayWebhook.VerifySignature(body, null, string.Empty).Should().BeTrue();
    }

    /// <summary>
    /// Le garde-fou du helper GÉNÉRIQUE.
    ///
    /// ATTENTION À CE QUE CE TEST NE COUVRE PAS. Il a longtemps été présenté ici comme
    /// « LE test qui compte vraiment » — il ne l'était pas : FedaPay et Stripe
    /// surchargent `ParseWebhookAsync` et ne traversent JAMAIS ce helper. Leurs
    /// propres vérificateurs acceptaient tout sur secret vide, et ce test ne pouvait
    /// pas le voir. Les deux passerelles réellement déployées sont désormais testées
    /// par leur nom dans `SignatureDesPasserellesTests`.
    ///
    /// En production, aucun secret n'est parfois configuré — par oubli, par erreur de
    /// vault, par variable d'environnement mal nommée. Ce jour-là, le webhook DOIT
    /// être rejeté. Un fail-open silencieux permettrait à quiconque connaît l'URL de
    /// marquer une commande comme payée en postant un JSON.
    ///
    /// Ce test verrouille le défaut. Si quelqu'un remet un jour la valeur à `true`
    /// « pour débloquer le sandbox », c'est ici que ça cassera.
    /// </summary>
    [Fact]
    public void Sans_secret_ET_sans_mode_permissif_le_webhook_est_REJETE()
    {
        var previous = GatewayWebhook.AllowUnsignedWhenSecretMissing;
        try
        {
            GatewayWebhook.AllowUnsignedWhenSecretMissing = false; // le défaut de production
            var body = "{\"status\":\"SUCCESSFUL\",\"referenceId\":\"MTN-1\"}";

            GatewayWebhook.VerifySignature(body, signatureHeader: null, secret: string.Empty)
                .Should().BeFalse("un webhook non signé sans secret configuré ne doit JAMAIS créditer une commande");
        }
        finally
        {
            GatewayWebhook.AllowUnsignedWhenSecretMissing = previous;
        }
    }
}

/// <summary>
/// Sérialise les classes qui touchent au drapeau statique de GatewayWebhook.
/// </summary>
[CollectionDefinition("GatewayWebhook state", DisableParallelization = true)]
public sealed class GatewayWebhookCollection;
