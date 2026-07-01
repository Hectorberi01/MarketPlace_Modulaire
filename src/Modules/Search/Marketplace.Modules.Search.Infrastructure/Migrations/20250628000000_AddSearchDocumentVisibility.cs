using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Search.Infrastructure.Migrations;

/// <inheritdoc />
public partial class AddSearchDocumentVisibility : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsVisible",
            schema: "search",
            table: "documents",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        // Les produits déjà indexés (avant le verrou de validation) restent
        // visibles pour ne pas vider la vitrine existante. Les nouveaux produits
        // démarrent masqués et n'apparaissent qu'après validation (Active).
        migrationBuilder.Sql("UPDATE search.documents SET \"IsVisible\" = true;");

        migrationBuilder.CreateIndex(
            name: "IX_documents_IsVisible",
            schema: "search",
            table: "documents",
            column: "IsVisible");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_documents_IsVisible",
            schema: "search",
            table: "documents");

        migrationBuilder.DropColumn(
            name: "IsVisible",
            schema: "search",
            table: "documents");
    }
}
