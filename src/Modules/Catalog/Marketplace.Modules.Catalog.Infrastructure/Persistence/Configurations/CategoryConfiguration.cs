using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Marketplace.Modules.Catalog.Domain.Categories;
using Marketplace.Modules.Catalog.Domain.Products;

namespace Marketplace.Modules.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("categories");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasConversion(id => id.Value, value => new CategoryId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.ParentId);
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();

        builder.Property(c => c.Slug)
            .HasConversion(slug => slug.Value, value => Slug.Create(value).Value)
            .HasColumnName("slug")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(c => c.Path).HasMaxLength(1000).IsRequired();
        builder.Property(c => c.ImageUrl).HasMaxLength(2000);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.Property(c => c.AttributeSchema)
            .HasColumnType("jsonb")
            .HasColumnName("attribute_schema")
            .IsRequired();

        // ─────────────────────────────────────────────────────────────────────────
        // L'UNICITÉ PORTE SUR LE CHEMIN, PLUS SUR LE SLUG.
        //
        // L'index unique sur le slug interdisait toute taxonomie à noms répétés :
        // « Alimentation » ne pouvait exister qu'une seule fois dans l'arbre entier,
        // alors qu'il en faut une sous « Chiens » et une sous « Chats ».
        //
        // Le chemin porte la branche, donc la contrainte tombe au bon endroit : deux
        // sœurs homonymes produisent le même chemin et restent refusées ; deux
        // homonymes sous des parents différents sont acceptées.
        //
        // Le slug garde un index NON unique : il n'identifie rien à lui seul (aucune
        // recherche ne s'y appuie), mais il reste utile aux tris et aux filtres.
        // ─────────────────────────────────────────────────────────────────────────
        builder.HasIndex(c => c.Slug);
        builder.HasIndex(c => c.Path).IsUnique();
        builder.HasIndex(c => c.ParentId);

        builder.Ignore(c => c.DomainEvents);
    }
}
