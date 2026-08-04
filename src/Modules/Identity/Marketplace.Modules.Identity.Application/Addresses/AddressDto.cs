using Marketplace.BuildingBlocks.Domain.Geography;

namespace Marketplace.Modules.Identity.Application.Addresses;

/// <summary>
/// Vue d'une adresse du carnet utilisateur.
///
/// <see cref="CommuneCode"/> est ce que le client renvoie à l'écriture ;
/// <see cref="CommuneName"/> et <see cref="DepartmentName"/> ne servent QU'À L'AFFICHAGE
/// et sont résolus ici — ainsi une application mobile n'a pas besoin d'embarquer les
/// 77 communes pour afficher une adresse déjà enregistrée.
///
/// <see cref="IsComplete"/> vaut <c>false</c> pour les adresses antérieures à la refonte
/// (sans commune normalisée, sans repère, ou sans téléphone valide). Les surfaces DOIVENT
/// s'en servir : le checkout la refuse, le carnet la signale.
/// </summary>
public sealed record AddressDto(
    Guid Id,
    string Label,
    string Recipient,
    string Phone,
    string? CommuneCode,
    string CommuneName,
    string DepartmentName,
    string? Quartier,
    string? Landmark,
    string? Line1,
    string CountryCode,
    bool IsDefault,
    bool IsComplete)
{
    /// <summary>
    /// Une ligne prête à afficher ou à lire au téléphone, du plus précis au plus large :
    /// « En face de la pharmacie Sainte-Rita, Fidjrossè, Cotonou ».
    ///
    /// Le repère vient EN PREMIER, à l'inverse d'une adresse occidentale. C'est ce que le
    /// livreur cherche : la commune, il la connaît déjà.
    /// </summary>
    public string SingleLine => string.Join(", ", new[] { Landmark, Quartier, Line1, CommuneName }
        .Where(p => !string.IsNullOrWhiteSpace(p)));

    public static AddressDto From(Marketplace.Modules.Identity.Domain.Addresses.Address a) => new(
        a.Id.Value,
        a.Label,
        a.Recipient,
        a.Phone,
        a.CommuneCode,
        a.CommuneName,
        BeninGeography.Commune(a.CommuneCode)?.Department.Name ?? string.Empty,
        a.Quartier,
        a.Landmark,
        a.Line1,
        a.CountryCode,
        a.IsDefault,
        a.IsComplete);
}
