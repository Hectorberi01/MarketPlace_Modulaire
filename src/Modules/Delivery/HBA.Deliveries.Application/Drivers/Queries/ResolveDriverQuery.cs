using HBA.Deliveries.Domain.Drivers;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Application.Drivers.Queries;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// QUEL LIVREUR EST DERRIÈRE CE COMPTE ?
///
/// C'est la pièce qui permet aux routes de ne plus faire confiance au corps de
/// requête. Jusqu'ici, l'application livreur envoyait « driverId » dans le JSON et
/// l'API le croyait sur parole : n'importe quel livreur authentifié pouvait
/// accepter, refuser ou faire avancer la course d'un autre — et, depuis
/// l'introduction de la rémunération, encaisser son gain.
///
/// Le compte utilisateur, lui, vient du jeton signé. Il ne se falsifie pas.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed record ResolveDriverQuery(Guid UserId) : IQuery<Guid>;

internal sealed class ResolveDriverQueryHandler : IQueryHandler<ResolveDriverQuery, Guid>
{
    private readonly IDriverRepository _drivers;

    public ResolveDriverQueryHandler(IDriverRepository drivers) => _drivers = drivers;

    public async Task<Result<Guid>> Handle(ResolveDriverQuery query, CancellationToken cancellationToken)
    {
        var driver = await _drivers.GetByUserIdAsync(query.UserId, cancellationToken);

        // « Interdit » et non « introuvable » : ici, contrairement aux ressources
        // d'un autre partenaire, il n'y a rien à divulguer. L'appelant apprend
        // seulement que SON propre compte n'est pas un compte livreur — ce qu'il
        // sait déjà — et le message lui dit quoi faire.
        return driver is null
            ? Result.Failure<Guid>(Error.Forbidden(
                "driver.not_registered",
                "Ce compte n'est pas rattaché à un livreur. Terminez votre inscription livreur."))
            : driver.Id.Value;
    }
}
