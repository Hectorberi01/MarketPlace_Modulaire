using FluentAssertions;
using Marketplace.BuildingBlocks.Domain.Geography;
using Xunit;

namespace BuildingBlocks.UnitTests;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════════
/// LE RÉFÉRENTIEL DOIT S'INITIALISER, QUEL QUE SOIT LE PREMIER MEMBRE TOUCHÉ.
///
/// Ces tests existent à cause d'un bogue qu'aucune relecture n'avait attrapé et
/// qu'aucune compilation ne pouvait signaler : <c>Reference</c> était initialisée
/// AVANT le dictionnaire des départements qu'elle interroge. Les initialiseurs
/// statiques s'exécutant dans l'ordre de déclaration, le constructeur de type levait
/// une <c>NullReferenceException</c>.
///
/// La conséquence dépassait de loin le référentiel servi : une
/// <c>TypeInitializationException</c> rend le type ENTIÈREMENT inutilisable. Résoudre
/// une commune, valider une adresse, normaliser un téléphone — tout échouait, y compris
/// dans des modules qui n'ont jamais entendu parler de <c>Reference</c>.
///
/// D'où le premier test : toucher <c>Reference</c> AVANT tout autre membre. C'est ce
/// que ferait un processus dont la toute première requête serait
/// <c>GET /geo/communes</c> — et c'est précisément le scénario qui plantait.
/// ═════════════════════════════════════════════════════════════════════════════════
/// </summary>
public class BeninGeographyTests
{
    [Fact]
    public void Le_referentiel_sinitialise_meme_si_Reference_est_le_premier_membre_touche()
    {
        var reference = BeninGeography.Reference;

        reference.Should().NotBeNull();
        reference.Communes.Should().HaveCount(77);
        reference.Departments.Should().HaveCount(12);
        reference.CountryCode.Should().Be("BJ");

        // Le champ qui plantait : il vient du dictionnaire des départements.
        reference.Communes.Should().OnlyContain(c => !string.IsNullOrWhiteSpace(c.DepartmentName));
    }

    [Fact]
    public void Chaque_commune_est_rattachee_a_un_departement_existant()
    {
        var departmentCodes = BeninGeography.Departments.Select(d => d.Code).ToHashSet();

        BeninGeography.Communes.Should().OnlyContain(c => departmentCodes.Contains(c.DepartmentCode));
    }

    [Fact]
    public void Les_codes_de_commune_sont_uniques()
    {
        // Un doublon ferait exploser la construction du dictionnaire — mais seulement
        // au premier accès, donc potentiellement en production.
        BeninGeography.Communes.Select(c => c.Code).Should().OnlyHaveUniqueItems();
    }

    [Theory]
    [InlineData("cotonou", "Cotonou")]
    [InlineData("abomey-calavi", "Abomey-Calavi")]
    [InlineData("seme-podji", "Sèmè-Podji")]
    public void Un_code_se_resout_en_libelle_accentue(string code, string expected)
        => BeninGeography.CommuneName(code).Should().Be(expected);

    [Theory]
    [InlineData("Cotonou", "cotonou")]
    [InlineData("COTONOU", "cotonou")]
    [InlineData("Abomey Calavi", "abomey-calavi")]
    [InlineData("Calavi", "abomey-calavi")]
    [InlineData("Sèmè-Kpodji", "seme-podji")]
    public void Un_libelle_saisi_librement_se_resout_en_code(string input, string expected)
        => BeninGeography.ResolveCommuneCode(input).Should().Be(expected);

    [Theory]
    [InlineData("Lomé")]
    [InlineData("Paris")]
    [InlineData("")]
    [InlineData(null)]
    public void Ce_qui_nest_pas_une_commune_beninoise_est_refuse(string? input)
        => BeninGeography.ResolveCommuneCode(input).Should().BeNull();

    [Theory]
    [InlineData("0197123456", "+2290197123456")]
    [InlineData("+229 01 97 12 34 56", "+2290197123456")]
    [InlineData("00229 0197123456", "+2290197123456")]
    [InlineData("229-01-97-12-34-56", "+2290197123456")]
    public void Un_numero_beninois_est_normalise(string input, string expected)
        => BeninGeography.NormalizePhone(input).Should().Be(expected);

    [Theory]
    [InlineData("97123456")]          // ancien format à 8 chiffres, d'avant 2024
    [InlineData("97 12 34 56 / 96 18 68 06")]
    [InlineData("appeler le voisin")]
    [InlineData("")]
    [InlineData(null)]
    public void Un_numero_inexploitable_est_refuse_plutot_que_repare(string? input)
        => BeninGeography.NormalizePhone(input).Should().BeNull();
}
