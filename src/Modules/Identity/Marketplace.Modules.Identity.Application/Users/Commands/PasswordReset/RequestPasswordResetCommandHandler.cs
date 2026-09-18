using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Application.Observability;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.BuildingBlocks.IntegrationEvents;
using Marketplace.Modules.Identity.Application.Abstractions;
using Marketplace.Modules.Identity.Contracts.IntegrationEvents;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Application.Users.Commands.PasswordReset;

internal sealed class RequestPasswordResetCommandHandler : ICommandHandler<RequestPasswordResetCommand>
{
    private readonly IUserRepository _userRepository;
    private readonly ISecureTokenGenerator _tokenGenerator;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly IIdentityUnitOfWork _unitOfWork;
    private readonly ISecurityMetrics _security;

    public RequestPasswordResetCommandHandler(
        IUserRepository userRepository,
        ISecureTokenGenerator tokenGenerator,
        IIntegrationEventPublisher publisher,
        IIdentityUnitOfWork unitOfWork,
        ISecurityMetrics security)
    {
        _userRepository = userRepository;
        _tokenGenerator = tokenGenerator;
        _publisher = publisher;
        _unitOfWork = unitOfWork;
        _security = security;
    }

    public async Task<Result> Handle(RequestPasswordResetCommand command, CancellationToken cancellationToken)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await _userRepository.GetByEmailAsync(email, cancellationToken);
        if (user is null)
        {
            // Pas d'énumération de comptes : succès silencieux. Le BFF répond exactement
            // la même chose que pour un compte existant.
            //
            // Ne PAS ajouter de délai artificiel « pour égaliser les temps de réponse » :
            // ici le chemin « inconnu » est même plus court que le chemin « connu » (pas de
            // génération de jeton, pas d'écriture). Un attaquant pourrait en théorie
            // mesurer l'écart. La vraie parade est le limiteur de débit posé sur le groupe
            // /auth (30 req/min), qui rend toute campagne de mesure impraticable.
            return Result.Success();
        }

        // CODE numérique à 6 chiffres (comme la vérification e-mail) : bien plus simple
        // à saisir dans l'app mobile qu'un lien à copier. Seul le hash est stocké.
        var (raw, hash) = _tokenGenerator.GenerateNumericCode();
        var begin = user.BeginPasswordReset(hash, DateTime.UtcNow.AddHours(1));
        if (begin.IsFailure)
        {
            return Result.Failure(begin.Error);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // LE JETON SORT PAR ICI, ET SEULEMENT PAR ICI.
        //
        // Il n'est PAS renvoyé à l'appelant (voir le commentaire de la commande : c'est
        // ainsi qu'un endpoint anonyme le divulguait à qui le demandait). Il part dans
        // l'outbox, et le module Notifications l'envoie par e-mail à son propriétaire.
        //
        // La publication précède SaveChanges : `ModuleDbContext` draine les événements
        // d'intégration vers la table d'outbox DANS LA MÊME TRANSACTION que l'écriture du
        // haché. Les deux réussissent ou échouent ensemble — jamais un jeton en base sans
        // e-mail parti, jamais un e-mail parti sans jeton en base.
        // ─────────────────────────────────────────────────────────────────────────
        await _publisher.PublishAsync(
            new PasswordResetRequestedIntegrationEvent
            {
                UserId = user.Id.Value,
                Email = user.Email.Value,
                FirstName = user.FirstName,
                ResetToken = raw,
            },
            cancellationToken);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _security.PasswordReset();
        return Result.Success();
    }
}
