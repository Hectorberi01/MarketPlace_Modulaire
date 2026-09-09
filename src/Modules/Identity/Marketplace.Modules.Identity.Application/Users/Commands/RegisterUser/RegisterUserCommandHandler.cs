using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Application.Observability;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Contracts.IntegrationEvents;
using Marketplace.Modules.Identity.Domain.Roles;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.RegisterUser;

/// <summary>
/// Valide l'unicité e-mail/téléphone, hash le mot de passe, génère un jeton de
/// vérification (seul le hash est stocké), crée le compte avec le rôle Buyer par
/// défaut, et publie l'event de vérification d'e-mail via l'outbox.
/// </summary>
internal sealed class RegisterUserCommandHandler : ICommandHandler<RegisterUserCommand, Guid>
{
    private const string DefaultRoleName = "Buyer";

    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IAuthTokenSettings _tokenSettings;
    private readonly IRegistrationPolicy _registrationPolicy;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly ISecurityMetrics _security;
    private readonly IMarketplaceBusinessMetrics _business;

    public RegisterUserCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        IAuthTokenSettings tokenSettings,
        IRegistrationPolicy registrationPolicy,
        IIntegrationEventPublisher publisher,
        IIdentityUnitOfWork unitOfWork,
        ISecurityMetrics security,
        IMarketplaceBusinessMetrics business)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _tokenSettings = tokenSettings;
        _registrationPolicy = registrationPolicy;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
        _security = security;
        _business = business;
    }

    public async Task<Result<Guid>> Handle(RegisterUserCommand command, CancellationToken cancellationToken)
    {
        var emailResult = Email.Create(command.Email);
        if (emailResult.IsFailure)
        {
            return Result.Failure<Guid>(emailResult.Error);
        }

        var phoneResult = PhoneNumber.Create(command.PhoneNumber);
        if (phoneResult.IsFailure)
        {
            return Result.Failure<Guid>(phoneResult.Error);
        }

        // ═════════════════════════════════════════════════════════════════════════
        // LE HACHAGE EST FAIT AVANT LES CONTRÔLES D'EXISTENCE, ET C'EST DÉLIBÉRÉ.
        //
        // Il venait après. Les appelants qui uniformisent leur réponse — les trois
        // routes d'inscription — avaient donc un corps constant et un TEMPS DE RÉPONSE
        // qui ne l'était pas : adresse libre, on payait ~300 ms de BCrypt (facteur 12) ;
        // adresse prise, on retournait en ~10 ms. Un ordre de grandeur, très au-dessus
        // de la gigue réseau, séparable en quelques échantillons. Le corps uniforme
        // était défait au chronomètre.
        //
        // On paie donc le hachage dans tous les cas, y compris quand on sait déjà qu'on
        // va refuser. C'est du temps CPU dépensé pour rien, et c'est exactement le but.
        //
        // COROLLAIRE OPÉRATIONNEL : une rafale d'inscriptions sur des adresses déjà
        // prises coûte maintenant du BCrypt. Le limiteur `auth` est ce qui borne ça —
        // 30 requêtes par minute et par IP.
        // ═════════════════════════════════════════════════════════════════════════
        var passwordHash = _passwordHasher.Hash(command.Password);

        if (await _userRepository.EmailExistsAsync(emailResult.Value.Value, cancellationToken))
        {
            return Error.Conflict("identity.user.email_taken", "Un compte existe déjà avec cet e-mail.");
        }

        if (await _userRepository.PhoneExistsAsync(phoneResult.Value.Value, cancellationToken))
        {
            return Error.Conflict("identity.user.phone_taken", "Un compte existe déjà avec ce numéro.");
        }

        // Vérification e-mail par CODE numérique (6 chiffres) envoyé par e-mail.
        var (rawToken, tokenHash) = _tokenGenerator.GenerateNumericCode();
        var expiresOnUtc = DateTime.UtcNow.Add(_tokenSettings.EmailVerificationLifetime);

        var userResult = User.Register(
            command.FirstName,
            command.LastName,
            emailResult.Value,
            phoneResult.Value,
            passwordHash,
            tokenHash,
            expiresOnUtc);

        if (userResult.IsFailure)
        {
            return Result.Failure<Guid>(userResult.Error);
        }

        var user = userResult.Value;

        // Le compte naît `PendingVerification` (cf. User.Register). Reste à savoir
        // s'il y reste.
        //
        // L'ancien code appelait ici `user.ConfirmEmail(tokenHash, …)` — c'est-à-dire
        // qu'il confirmait l'e-mail avec le jeton qu'il venait lui-même de fabriquer
        // trois lignes plus haut. Le commentaire l'assumait comme un raccourci de
        // démo, mais il avait deux conséquences fâcheuses : tout compte était actif
        // instantanément, et la base affirmait `EmailVerified = true` pour des
        // adresses que personne n'avait jamais vérifiées.
        //
        // Désormais l'activation est une DÉCISION, prise par un administrateur ou
        // par la configuration — jamais une confirmation fabriquée.
        // ACTIVATION.
        //
        // LIBRE-SERVICE (CreatedByAdmin == false) : le compte N'EST PAS activé ici.
        // Il naît PendingVerification et n'est activé que lorsque l'utilisateur
        // confirme son adresse e-mail (VerifyEmailCode). Sans cela, un compte
        // pourrait se connecter sans jamais prouver qu'il possède l'adresse — c'est
        // exactement le trou qu'on ferme (un non-vérifié ne doit pas entrer).
        //
        // CRÉÉ PAR UN ADMIN : l'administrateur se porte garant de l'adresse, on peut
        // donc activer d'emblée (sauf si la politique exige tout de même une
        // approbation manuelle).
        if (command.CreatedByAdmin && !_registrationPolicy.RequireApprovalForAdminCreated)
        {
            user.Approve();
        }

        var buyerRole = await _roleRepository.GetByNameAsync(DefaultRoleName, cancellationToken);
        if (buyerRole is not null)
        {
            user.AssignRole(buyerRole.Id.Value);
        }

        await _userRepository.AddAsync(user, cancellationToken);

        await _publisher.PublishAsync(
            new EmailVerificationRequestedIntegrationEvent
            {
                UserId = user.Id.Value,
                Email = user.Email.Value,
                FirstName = user.FirstName,
                VerificationToken = rawToken
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _security.Registration("unknown");
        _business.UserRegistered();

        return user.Id.Value;
    }
}
