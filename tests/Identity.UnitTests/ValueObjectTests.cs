using FluentAssertions;
using Marketplace.Modules.Identity.Domain.Roles;
using Marketplace.Modules.Identity.Domain.Users;
using Xunit;

namespace Identity.UnitTests;

public class ValueObjectTests
{
    [Theory]
    [InlineData("Hector.Adjakpa@Gmail.com", "hector.adjakpa@gmail.com")]
    [InlineData("  USER@example.org ", "user@example.org")]
    public void Email_est_normalise(string input, string expected)
    {
        var result = Email.Create(input);

        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().Be(expected);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@b")]
    public void Email_invalide_est_rejete(string input)
    {
        Email.Create(input).IsFailure.Should().BeTrue();
    }

    [Theory]
    [InlineData("+229 01 96 00 00 00")]
    [InlineData("0196000000")]
    public void PhoneNumber_garde_les_chiffres(string input)
    {
        var result = PhoneNumber.Create(input);
        result.IsSuccess.Should().BeTrue();
        result.Value.Value.Should().MatchRegex(@"^\+?[0-9]{8,15}$");
    }

    [Fact]
    public void Permission_valide_le_format()
    {
        Permission.Create("catalog.write").IsSuccess.Should().BeTrue();
        Permission.Create("invalide").IsFailure.Should().BeTrue();
    }
}
