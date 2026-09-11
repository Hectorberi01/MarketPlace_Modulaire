using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;
using Marketplace.Modules.Delivery.Application.Abstractions;
using Marketplace.Modules.Delivery.Contracts;
using Marketplace.Modules.Delivery.Domain.Couriers;

namespace Marketplace.Modules.Delivery.Application.Couriers;

/// <summary>Un coursier par son identifiant.</summary>
public sealed record GetCourierQuery(Guid CourierId) : IQuery<CourierSummary>;

/// <summary>Le coursier rattaché à un compte utilisateur.</summary>
public sealed record GetCourierByUserQuery(Guid UserId) : IQuery<CourierSummary>;

/// <summary>Liste des coursiers, filtrable par statut (console admin).</summary>
public sealed record ListCouriersQuery(string? Status = null) : IQuery<IReadOnlyList<CourierSummary>>;

/// <summary>
/// Les coursiers tels qu'un SÉLECTEUR en a besoin : nom, téléphone, statut. Sans compte
/// de versement, et BORNÉE — voir <see cref="CourierPickerView"/> et
/// <see cref="ListCouriersForPickerQueryHandler.Plafond"/>.
/// </summary>
public sealed record ListCouriersForPickerQuery : IQuery<CourierPickerList>;

internal sealed class GetCourierQueryHandler : IQueryHandler<GetCourierQuery, CourierSummary>
{
    private readonly ICourierRepository _couriers;

    public GetCourierQueryHandler(ICourierRepository couriers) => _couriers = couriers;

    public async Task<Result<CourierSummary>> Handle(GetCourierQuery query, CancellationToken cancellationToken)
    {
        var courier = await _couriers.GetByIdAsync(new CourierId(query.CourierId), cancellationToken);
        return courier is null
            ? Error.NotFound("delivery.courier.not_found", "Coursier introuvable.")
            : CourierMapper.ToSummary(courier);
    }
}

internal sealed class GetCourierByUserQueryHandler : IQueryHandler<GetCourierByUserQuery, CourierSummary>
{
    private readonly ICourierRepository _couriers;

    public GetCourierByUserQueryHandler(ICourierRepository couriers) => _couriers = couriers;

    public async Task<Result<CourierSummary>> Handle(GetCourierByUserQuery query, CancellationToken cancellationToken)
    {
        var courier = await _couriers.GetByUserAsync(query.UserId, cancellationToken);
        return courier is null
            ? Error.NotFound("delivery.courier.not_found", "Aucun compte coursier pour cet utilisateur.")
            : CourierMapper.ToSummary(courier);
    }
}

internal sealed class ListCouriersQueryHandler : IQueryHandler<ListCouriersQuery, IReadOnlyList<CourierSummary>>
{
    private readonly ICourierRepository _couriers;

    public ListCouriersQueryHandler(ICourierRepository couriers) => _couriers = couriers;

    public async Task<Result<IReadOnlyList<CourierSummary>>> Handle(
        ListCouriersQuery query, CancellationToken cancellationToken)
    {
        // Un statut inconnu ne filtre RIEN plutôt que de ne rien rendre : une faute de
        // frappe dans l'URL doit donner une liste complète, pas une page vide qu'on prend
        // pour « aucun coursier ».
        CourierStatus? status = Enum.TryParse<CourierStatus>(query.Status, ignoreCase: true, out var parsed)
            ? parsed
            : null;

        var couriers = await _couriers.ListAsync(status, cancellationToken);
        IReadOnlyList<CourierSummary> views = couriers.Select(CourierMapper.ToSummary).ToList();
        return Result.Success(views);
    }
}

internal sealed class ListCouriersForPickerQueryHandler
    : IQueryHandler<ListCouriersForPickerQuery, CourierPickerList>
{
    /// <summary>
    /// Nombre de coursiers rendus au plus.
    ///
    /// ─────────────────────────────────────────────────────────────────────────
    /// UNE BORNE PLUTÔT QU'UNE PAGINATION, ET C'EST ASSUMÉ.
    ///
    /// Un menu déroulant ne se pagine pas : il filtre ce qu'on lui a donné. Paginer
    /// vraiment supposerait une recherche côté serveur dans le sélecteur, ce que le
    /// composant ne sait pas faire aujourd'hui. La borne, elle, empêche au moins qu'une
    /// flotte de trois mille coursiers parte d'un coup.
    ///
    /// Le jour où ce plafond est atteint, la liste est SILENCIEUSEMENT incomplète — et
    /// c'est le signal qu'il faut la recherche serveur, pas un plafond plus haut.
    /// ─────────────────────────────────────────────────────────────────────────
    /// </summary>
    internal const int Plafond = 500;

    private readonly ICourierRepository _couriers;

    public ListCouriersForPickerQueryHandler(ICourierRepository couriers) => _couriers = couriers;

    public async Task<Result<CourierPickerList>> Handle(
        ListCouriersForPickerQuery query, CancellationToken cancellationToken)
    {
        var couriers = await _couriers.ListAsync(status: null, cancellationToken);

        IReadOnlyList<CourierPickerView> views = couriers
            .Take(Plafond)
            .Select(c => new CourierPickerView(
                c.Id.Value, c.DisplayName, c.Phone, c.Status.ToString()))
            .ToList();

        // LE TOTAL EST COMPTÉ AVANT LA BORNE, et il est gratuit : `ListAsync` a déjà
        // ramené toute la flotte en mémoire — c'est d'ailleurs ce que la borne ci-dessus
        // cherche à ne pas renvoyer au navigateur, pas ce qu'elle évite de lire.
        // Sans ce compte, l'écran ne peut pas distinguer « la flotte tient dans la
        // liste » de « la liste est coupée », et se tait dans les deux cas.
        return Result.Success(new CourierPickerList(views, couriers.Count));
    }
}
