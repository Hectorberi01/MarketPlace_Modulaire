using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
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
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public RegisterUserCommandHandler(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPasswordHasher passwordHasher,
        ISecureTokenGenerator tokenGenerator,
        IAuthTokenSettings tokenSettings,
        IIntegrationEventPublisher publisher,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _tokenSettings = tokenSettings;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
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

        if (await _userRepository.EmailExistsAsync(emailResult.Value.Value, cancellationToken))
        {
            return Error.Conflict("identity.user.email_taken", "Un compte existe déjà avec cet e-mail.");
        }

        if (await _userRepository.PhoneExistsAsync(phoneResult.Value.Value, cancellationToken))
        {
            return Error.Conflict("identity.user.phone_taken", "Un compte existe déjà avec ce numéro.");
        }

        var passwordHash = _passwordHasher.Hash(command.Password);
        var (rawToken, tokenHash) = _tokenGenerator.Generate();
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

        // Dev/démo : aucun service d'e-mailing n'est branché, donc on confirme
        // l'e-mail immédiatement pour que le compte soit Actif et puisse se
        // connecter. En production : retirer cet appel et confirmer via le lien
        // reçu par e-mail (event EmailVerificationRequested ci-dessous).
        user.ConfirmEmail(tokenHash, DateTime.UtcNow);

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

        return user.Id.Value;
    }
}
