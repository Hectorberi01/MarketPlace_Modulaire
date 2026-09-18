using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Domain.Devices;

namespace Marketplace.Modules.Notifications.Application.Devices;

/// <summary>Enregistre (ou réassocie) un jeton d'appareil FCM pour l'utilisateur.</summary>
public sealed record RegisterDeviceTokenCommand(Guid UserId, string Token, string Platform) : ICommand;

/// <summary>Retire un jeton d'appareil (déconnexion / désabonnement push).</summary>
/// <summary>
/// Retire un jeton d'appareil. <paramref name="UserId"/> BORNE l'opération à son
/// propriétaire.
///
/// Ce paramètre manquait. La commande ne prenait que le jeton, et le BFF vendeur
/// vérifiait l'identité de l'appelant… pour la jeter aussitôt. N'importe quel vendeur
/// authentifié connaissant le jeton FCM d'un concurrent coupait ses notifications
/// push — y compris les alertes de nouvelle commande. Un déni de service silencieux
/// entre concurrents, sur la fonction qui prévient qu'on a vendu.
/// </summary>
public sealed record UnregisterDeviceTokenCommand(Guid UserId, string Token) : ICommand;

internal sealed class RegisterDeviceTokenCommandHandler : ICommandHandler<RegisterDeviceTokenCommand>
{
    private readonly IDeviceTokenRepository _repository;
    private readonly INotificationsUnitOfWork _unitOfWork;

    public RegisterDeviceTokenCommandHandler(IDeviceTokenRepository repository, INotificationsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(RegisterDeviceTokenCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure(Error.Validation("device.token_required", "Le jeton d'appareil est requis."));

        // Upsert : un même jeton (installation) ne doit exister qu'une fois.
        var existing = await _repository.GetByTokenAsync(command.Token.Trim(), cancellationToken);
        if (existing is null)
            await _repository.AddAsync(DeviceToken.Create(command.UserId, command.Token, command.Platform), cancellationToken);
        else
            existing.Reassign(command.UserId, command.Platform);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

internal sealed class UnregisterDeviceTokenCommandHandler : ICommandHandler<UnregisterDeviceTokenCommand>
{
    private readonly IDeviceTokenRepository _repository;
    private readonly INotificationsUnitOfWork _unitOfWork;

    public UnregisterDeviceTokenCommandHandler(IDeviceTokenRepository repository, INotificationsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UnregisterDeviceTokenCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Success();

        var existing = await _repository.GetByTokenAsync(command.Token.Trim(), cancellationToken);

        // Le jeton doit appartenir à l'appelant. Silencieux si ce n'est pas le cas :
        // répondre « ce jeton n'est pas à vous » confirmerait qu'il existe et à qui.
        if (existing is not null && existing.UserId == command.UserId)
        {
            _repository.Remove(existing);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Result.Success();
    }
}
