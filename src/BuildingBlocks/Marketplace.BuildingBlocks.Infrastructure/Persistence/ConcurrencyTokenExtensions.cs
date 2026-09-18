using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Marketplace.BuildingBlocks.Infrastructure.Persistence;

/// <summary>
/// Verrou optimiste PostgreSQL, adossé à la colonne système <c>xmin</c>.
/// </summary>
public static class ConcurrencyTokenExtensions
{
    /// <summary>
    /// Déclare <c>xmin</c> comme jeton de concurrence de l'entité.
    ///
    /// ═════════════════════════════════════════════════════════════════════════
    /// POURQUOI CETTE EXTENSION EXISTE PLUTÔT QUE `UseXminAsConcurrencyToken()`.
    ///
    /// L'API Npgsql `UseXminAsConcurrencyToken()` est marquée [Obsolete] — et comme le
    /// dépôt compile en « avertissements = erreurs », elle CASSE LA BUILD. Son message
    /// oriente vers `IsRowVersion()`, ce qui est un mauvais conseil ici : `IsRowVersion`
    /// suppose une colonne `byte[]` gérée par le fournisseur, ce que PostgreSQL n'a pas.
    ///
    /// Ce que faisait réellement `UseXminAsConcurrencyToken()`, c'est EXACTEMENT le corps
    /// ci-dessous : déclarer une propriété shadow nommée `xmin`, de type `xid`, générée
    /// par la base, et jeton de concurrence. On ne perd rien — on l'écrit nous-mêmes,
    /// sans dépendre d'une API dépréciée.
    /// ═════════════════════════════════════════════════════════════════════════
    ///
    /// <para>
    /// <b>Ce que fait `xmin`.</b> C'est une colonne SYSTÈME : chaque ligne Postgres la
    /// porte déjà, et elle contient le numéro de la transaction qui a écrit la ligne en
    /// dernier. On ne l'ajoute pas — <b>aucune colonne n'est créée</b> — on la LIT. EF
    /// l'inclut dès lors dans le <c>WHERE</c> de chaque <c>UPDATE</c> : si une autre
    /// transaction a modifié la ligne entre-temps, l'UPDATE touche 0 ligne et EF lève
    /// <see cref="DbUpdateConcurrencyException"/>, traduite en 409 par
    /// <c>ConcurrencyExceptionHandler</c>.
    /// </para>
    ///
    /// <para>
    /// <b>UN JETON N'EST ÉVALUÉ QUE DANS UN `UPDATE`.</b> Si une opération ne modifie
    /// aucune colonne de la table parente — parce qu'elle n'insère ou ne supprime que des
    /// lignes ENFANTS — EF n'émet aucun UPDATE, et ce verrou est <b>totalement inerte</b>
    /// sur ce chemin. C'était le cas d'<c>InventoryItem.Reserve()</c> : voir la colonne
    /// <c>StockVersion</c>, qui existe uniquement pour salir la ligne parente et forcer
    /// l'UPDATE. Avant de poser cette extension sur un agrégat, vérifiez que le chemin
    /// que vous prétendez protéger écrit bien une colonne du parent.
    /// </para>
    ///
    /// <para>
    /// <b>AUCUN REJEU AUTOMATIQUE, ET C'EST DÉLIBÉRÉ.</b> <c>ModuleDbContext</c>
    /// dispatche les événements de domaine et draine l'outbox <b>avant</b>
    /// <c>base.SaveChangesAsync</c>. Rejouer la commande dans le même scope
    /// re-dispatcherait ces événements et dupliquerait les messages d'outbox : on
    /// corrigerait un bug d'argent en en créant un autre. Le rejeu doit venir d'une
    /// requête neuve.
    /// </para>
    /// </summary>
    public static EntityTypeBuilder<TEntity> UsePostgresRowVersion<TEntity>(
        this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
    {
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        return builder;
    }
}
