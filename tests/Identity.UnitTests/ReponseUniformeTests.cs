using FluentAssertions;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Application.Users.Commands.ConfirmEmail;
using Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;
using Marketplace.Modules.Identity.Application.Users.Commands.VerifyEmailCode;
using Marketplace.Modules.Identity.Domain.Users;
using Xunit;

namespace Identity.UnitTests;

/// <summary>
/// ═══════════════════════════════════════════════════════════════════════════════
/// TESTS D'UNIFORMITÉ DE RÉPONSE. NE PAS « CORRIGER » EN RENDANT UNE ERREUR PLUS
/// PRÉCISE.
///
/// Trois handlers ont été réécrits pour qu'une adresse INCONNUE et une adresse
/// CONNUE avec un mauvais jeton produisent la MÊME erreur. Chacun de ces trois
/// écarts était un oracle d'énumération de comptes exploitable sans
/// authentification, et le pire — `ResetPasswordCommandHandler` — répondait 400 sur
/// une adresse libre et 409 sur une adresse prise, en une requête, sans envoyer
/// d'e-mail ni rien modifier. Il fonctionnait aussi sur la surface admin.
///
/// AVANT CES TESTS, RIEN NE FIXAIT CES GARANTIES. Elles ne tenaient que par des
/// encadrés de commentaires, et se seraient défaites au premier « on devrait dire à
/// l'utilisateur que son lien a expiré plutôt qu'un message vague » — une demande
/// parfaitement raisonnable, formulée par quelqu'un qui n'aurait pas su ce qu'elle
/// rouvre.
///
/// CE QUE CES TESTS VÉRIFIENT VRAIMENT : que les branches d'échec sont
/// INDISCERNABLES. Pas seulement le statut HTTP — `ApiResults` publie aussi le CODE
/// d'erreur dans le champ `title` du ProblemDetails, donc deux codes distincts sous
/// un même 400 laissent l'oracle intact. On compare donc le couple (Code, Type).
///
/// Si l'un d'eux échoue, la question n'est pas « comment faire passer le test »,
/// c'est « qui a rendu une erreur plus bavarde, et le savait-il ».
/// ═══════════════════════════════════════════════════════════════════════════════
/// </summary>
public class ReponseUniformeTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    // ── Fabriques de comptes ────────────────────────────────────────────────────

    private const string Adresse = "cliente@example.com";

    /// <summary>Compte avec un code de vérification VALIDE dont on connaît le hash.</summary>
    private static User AvecVerificationEnCours(string codeHash = "hash-du-bon-code")
        => User.Register(
            "Awa", "Kponou",
            Email.Create(Adresse).Value,
            PhoneNumber.Create("+2290190000000").Value,
            "bcrypt:motdepasse",
            codeHash,
            Now.AddHours(2)).Value;

    /// <summary>Compte dont le code de vérification est EXPIRÉ.</summary>
    private static User AvecVerificationExpiree()
        => User.Register(
            "Awa", "Kponou",
            Email.Create(Adresse).Value,
            PhoneNumber.Create("+2290190000000").Value,
            "bcrypt:motdepasse",
            "hash-du-bon-code",
            Now.AddHours(-2)).Value;

    // ══════════════════════════════════════════════════════════════════════════
    // 1. Confirmation d'e-mail
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ConfirmEmail_repond_la_MEME_erreur_quelle_que_soit_la_cause()
    {
        var refus = new List<Error>();

        // Adresse inconnue : rien en base.
        refus.Add(await ConfirmerAsync(null, "n-importe-quoi"));

        // Adresse connue, mauvais jeton.
        refus.Add(await ConfirmerAsync(AvecVerificationEnCours(), "mauvais-jeton"));

        // Adresse connue, jeton expiré — et le BON jeton, pour que seule l'expiration
        // fasse échouer.
        refus.Add(await ConfirmerAsync(AvecVerificationExpiree(), "bon-code"));

        refus.Should().OnlyContain(e => e.Code == "identity.email.confirmation_invalid");
        refus.Select(e => (e.Code, e.Type)).Distinct().Should().HaveCount(1,
            "adresse inconnue, jeton faux et jeton expiré doivent être INDISCERNABLES : "
            + "le code d'erreur est publié dans le `title` du ProblemDetails, donc un code "
            + "distinct suffit à rétablir l'énumération même sous un statut identique.");
    }

    [Fact]
    public async Task ConfirmEmail_hache_le_jeton_MEME_sur_une_adresse_inconnue()
    {
        // Le hachage doit précéder le test de nullité, sinon le refus sur adresse
        // inconnue revient plus vite et l'uniformité se mesure au chronomètre.
        var jetons = new CompteurDeJetons();
        var handler = new ConfirmEmailCommandHandler(
            new DepotFactice(null), jetons, new UnitOfWorkFactice());

        await handler.Handle(new ConfirmEmailCommand(Adresse, "peu-importe"), default);

        jetons.NombreDeHachages.Should().Be(1,
            "sans ce hachage, la branche « adresse inconnue » sort plus tôt que les autres "
            + "et l'écart est mesurable.");
    }

    private static async Task<Error> ConfirmerAsync(User? enBase, string jeton)
    {
        var handler = new ConfirmEmailCommandHandler(
            new DepotFactice(enBase), new CompteurDeJetons(), new UnitOfWorkFactice());

        var resultat = await handler.Handle(new ConfirmEmailCommand(Adresse, jeton), default);
        resultat.IsFailure.Should().BeTrue();
        return resultat.Error;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. Vérification par code (auto-inscription vendeur)
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task VerifyEmailCode_repond_la_MEME_erreur_quelle_que_soit_la_cause()
    {
        var refus = new List<Error>();

        refus.Add(await VerifierAsync(null, "000000"));
        refus.Add(await VerifierAsync(AvecVerificationEnCours(), "mauvais-code"));
        refus.Add(await VerifierAsync(AvecVerificationExpiree(), "bon-code"));

        refus.Should().OnlyContain(e => e.Code == "identity.email.verification_invalid");
        refus.Select(e => (e.Code, e.Type)).Distinct().Should().HaveCount(1,
            "cette commande est atteinte par une route ANONYME, `/seller/auth/verify` : "
            + "distinguer les causes y révélerait quelles adresses ont un compte.");
    }

    private static async Task<Error> VerifierAsync(User? enBase, string code)
    {
        var handler = new VerifyEmailCodeCommandHandler(
            new DepotFactice(enBase), new CompteurDeJetons(), new UnitOfWorkFactice());

        var resultat = await handler.Handle(new VerifyEmailCodeCommand(Adresse, code), default);
        resultat.IsFailure.Should().BeTrue();
        return resultat.Error;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. Réinitialisation de mot de passe — l'oracle le plus grave des trois
    // ══════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResetPassword_repond_la_MEME_erreur_quelle_que_soit_la_cause()
    {
        var refus = new List<Error>();

        // Adresse inconnue.
        refus.Add(await ReinitialiserAsync(null));

        // Adresse connue, AUCUNE réinitialisation demandée. C'était le cas qui trahissait :
        // il répondait 409 `identity.user.no_reset_pending` là où l'adresse inconnue
        // répondait 400 `identity.user.reset_invalid`. Une requête, aucun e-mail, aucune
        // écriture — et l'existence du compte.
        refus.Add(await ReinitialiserAsync(AvecVerificationEnCours()));

        // Adresse connue, réinitialisation EXPIRÉE.
        var expire = AvecVerificationEnCours();
        expire.BeginPasswordReset("hash-du-bon-jeton", Now.AddHours(-1));
        refus.Add(await ReinitialiserAsync(expire));

        // Adresse connue, réinitialisation en cours, MAUVAIS jeton.
        var enCours = AvecVerificationEnCours();
        enCours.BeginPasswordReset("hash-du-bon-jeton", Now.AddHours(1));
        refus.Add(await ReinitialiserAsync(enCours));

        refus.Should().OnlyContain(e => e.Code == "identity.user.reset_invalid");
        refus.Select(e => (e.Code, e.Type)).Distinct().Should().HaveCount(1,
            "les quatre refus doivent être indiscernables. Le domaine garde ses trois "
            + "erreurs distinctes — elles sont utiles en journal — mais le handler les "
            + "aplatit, parce que c'est lui qui parle à une route anonyme.");
    }

    [Fact]
    public async Task ResetPassword_hache_le_mot_de_passe_MEME_sur_une_adresse_inconnue()
    {
        // C'est ici que le chronomètre comptait le plus : BCrypt à facteur 12 coûte
        // ~300 ms. Le laisser dans la seule branche « compte trouvé » rendait le refus
        // sur adresse inconnue un ordre de grandeur plus rapide, et défaisait au
        // chronomètre l'uniformité qu'on vient d'établir dans le corps.
        var hacheur = new CompteurDeHachages();
        var handler = new ResetPasswordCommandHandler(
            new DepotFactice(null), hacheur, new CompteurDeJetons(), new UnitOfWorkFactice());

        await handler.Handle(new ResetPasswordCommand(Adresse, "jeton", "NouveauMotDePasse1!"), default);

        hacheur.NombreDeHachages.Should().Be(1,
            "le coût du BCrypt doit être payé sur les DEUX branches, y compris quand on "
            + "sait déjà qu'on va refuser.");
    }

    private static async Task<Error> ReinitialiserAsync(User? enBase)
    {
        var handler = new ResetPasswordCommandHandler(
            new DepotFactice(enBase), new CompteurDeHachages(), new CompteurDeJetons(), new UnitOfWorkFactice());

        var resultat = await handler.Handle(
            new ResetPasswordCommand(Adresse, "jeton-fourni", "NouveauMotDePasse1!"), default);

        resultat.IsFailure.Should().BeTrue();
        return resultat.Error;
    }

    // ══════════════════════════════════════════════════════════════════════════
    // Doublures — écrites à la main plutôt que moquées : trois membres utiles sur
    // dix, et le reste doit LEVER si on l'appelle. Un mock complaisant renverrait
    // null en silence et le test passerait pour la mauvaise raison.
    // ══════════════════════════════════════════════════════════════════════════

    private sealed class DepotFactice : IUserRepository
    {
        private readonly User? _user;

        public DepotFactice(User? user) => _user = user;

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
            => Task.FromResult(_user);

        public Task AddAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Remove(User user) => throw new NotSupportedException();
        public Task<User?> GetByIdAsync(UserId id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> GetByRefreshTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> PhoneExistsAsync(string phoneNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<User>> ListAllAsync(int take = 500, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<(IReadOnlyList<User> Items, int Total, IReadOnlyDictionary<string, int> StatusCounts)> ListPagedAsync(
            int page, int pageSize, string? search, UserStatus? status, string? sort, bool desc, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<(DateTime Day, int Count)>> SignupsByDayAsync(
            DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Hachage de jeton DÉTERMINISTE, et qui compte ses appels.
    ///
    /// « hash-du-JETON » : c'est ce qui permet aux fabriques ci-dessus de créer un
    /// compte dont on connaît le hash attendu, sans dépendre d'une vraie fonction de
    /// hachage.
    /// </summary>
    private sealed class CompteurDeJetons : ISecureTokenGenerator
    {
        public int NombreDeHachages { get; private set; }

        public string Hash(string rawToken)
        {
            NombreDeHachages++;
            return $"hash-du-{rawToken}";
        }

        public (string Raw, string Hash) Generate() => ("jeton", "hash-du-jeton");

        public (string Code, string Hash) GenerateNumericCode(int digits = 6) => ("123456", "hash-du-123456");
    }

    private sealed class CompteurDeHachages : IPasswordHasher
    {
        public int NombreDeHachages { get; private set; }

        public string Hash(string password)
        {
            NombreDeHachages++;
            return $"bcrypt:{password}";
        }

        public bool Verify(string passwordHash, string password) => passwordHash == $"bcrypt:{password}";
    }

    private sealed class UnitOfWorkFactice : IIdentityUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}
