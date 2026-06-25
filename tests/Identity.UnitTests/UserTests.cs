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
    public void ConfirmEmail_active_le_compte_avec_le_bon_jeton()
    {
        var user = NewUser("verif-hash");

        var result = user.ConfirmEmail("verif-hash", DateTime.UtcNow);

        result.IsSuccess.Should().BeTrue();
        user.EmailVerified.Should().BeTrue();
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
