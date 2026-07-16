using FluentAssertions;
using Marketplace.Modules.Identity.Domain.Users;
using Xunit;

namespace Identity.UnitTests;

public class UserTests
{
    private static User NewUser(string tokenHash = "verif-hash")
    {
        var email = Email.Create("buyer@example.com").Value;
        var phone = PhoneNumber.Create("+2290190000000").Value;
        return User.Register("Hector", "Adjakpa", email, phone, "bcrypt-hash", tokenHash, DateTime.UtcNow.AddHours(48)).Value;
    }

    [Fact]
    public void Register_cree_un_compte_en_attente_de_verification()
    {
        var user = NewUser();

        user.Status.Should().Be(UserStatus.PendingVerification);
        user.EmailVerified.Should().BeFalse();
        user.MfaEnabled.Should().BeFalse();
    }

    [Fact]
    public void ConfirmEmail_verifie_l_email_mais_n_active_PAS_le_compte()
    {
        var user = NewUser("verif-hash");

        var result = user.ConfirmEmail("verif-hash", DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        user.EmailVerified.Should().BeTrue();

        // Le cœur de la règle : vérifier son e-mail prouve qu'on relève bien cette
        // boîte aux lettres. Cela ne dit rien de la légitimité du compte, et ne vaut
        // donc pas autorisation d'entrer. L'activation reste une décision
        // d'administrateur (Approve).
        user.Status.Should().Be(UserStatus.PendingVerification);
    }

    [Fact]
    public void Approve_active_le_compte_sans_pretendre_que_l_email_est_verifie()
    {
        var user = NewUser("verif-hash");

        var result = user.Approve();

        result.IsSuccess.Should().BeTrue();
        user.Status.Should().Be(UserStatus.Active);

        // Un administrateur atteste que le compte est légitime — il ne prouve pas
        // que l'adresse e-mail appartient à cette personne. Écrire EmailVerified =
        // true ici fabriquerait un fait qui n'a jamais eu lieu.
        user.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public void Approve_est_idempotent()
    {
        var user = NewUser("verif-hash");
        user.Approve();

        var result = user.Approve();

        result.IsSuccess.Should().BeTrue();
        user.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public void Reactivate_refuse_un_compte_qui_n_est_pas_suspendu()
    {
        var user = NewUser("verif-hash"); // PendingVerification

        var result = user.Reactivate();

        // On ne « réactive » pas ce qui n'a jamais été actif : un compte en attente
        // se VALIDE. La distinction n'est pas verbale — elle empêche la console de
        // proposer le mauvais bouton, et de fabriquer une activation par mégarde.
        result.IsFailure.Should().BeTrue();
        user.Status.Should().Be(UserStatus.PendingVerification);
    }

    [Fact]
    public void Suspend_puis_Reactivate_ramene_le_compte_a_actif()
    {
        var user = NewUser("verif-hash");
        user.Approve();
        user.Suspend();
        user.Status.Should().Be(UserStatus.Suspended);

        var result = user.Reactivate();

        result.IsSuccess.Should().BeTrue();
        user.Status.Should().Be(UserStatus.Active);
    }

    [Fact]
    public void ConfirmEmail_refuse_un_mauvais_jeton()
    {
        var user = NewUser("verif-hash");

        var result = user.ConfirmEmail("mauvais", DateTime.UtcNow);

        result.IsFailure.Should().BeTrue();
        user.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public void AssignRole_est_idempotent()
    {
        var user = NewUser();
        var roleId = Guid.NewGuid();

        user.AssignRole(roleId);
        user.AssignRole(roleId);

        user.RoleIds.Should().ContainSingle().Which.Should().Be(roleId);
    }

    [Fact]
    public void ChangePassword_revoque_les_refresh_tokens()
    {
        var user = NewUser();
        user.IssueRefreshToken("token-hash", DateTime.UtcNow.AddDays(30));

        user.ChangePassword("new-hash");

        user.FindActiveRefreshToken("token-hash", DateTime.UtcNow).Should().BeNull();
    }

    [Fact]
    public void Mfa_setup_puis_confirmation_active_la_double_authentification()
    {
        var user = NewUser();

        user.BeginMfaSetup("SECRET").IsSuccess.Should().BeTrue();
        user.MfaEnabled.Should().BeFalse();

        user.ConfirmMfaSetup().IsSuccess.Should().BeTrue();
        user.MfaEnabled.Should().BeTrue();
    }
}
