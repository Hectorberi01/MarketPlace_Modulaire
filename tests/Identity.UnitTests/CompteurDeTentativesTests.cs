using FluentAssertions;
using Marketplace.Modules.Identity.Domain.Users;
using Xunit;

namespace Identity.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// UN CODE À SIX CHIFFRES N'EST UN SECRET QUE S'IL EST ESSAYABLE UN NOMBRE FINI DE
/// FOIS. NE PAS RELEVER LE PLAFOND SANS RELIRE CECI.
///
/// Les codes de vérification et de réinitialisation vivent dans un espace de 10⁶. Un
/// essai raté ne décrémentait rien, ne verrouillait rien et ne purgeait pas le code :
/// on pouvait les essayer tous. Le seul frein était le limiteur `auth` — trente
/// requêtes par minute et PAR ADRESSE IP — dont le propre commentaire dit qu'il ne
/// protège pas un compte précis d'une attaque lente et distribuée.
///
/// La conjonction était ce qui rendait la chose atteignable : espace étroit, durée
/// longue (une heure pour la réinitialisation, QUARANTE-HUIT pour la vérification), et
/// essais illimités. Retirer n'importe lequel des trois suffit ; c'est le troisième
/// qu'on retire ici, parce que c'est le seul qui ne coûte rien à l'utilisateur légitime.
///
/// LE CODE EST PURGÉ, LE COMPTE N'EST PAS VERROUILLÉ. Verrouiller offrirait un déni de
/// service : n'importe qui pourrait bloquer une victime en épuisant son quota. Purger
/// ne lui coûte qu'un nouveau code à demander.
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class CompteurDeTentativesTests
{
    private const string BonCode = "hash-du-bon-code";
    private const string MauvaisCode = "hash-du-mauvais-code";

    private static User NouveauCompte()
        => User.Register(
            "Awa", "Kponou",
            Email.Create("cliente@example.com").Value,
            PhoneNumber.Create("+2290190000000").Value,
            "bcrypt:motdepasse",
            BonCode,
            DateTime.UtcNow.AddHours(48)).Value;

    // ══════════════════════════════════════════════════════════════════════════
    // Vérification d'e-mail
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Cinq_essais_rates_tuent_le_code_de_verification()
    {
        var user = NouveauCompte();

        for (var i = 0; i < 5; i++)
        {
            user.ConfirmEmail(MauvaisCode, DateTime.UtcNow).IsFailure.Should().BeTrue();
        }

        user.EmailVerificationTokenHash.Should().BeNull("le code est purgé au cinquième essai");
        user.EmailVerificationExpiresOnUtc.Should().BeNull();
    }

    [Fact]
    public void Le_BON_code_ne_marche_plus_apres_le_plafond()
    {
        var user = NouveauCompte();

        for (var i = 0; i < 5; i++)
        {
            user.ConfirmEmail(MauvaisCode, DateTime.UtcNow);
        }

        // C'est ce test qui dit que le plafond PLAFONNE. Sans la purge, un attaquant
        // arrêté à cinq essais reprendrait simplement au sixième.
        user.ConfirmEmail(BonCode, DateTime.UtcNow).IsFailure.Should().BeTrue();
        user.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public void Quatre_essais_rates_laissent_le_bon_code_valide()
    {
        var user = NouveauCompte();

        for (var i = 0; i < 4; i++)
        {
            user.ConfirmEmail(MauvaisCode, DateTime.UtcNow);
        }

        user.ConfirmEmail(BonCode, DateTime.UtcNow).IsSuccess.Should().BeTrue();
        user.EmailVerified.Should().BeTrue();
    }

    [Fact]
    public void Un_code_expire_n_incremente_PAS_le_compteur()
    {
        var user = User.Register(
            "Awa", "Kponou",
            Email.Create("cliente@example.com").Value,
            PhoneNumber.Create("+2290190000000").Value,
            "bcrypt:motdepasse",
            BonCode,
            DateTime.UtcNow.AddHours(-1)).Value;

        user.ConfirmEmail(MauvaisCode, DateTime.UtcNow).IsFailure.Should().BeTrue();

        // On ne compte que les tentatives de DEVINER un code qui existe. Compter les
        // autres brûlerait le quota d'un utilisateur simplement lent, et laisserait un
        // attaquant l'épuiser à distance sans jamais rien tenter.
        user.EmailVerificationAttempts.Should().Be(0);
    }

    [Fact]
    public void Un_nouveau_code_remet_le_compteur_a_zero()
    {
        var user = NouveauCompte();
        user.ConfirmEmail(MauvaisCode, DateTime.UtcNow);
        user.ConfirmEmail(MauvaisCode, DateTime.UtcNow);
        user.EmailVerificationAttempts.Should().Be(2);

        user.BeginEmailVerification("hash-du-nouveau-code", DateTime.UtcNow.AddHours(48));

        // Sans cette remise à zéro, un utilisateur ayant tâtonné démarrerait le code
        // suivant déjà à moitié consommé.
        user.EmailVerificationAttempts.Should().Be(0);
        user.ConfirmEmail("hash-du-nouveau-code", DateTime.UtcNow).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Le_compteur_du_code_vendeur_partage_le_meme_plafond()
    {
        // `ConsumeEmailVerificationCode` est l'autre porte — celle de l'auto-inscription
        // vendeur, stricte même sur un compte déjà vérifié. Elle doit compter aussi,
        // sinon le plafond se contourne en changeant de route.
        var user = NouveauCompte();

        for (var i = 0; i < 5; i++)
        {
            user.ConsumeEmailVerificationCode(MauvaisCode, DateTime.UtcNow);
        }

        user.EmailVerificationTokenHash.Should().BeNull();
        user.ConsumeEmailVerificationCode(BonCode, DateTime.UtcNow).IsFailure.Should().BeTrue();
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Réinitialisation de mot de passe — la plus grave des deux
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Cinq_essais_rates_tuent_le_code_de_reinitialisation()
    {
        var user = NouveauCompte();
        user.BeginPasswordReset("hash-du-bon-jeton", DateTime.UtcNow.AddHours(1));

        for (var i = 0; i < 5; i++)
        {
            user.ResetPassword("hash-du-mauvais-jeton", "bcrypt:nouveau", DateTime.UtcNow)
                .IsFailure.Should().BeTrue();
        }

        user.PasswordResetTokenHash.Should().BeNull();

        // Ce chemin mène à une PRISE DE CONTRÔLE COMPLÈTE du compte : il révoque toutes
        // les sessions et pose un nouveau mot de passe. C'est celui qui devait être borné
        // en premier.
        user.ResetPassword("hash-du-bon-jeton", "bcrypt:nouveau", DateTime.UtcNow)
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public void Une_nouvelle_demande_de_reinitialisation_remet_le_compteur_a_zero()
    {
        var user = NouveauCompte();
        user.BeginPasswordReset("hash-du-bon-jeton", DateTime.UtcNow.AddHours(1));
        user.ResetPassword("hash-du-mauvais-jeton", "bcrypt:x", DateTime.UtcNow);
        user.PasswordResetAttempts.Should().Be(1);

        user.BeginPasswordReset("hash-du-second-jeton", DateTime.UtcNow.AddHours(1));

        user.PasswordResetAttempts.Should().Be(0);
        user.ResetPassword("hash-du-second-jeton", "bcrypt:x", DateTime.UtcNow)
            .IsSuccess.Should().BeTrue();
    }
}
