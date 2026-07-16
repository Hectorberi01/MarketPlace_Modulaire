namespace Marketplace.BuildingBlocks.Infrastructure.Outbox;

/// <summary>
/// Politique de réessai de l'outbox : combien de fois, à quel rythme, et quand abandonner.
///
/// ═════════════════════════════════════════════════════════════════════════════
/// LES DEUX ERREURS SYMÉTRIQUES QU'IL FAUT ÉVITER.
///
/// • <b>Rejouer sans fin</b> (l'état d'origine) : un message empoisonné bloque une place
///   dans chaque lot, à jamais. À 50, l'outbox du module est morte — plus rien ne passe.
///
/// • <b>Abandonner trop vite</b> : une panne passagère (Resend indisponible 3 minutes, la
///   base qui redémarre) enterrerait des messages parfaitement sains. Or un message enterré,
///   c'est un vendeur jamais payé.
///
/// Le backoff exponentiel tranche : on insiste beaucoup au début (la plupart des pannes
/// durent quelques secondes), on espace ensuite, et on n'abandonne qu'après ~2 h — bien
/// au-delà de la durée d'un incident ordinaire.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
public sealed class OutboxRetryPolicy
{
    /// <summary>
    /// Nombre de tentatives avant mise en lettre morte. 10 essais ≈ 2 h de persévérance.
    /// </summary>
    public int MaxAttempts { get; init; } = 10;

    /// <summary>Délai de base (première temporisation).</summary>
    public TimeSpan BaseDelay { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Plafond du délai : au-delà, on n'espace plus davantage.</summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Prochaine tentative après <paramref name="attemptCount"/> échecs.
    ///
    /// Progression : 10 s, 20 s, 40 s, 80 s, 160 s, 320 s, 640 s, puis plafonné à 30 min.
    ///
    /// La GIGUE (jitter, ±20 %) n'est pas cosmétique. Sans elle, tous les messages tombés
    /// pendant la même panne — souvent des centaines — reviendraient EXACTEMENT au même
    /// instant, et frapperaient le service qui vient à peine de se relever. On l'achèverait,
    /// et le cycle recommencerait : c'est le « troupeau qui charge ». La gigue étale la
    /// reprise.
    /// </summary>
    public TimeSpan DelayFor(int attemptCount, Random random)
    {
        // 2^(n-1) × base, saturé au plafond. Le calcul est fait en `double` pour ne pas
        // déborder : 2^62 ticks n'a aucun sens, et un dépassement produirait un délai
        // NÉGATIF — donc un rejeu immédiat, c'est-à-dire précisément la boucle qu'on ferme.
        var exponent = Math.Min(attemptCount - 1, 20);
        var seconds = BaseDelay.TotalSeconds * Math.Pow(2, Math.Max(0, exponent));
        seconds = Math.Min(seconds, MaxDelay.TotalSeconds);

        var jitter = 1.0 + ((random.NextDouble() - 0.5) * 0.4); // ±20 %
        return TimeSpan.FromSeconds(seconds * jitter);
    }

    /// <summary>
    /// Applique un échec au message : incrémente le compteur, puis TEMPORISE le message ou
    /// l'ENTERRE selon qu'il reste ou non des tentatives.
    ///
    /// Vit ici, et pas dans le processeur, pour une raison simple : c'est la règle la plus
    /// importante de tout le mécanisme, et une règle qui ne peut pas être testée n'est pas
    /// une règle — c'est une intention. Le processeur, lui, n'est qu'une boucle et des logs.
    /// </summary>
    /// <returns><c>true</c> si le message vient d'être mis en LETTRE MORTE.</returns>
    public bool RegisterFailure(OutboxMessage message, string error, Random random, DateTime nowUtc)
    {
        message.AttemptCount++;
        message.Error = error.Length <= 2000 ? error : error[..2000];

        if (message.AttemptCount >= MaxAttempts)
        {
            message.DeadLetteredOnUtc = nowUtc;

            // On efface la temporisation : un message enterré n'est plus « en attente d'une
            // prochaine tentative », il n'en aura plus. Laisser une date ici laisserait
            // croire qu'il va revenir.
            message.NextAttemptAtUtc = null;
            return true;
        }

        // C'EST CETTE LIGNE QUI SUPPRIME LE BLOCAGE DE TÊTE DE FILE. Tant que cette date
        // n'est pas atteinte, le message sort du lot — et les messages sains passent devant.
        message.NextAttemptAtUtc = nowUtc.Add(DelayFor(message.AttemptCount, random));
        return false;
    }
}
