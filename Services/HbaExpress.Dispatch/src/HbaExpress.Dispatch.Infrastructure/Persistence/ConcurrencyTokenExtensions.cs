using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HbaExpress.Dispatch.Infrastructure.Persistence;

/// <summary>
/// Verrou optimiste PostgreSQL, adossé à la colonne système <c>xmin</c>.
///
/// Repris à l'identique de <c>Marketplace.BuildingBlocks.Infrastructure.Persistence</c>.
/// Recopié plutôt que référencé : ramener tout BuildingBlocks.Infrastructure ici
/// apporterait l'outbox, ModuleDbContext et le découpage en schémas du monolithe, dont
/// rien n'a de sens dans ce service. Douze lignes valent mieux que cette dépendance.
///
/// <para>
/// <b>Ce que fait `xmin`.</b> C'est une colonne SYSTÈME : chaque ligne Postgres la porte
/// déjà et y garde le numéro de la transaction qui l'a écrite en dernier. On ne l'ajoute
/// pas — aucune colonne n'est créée — on la LIT. EF l'inclut alors dans le <c>WHERE</c>
/// de chaque <c>UPDATE</c> : si une autre transaction a modifié la ligne entre-temps,
/// l'UPDATE touche 0 ligne et EF lève <see cref="DbUpdateConcurrencyException"/>.
/// </para>
///
/// <para>
/// UN JETON N'EST ÉVALUÉ QUE DANS UN `UPDATE`. Si une opération ne modifie aucune colonne
/// de la table parente — parce qu'elle n'écrit que des lignes ENFANTS — EF n'émet aucun
/// UPDATE et ce verrou est totalement inerte sur ce chemin. Avant de poser cette
/// extension sur un agrégat, vérifier que le chemin qu'on prétend protéger écrit bien une
/// colonne du parent. Voir DeliveryJobConfiguration, où cette vérification est faite
/// explicitement pour l'acceptation d'une course.
/// </para>
/// </summary>
public static class ConcurrencyTokenExtensions
{
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
