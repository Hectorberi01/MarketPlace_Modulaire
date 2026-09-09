namespace HbaExpress.Dispatch.Application.Abstractions;

/// <summary>
/// Ce que le service doit pouvoir mesurer.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// DEUX DE CES COMPTEURS DOIVENT RESTER À ZÉRO. LES AUTRES SONT DU CONFORT.
///
///  • <see cref="OutboundDeadLettered"/> — une course LIVRÉE dont la rémunération n'a
///    jamais été annoncée à la marketplace. C'est de l'argent dû qui n'est pas parti, et
///    il n'existe aucun mécanisme pour le rattraper tout seul.
///  • <see cref="JobExpired"/> — une course que PERSONNE n'a acceptée en cinq minutes.
///    Tant que le repli en cascade n'existe pas, c'est un acheteur qui attend une
///    livraison que personne ne viendra faire.
///
/// Les deux étaient jusqu'ici journalisés et rien d'autre. Un journal ne réveille
/// personne : il faut le voir pour le lire, et on ne le lit qu'après avoir su qu'il y
/// avait un problème. Un compteur se surveille.
///
/// DES COMPTEURS, PAS DES JAUGES — la raison est la même que côté monolithe : une jauge
/// interrogerait la table par intermittence et raterait ce qu'un administrateur aurait
/// traité entre deux relevés. Un compteur enregistre l'ÉVÉNEMENT au moment où il se
/// produit ; il ne peut pas être manqué.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public interface IDispatchMetrics
{
    /// <summary>Une course créée depuis une commande prête.</summary>
    void JobCreated();

    /// <summary>Une vague d'offres partie : combien de coursiers, à quel tour.</summary>
    void OffersSent(int courierCount, int round);

    /// <summary>Une course prise par un coursier.</summary>
    void JobAccepted();

    /// <summary>Une course livrée.</summary>
    void JobDelivered();

    /// <summary>
    /// Une course DÉCLARÉE EN ÉCHEC par son coursier : acheteur introuvable, colis
    /// refusé, rien à remettre.
    ///
    /// Elle n'était comptée nulle part, et rien d'autre ne la signalait : l'échec ne
    /// produisait ni message sortant ni journal. Un pic d'échecs — une commune mal
    /// desservie, un quartier introuvable, un mode opératoire abusif — était donc
    /// strictement invisible. Ce compteur n'a pas vocation à rester à zéro : quelques
    /// échecs sont normaux, c'est leur TAUX qui se surveille.
    /// </summary>
    void JobFailed();

    /// <summary>
    /// Une course ABANDONNÉE faute de coursier. Doit rester à zéro tant que le repli en
    /// cascade n'existe pas : il n'y a personne derrière.
    /// </summary>
    void JobExpired();

    /// <summary>Un envoi sortant en échec — il sera réessayé.</summary>
    void OutboundSendFailed(string messageType);

    /// <summary>
    /// Un message sortant ABANDONNÉ. Doit rester à zéro : s'il s'agit d'une course
    /// livrée, le coursier n'a pas été payé et ne le sera pas sans intervention.
    /// </summary>
    void OutboundDeadLettered(string messageType);
}

/// <summary>
/// Implémentation muette, utilisée quand aucune n'est fournie — tests unitaires, ou hôte
/// monté sans observabilité.
///
/// Elle existe pour que la mesure ne soit JAMAIS une raison de faire échouer le service :
/// un compteur est un observateur, il n'a pas de droit de veto sur le métier.
/// </summary>
public sealed class NullDispatchMetrics : IDispatchMetrics
{
    public static readonly NullDispatchMetrics Instance = new();

    private NullDispatchMetrics()
    {
    }

    public void JobCreated()
    {
    }

    public void OffersSent(int courierCount, int round)
    {
    }

    public void JobAccepted()
    {
    }

    public void JobDelivered()
    {
    }

    public void JobFailed()
    {
    }

    public void JobExpired()
    {
    }

    public void OutboundSendFailed(string messageType)
    {
    }

    public void OutboundDeadLettered(string messageType)
    {
    }
}
