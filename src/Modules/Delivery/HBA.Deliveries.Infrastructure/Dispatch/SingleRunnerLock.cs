using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HBA.Deliveries.Infrastructure.Dispatch;

/// <summary>
/// ═════════════════════════════════════════════════════════════════════════════
/// UN SEUL PROCESSUS À LA FOIS DANS UNE BOUCLE DE FOND.
///
/// LE PROBLÈME QUE CECI FERME
///
/// Les boucles de dispatch et de webhooks lisent un lot, le traitent, puis
/// enregistrent. Rien ne les protège de la concurrence. Deux répliques de l'API
/// liraient les MÊMES courses : la même course serait proposée à deux livreurs
/// différents — les deux reçoivent la notification, un seul arrive — et le même
/// webhook partirait deux fois chez le partenaire.
///
/// Le dépôt le savait déjà pour l'outbox : « deux répliques de l'API = double
/// dispatch. Avant de scaler l'API, il faut implémenter le verrou de ligne. »
/// C'était une CONSIGNE DE DÉPLOIEMENT — c'est-à-dire une chose qu'on oublie le
/// jour où l'on ajoute une réplique pour tenir la charge du vendredi soir.
///
/// POURQUOI UN VERROU CONSULTATIF ET NON « SELECT … FOR UPDATE SKIP LOCKED »
///
/// Le verrou de ligne est la réponse canonique, et c'est la bonne quand plusieurs
/// travailleurs doivent se PARTAGER une file. Ici, ce n'est pas le besoin : ces
/// boucles sont conçues pour tourner en un seul exemplaire, et leur débit n'est
/// pas la contrainte — quelques dizaines de courses par tour de cinq secondes.
///
/// Le verrou de ligne exigerait en plus d'ouvrir une transaction explicite
/// englobant la lecture ET les commandes MediatR qui suivent, chacune faisant son
/// propre SaveChanges. C'est un remaniement des trois requêtes et des deux
/// boucles, pour un gain de parallélisme dont personne n'a besoin — et que je ne
/// peux pas éprouver ici.
///
/// Le verrou consultatif dit exactement ce qu'on veut dire : « un seul ». Il est
/// pris sur une connexion, libéré à sa fermeture, et se comporte correctement si
/// le processus meurt — PostgreSQL le relâche à la fin de la session.
///
/// ⚠️ CE VERROU NE REMPLACE PAS OUTBOX_ENABLED.
///
/// Il empêche le double traitement ; il n'empêche pas N processus d'ouvrir
/// chacun une connexion pour tenter de le prendre. Le réglage reste utile pour ne
/// pas payer ces connexions sur les quatre BFF.
/// ═════════════════════════════════════════════════════════════════════════════
/// </summary>
internal sealed class SingleRunnerLock : IAsyncDisposable
{
    /// <summary>
    /// Clés d'application des verrous. Arbitraires mais STABLES : deux boucles
    /// différentes doivent avoir des clés différentes, sinon l'une empêcherait
    /// l'autre de tourner — et l'on chercherait longtemps pourquoi les webhooks
    /// ne partent que quand le dispatch est au repos.
    /// </summary>
    public const long DispatchKey = 771_001;

    public const long WebhookKey = 771_002;

    private readonly NpgsqlConnection? _connection;
    private readonly long _key;
    private readonly bool _openedHere;

    private SingleRunnerLock(NpgsqlConnection? connection, long key, bool openedHere)
    {
        _connection = connection;
        _key = key;
        _openedHere = openedHere;
    }

    /// <summary>Le verrou a-t-il été obtenu ? Faux = un autre processus travaille déjà.</summary>
    public bool Acquired => _connection is not null;

    /// <summary>
    /// Tente de prendre le verrou. Ne BLOQUE jamais : si un autre processus le
    /// détient, on rend la main et le tour suivant réessaiera dans quelques
    /// secondes. Attendre immobiliserait un thread de l'hôte pour rien.
    /// </summary>
    public static async Task<SingleRunnerLock> TryAcquireAsync(
        DbContext dbContext, long key, CancellationToken cancellationToken)
    {
        if (dbContext.Database.GetDbConnection() is not NpgsqlConnection connection)
        {
            // Pas PostgreSQL : on n'a pas de verrou à offrir. On laisse passer
            // plutôt que de bloquer une boucle — le cas ne se produit qu'avec un
            // fournisseur de test en mémoire.
            return new SingleRunnerLock(null, key, openedHere: false);
        }

        var openedHere = connection.State is not ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // ⚠️ pg_try_advisory_lock et NON pg_advisory_xact_lock : ce dernier est
        // lié à une transaction, et ces boucles n'en ouvrent pas d'explicite. Le
        // verrou de session est relâché par Dispose, ou par PostgreSQL si le
        // processus meurt.
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock($1)";
        command.Parameters.Add(new NpgsqlParameter { Value = key });

        var acquired = await command.ExecuteScalarAsync(cancellationToken) is true;

        if (acquired)
        {
            return new SingleRunnerLock(connection, key, openedHere);
        }

        if (openedHere)
        {
            await connection.CloseAsync();
        }

        return new SingleRunnerLock(null, key, openedHere: false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is null)
        {
            return;
        }

        try
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT pg_advisory_unlock($1)";
            command.Parameters.Add(new NpgsqlParameter { Value = _key });
            await command.ExecuteScalarAsync();
        }
        catch
        {
            // Libération best-effort : si la connexion est déjà tombée, PostgreSQL
            // a relâché le verrou avec la session. Lever ici masquerait l'erreur
            // réelle du tour de boucle.
        }

        if (_openedHere)
        {
            await _connection.CloseAsync();
        }
    }
}
