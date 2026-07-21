using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Notifications.Application.Abstractions;
using Marketplace.Modules.Notifications.Domain.Preferences;

namespace Marketplace.Modules.Notifications.Application.Notifications.Preferences;

internal sealed class UpdateNotificationPreferencesCommandHandler
    : ICommandHandler<UpdateNotificationPreferencesCommand>
{
    private readonly INotificationPreferenceRepository _repository;
    private readonly INotificationsUnitOfWork _unitOfWork;

    public UpdateNotificationPreferencesCommandHandler(
        INotificationPreferenceRepository repository, INotificationsUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result> Handle(UpdateNotificationPreferencesCommand command, CancellationToken cancellationToken)
    {
        var pref = await _repository.GetByUserAsync(command.UserId, cancellationToken);
        if (pref is null)
        {
            pref = NotificationPreference.Create(command.UserId);
            await _repository.AddAsync(pref, cancellationToken);
        }

        // On ne conserve que des catégories connues : un client qui envoie n'importe
        // quoi ne peut pas polluer la table.
        var muted = command.MutedCategories
            .Select(c => c.Trim().ToLowerInvariant())
            .Where(NotificationCategories.IsKnown)
            .ToList();

        pref.Replace(muted);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}
