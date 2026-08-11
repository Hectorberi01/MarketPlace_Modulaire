using HBA.Deliveries.Domain.Partners;
using Marketplace.BuildingBlocks.Application.Messaging;
using Marketplace.BuildingBlocks.Domain.Results;

namespace HBA.Deliveries.Application.Partners.Queries;

/// <summary>
/// Une clé, telle qu'on accepte de la MONTRER.
///
/// ⚠️ NI <c>Hash</c>, NI QUOI QUE CE SOIT QUI S'EN APPROCHE.
///
/// La tentation d'exposer directement l'entité <c>PartnerApiKey</c> est réelle —
/// elle a exactement les champs voulus, plus un. Ce « plus un » est le condensat,
/// et il n'a rien à faire dans une réponse HTTP : il ne sert à personne côté
/// console, et il transforme une lecture d'administration en fuite de matériel
/// cryptographique dès qu'un journal d'accès enregistre les corps de réponse.
///
/// C'est pour cela que ce type existe alors qu'il ressemble tant à l'entité.
/// </summary>
public sealed record PartnerApiKeyView(
    Guid Id,
    string Prefix,
    string? Label,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? RevokedAtUtc,
    DateTime? LastUsedAtUtc);

/// <summary>
/// Un partenaire, vu de la console d'administration.
///
/// <c>WebhookConfigured</c> est un BOOLÉEN et non l'URL accompagnée de son secret :
/// savoir qu'un rappel est en place suffit à l'exploitation.
/// </summary>
public sealed record PartnerView(
    Guid Id,
    string Name,
    string ContactEmail,
    string Status,
    int DailyQuota,
    DateTime CreatedAtUtc,
    string? WebhookUrl,
    bool WebhookConfigured,
    IReadOnlyList<PartnerApiKeyView> ApiKeys);

public sealed record ListPartnersQuery : IQuery<IReadOnlyList<PartnerView>>;

public sealed record GetPartnerQuery(Guid PartnerId) : IQuery<PartnerView>;

internal sealed class PartnerQueryHandler
    : IQueryHandler<ListPartnersQuery, IReadOnlyList<PartnerView>>,
      IQueryHandler<GetPartnerQuery, PartnerView>
{
    private readonly IPartnerRepository _partners;

    public PartnerQueryHandler(IPartnerRepository partners) => _partners = partners;

    public async Task<Result<IReadOnlyList<PartnerView>>> Handle(
        ListPartnersQuery query, CancellationToken cancellationToken)
    {
        var partners = await _partners.ListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<PartnerView>>(
            partners.Select(ToView).ToList());
    }

    public async Task<Result<PartnerView>> Handle(GetPartnerQuery query, CancellationToken cancellationToken)
    {
        var partner = await _partners.GetByIdAsync(new PartnerId(query.PartnerId), cancellationToken);

        return partner is null
            ? Result.Failure<PartnerView>(Error.NotFound("partner.not_found", "Partenaire introuvable."))
            : ToView(partner);
    }

    private static PartnerView ToView(Partner partner)
        => new(
            partner.Id.Value,
            partner.Name,
            partner.ContactEmail,
            partner.Status.ToString(),
            partner.DailyQuota,
            partner.CreatedAtUtc,
            partner.WebhookUrl,
            partner.WebhookSecret is not null,
            partner.ApiKeys
                // Les clés révoquées restent visibles : on doit pouvoir répondre
                // à « depuis quand cette clé est-elle coupée ? », qui est la
                // première question posée quand un partenaire tombe en 401.
                .OrderByDescending(k => k.CreatedAtUtc)
                .Select(k => new PartnerApiKeyView(
                    k.Id, k.Prefix, k.Label, k.IsActive,
                    k.CreatedAtUtc, k.RevokedAtUtc, k.LastUsedAtUtc))
                .ToList());
}
