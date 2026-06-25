using Marketplace.BuildingBlocks.Application.Abstractions;
using Marketplace.Modules.Sellers.Application.Abstractions;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Identity.Contracts;
using Marketplace.Modules.Sellers.Domain.Sellers;

namespace Marketplace.Modules.Sellers.Application.Sellers.Commands.RegisterSeller;

/// <summary>
/// Onboarde un vendeur. Vérifie d'abord, via un appel IN-PROCESS au module
/// Identity (ses Contracts), que le compte existe et que l'e-mail est confirmé —
/// jamais d'accès direct à la base d'Identity. Refuse les doublons (un compte =
/// un vendeur, nom de boutique unique).
/// </summary>
internal sealed class RegisterSellerCommandHandler : ICommandHandler<RegisterSellerCommand, Guid>
{
    private readonly ISellerRepository _sellerRepository;
    private readonly IIdentityModuleApi _identityModuleApi;
    private readonly ISellerUnitOfWork _unitOfWork;

    public RegisterSellerCommandHandler(
        ISellerRepository sellerRepository,
        IIdentityModuleApi identityModuleApi,
        ISellerUnitOfWork unitOfWork)
    {
        _sellerRepository = sellerRepository;
        _identityModuleApi = identityModuleApi;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<Guid>> Handle(RegisterSellerCommand command, CancellationToken cancellationToken)
    {
        var user = await _identityModuleApi.GetUserAsync(command.UserId, cancellationToken);
        if (user is null)
        {
            return Error.NotFound("sellers.seller.user_not_found", "Compte utilisateur introuvable.");
        }

        if (!user.EmailVerified)
        {
            return Error.Forbidden("sellers.seller.email_unverified", "L'e-mail du compte doit être vérifié avant l'onboarding vendeur.");
        }

        if (await _sellerRepository.ExistsForUserAsync(command.UserId, cancellationToken))
        {
            return Error.Conflict("sellers.seller.already_seller", "Ce compte est déjà rattaché à une boutique.");
        }

        if (await _sellerRepository.ShopNameExistsAsync(command.ShopName.Trim(), cancellationToken))
        {
            return Error.Conflict("sellers.seller.shop_name_taken", $"Le nom de boutique « {command.ShopName} » est déjà pris.");
        }

        var result = Seller.Register(command.UserId, command.ShopName, command.CommissionRate);
        if (result.IsFailure)
        {
            return Result.Failure<Guid>(result.Error);
        }

        await _sellerRepository.AddAsync(result.Value, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return result.Value.Id.Value;
    }
}
