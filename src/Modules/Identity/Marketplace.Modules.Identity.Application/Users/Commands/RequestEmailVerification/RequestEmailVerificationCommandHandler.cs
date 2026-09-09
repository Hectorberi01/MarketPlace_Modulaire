using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Contracts.IntegrationEvents;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.RequestEmailVerification;

/// <summary>
/// Charge le compte, génère un code numérique (seul le hash est stocké), l'inscrit
/// comme code de vérification en attente, puis publie l'event d'envoi d'e-mail.
/// N'active pas le compte et ne touche pas à <c>EmailVerified</c>.
/// </summary>
internal sealed class RequestEmailVerificationCommandHandler : ICommandHandler<RequestEmailVerificationCommand>
{
    /// <summary>
    /// Délai minimal entre deux envois pour un même compte. Assez long pour qu'une
    /// boucle ne serve à rien, assez court pour qu'un e-mail perdu se redemande sans
    /// que l'utilisateur ait l'impression que le bouton est cassé.
    /// </summary>
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(1);

    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IAuthTokenSettings _tokenSettings;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public RequestEmailVerificationCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IAuthTokenSettings tokenSettings,
        IIntegrationEventPublisher publisher,
        IIdentityUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _tokenSettings = tokenSettings;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RequestEmailVerificationCommand command, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(new UserId(command.UserId), cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("identity.user.not_found", $"Compte {command.UserId} introuvable."));
        }

        // ═════════════════════════════════════════════════════════════════════════
        // BUDGET D'ENVOI PAR COMPTE. LE LIMITEUR D'IP NE BORNE PAS CE QU'IL FAUT.
        //
        // La politique `auth` plafonne à 30 requêtes par minute et par ADRESSE IP. Sur
        // une route dont l'effet est d'envoyer un e-mail à un TIERS, ça ne borne rien
        // d'utile : trente e-mails par minute vers des victimes choisies, et autant de
        // fois qu'on a d'adresses IP. `AuthRateLimiter` le dit lui-même — la vraie
        // parade est par compte, et elle n'était pas faite.
        //
        // Un code encore frais n'est donc pas remplacé, et aucun e-mail ne part. La
        // commande répond SUCCÈS : l'appelant légitime a déjà son code dans sa boîte,
        // et un échec ferait varier la réponse des routes qu'on vient d'uniformiser.
        //
        // LA DATE D'ÉMISSION EST DÉDUITE, faute d'être stockée : `expire - durée de vie`.
        // Le calcul est juste tant que `EmailVerificationLifetime` ne change pas ; si
        // elle change, les codes émis avant la bascule sont mal datés — l'erreur est
        // bornée par une durée de vie et se résorbe seule. C'est ce qui évitait une
        // migration pour une colonne qui n'aurait servi qu'à ça.
        // ═════════════════════════════════════════════════════════════════════════
        var now = DateTime.UtcNow;
        if (user.EmailVerificationExpiresOnUtc is { } currentExpiry)
        {
            var issuedAtUtc = currentExpiry - _tokenSettings.EmailVerificationLifetime;
            if (now - issuedAtUtc < ResendCooldown)
            {
                return Result.Success();
            }
        }

        var (code, codeHash) = _tokenGenerator.GenerateNumericCode();
        var expiresOnUtc = now.Add(_tokenSettings.EmailVerificationLifetime);

        var begin = user.BeginEmailVerification(codeHash, expiresOnUtc);
        if (begin.IsFailure)
        {
            return begin;
        }

        await _publisher.PublishAsync(
            new EmailVerificationRequestedIntegrationEvent
            {
                UserId = user.Id.Value,
                Email = user.Email.Value,
                FirstName = user.FirstName,
                VerificationToken = code
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
