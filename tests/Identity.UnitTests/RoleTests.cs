using FluentAssertions;
using Marketplace.Modules.Identity.Domain.Roles;
using Xunit;

namespace Identity.UnitTests;

public class RoleTests
{
    [Fact]
    public void Create_normalise_et_dedoublonne_les_permissions()
    {
        var result = Role.Create("Seller", "Vendeur", isSystem: true,
            permissions: new[] { "Catalog.Write", "catalog.write", "offers.write" });

        result.IsSuccess.Should().BeTrue();
        result.Value.Permissions.Should().BeEquivalentTo(new[] { "catalog.write", "offers.write" });
    }

    [Fact]
    public void Create_rejette_une_permission_mal_formee()
    {
        var result = Role.Create("Bad", permissions: new[] { "not-a-permission" });

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public void SetPermissions_remplace_l_ensemble()
    {
        var role = Role.Create("Admin").Value;

        role.SetPermissions(new[] { "users.manage" }).IsSuccess.Should().BeTrue();
        role.Permissions.Should().ContainSingle().Which.Should().Be("users.manage");
    }
}
