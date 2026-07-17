namespace Marketplace.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Ligne d'outbox : un IntegrationEvent sérialisé, écrit dans la MÊME
/// transaction que le changement d'état métier. Un processeur le publie ensuite
/// (in-process aujourd'hui, Kafka demain) — garantie « au moins une fois ».
/// Vit dans le schéma du module propriétaire.
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Nom de type stable « FullName, AssemblyName » (sans version).</summary>
    public string Type { get; init; } = default!;

    /// <summary>Charge utile JSON de l'event.</summary>
    public string Content { get; init; } = default!;

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;

    public DateTime? ProcessedOnUtc { get; set; }

    /// <summary>Message de la dernière erreur rencontrée. Null tant que rien n'a échoué.</summary>
    public string? Error { get; set; }

    // ═════════════════════════════════════════════════════════════════════════════
    // LES TROIS CHAMPS CI-DESSOUS N'EXISTAIENT PAS. SANS EUX, L'OUTBOX N'AVAIT NI
    // PLAFOND DE TENTATIVES, NI TEMPORISATION, NI ISSUE.
    //
    // Un message « empoisonné » — adresse e-mail invalide, domaine non vérifié chez le
    // fournisseur, JSON qu'aucun type ne sait plus désérialiser — restait
    // `ProcessedOnUtc == null` POUR TOUJOURS. Le processeur le relisait toutes les
    // 5 secondes, échouait, journalisait, recommençait. Éternellement.
    //
    // Le coût réel n'est pas le bruit dans les logs. C'est le BLOCAGE DE TÊTE DE FILE.
    // Le lot est trié par `OccurredOnUtc` et plafonné à 50 : chaque message mort occupe
    // définitivement une place. À 50 messages morts, PLUS AUCUN événement ne passe — les
    // commandes ne sont plus confirmées, les vendeurs ne sont plus crédités, les
    // notifications ne partent plus. Et rien ne le signale, sinon un log qui se répète.
    //
    // Une panne qui commence par un e-mail refusé, et finit par une plateforme muette.
    // ═════════════════════════════════════════════════════════════════════════════

    /// <summary>Nombre de tentatives de publication déjà échouées.</summary>
    public int AttemptCount { get; set; }

    /// <summary>
    /// Instant à partir duquel une nouvelle tentative est permise (backoff exponentiel).
    /// Null = éligible immédiatement.
    ///
    /// C'est ce champ qui SORT un message en échec du lot courant : il cesse d'occuper une
    /// place tant que son délai n'est pas écoulé, et les messages sains passent devant.
    /// Sans lui, le plafond de tentatives seul n'aurait pas suffi — le message aurait
    /// simplement épuisé ses essais en quelques secondes, au lieu de laisser à une panne
    /// passagère le temps de se résorber.
    /// </summary>
    public DateTime? NextAttemptAtUtc { get; set; }

    /// <summary>
    /// Instant de mise en lettre morte. Non null = le message a épuisé ses tentatives et ne
    /// sera PLUS JAMAIS rejoué automatiquement.
    ///
    /// ⚠️ Une lettre morte est une perte métier réelle : un e-mail jamais envoyé, un gain
    /// vendeur jamais crédité, un stock jamais libéré. Elle DOIT être vue par un humain —
    /// d'où le log Critical et l'endpoint <c>GET /admin/outbox/dead-letters</c>. Enterrer un
    /// message en silence serait pire que de le rejouer sans fin : au moins, la boucle
    /// finissait par se remarquer.
    /// </summary>
    public DateTime? DeadLetteredOnUtc { get; set; }
}
