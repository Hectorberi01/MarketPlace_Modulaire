using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Identity.Domain.Users;

namespace Marketplace.Modules.Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.ToTable("user_roles");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.RoleId).IsRequired();

        // Relation vers User : la FK ombre « UserId » est créée ici (avant l'index)
        // et prend le type de la clé principale (UserId, mappé en uuid).
        // IsRequired() — LA CONTRAINTE DOIT VIVRE DANS LA BASE, PAS DANS UN RÉGLAGE EF.
        //
        // Correction d'une affirmation antérieure (voir doc 22) : SANS IsRequired(), EF
        // ne sévrait PAS pour autant. Le `OnDelete(DeleteBehavior.Cascade)` ci-dessous
        // gouverne aussi le sort des ORPHELINS — avec Cascade, un enfant retiré de la
        // collection est SUPPRIMÉ, pas mis à NULL. Les données de production l'ont confirmé.
        //
        // Alors pourquoi IsRequired() ? Pour trois raisons plus modestes et plus sûres :
        //
        //   1. Cette clé étrangère est RÉELLEMENT obligatoire — un enfant sans parent n'a
        //      aucun sens métier. Le modèle le déclarait facultatif. Un modèle qui ment
        //      finit toujours par produire du code qui se trompe.
        //
        //   2. La colonne était NULL-able en base, donc RIEN ne l'interdisait. Une ligne
        //      orpheline a d'ailleurs été trouvée en production (message_reactions) : on
        //      ignore ce qui l'a créée, et c'est précisément le problème. NOT NULL l'aurait
        //      refusée, quelle que soit sa provenance.
        //
        //   3. Sans ça, le comportement dépend d'un réglage FRAGILE : retirer le
        //      `OnDelete(Cascade)` — geste anodin en apparence — ferait réellement basculer
        //      cette relation en sévérance. Avec IsRequired() ET NOT NULL, c'est impossible.
        builder.HasOne<User>()
            .WithMany(u => u.RoleAssignments)
            .HasForeignKey("UserId")
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        // Un même rôle n'est assigné qu'une fois par utilisateur.
        builder.HasIndex("UserId", "RoleId").IsUnique();
    }
}
