using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Marketplace.Modules.Marketing.Infrastructure.Migrations
{
    /// <summary>
    /// Les documents juridiques versionnés, et la trace des acceptations.
    ///
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// CETTE MIGRATION N'EFFACE RIEN.
    ///
    /// `content_blocks` reste en place, avec ses textes. La lecture publique consulte
    /// d'abord les documents versionnés et ne retombe sur les anciens blocs que pour les
    /// adresses qu'ils sont seuls à connaître : la bascule se fait document par document,
    /// sans fenêtre pendant laquelle rien n'est servi.
    ///
    /// `identity.users.AcceptedTermsVersion` reste également. Cette migration-ci ne peut
    /// pas la reprendre : la colonne ne dit pas QUEL document a été accepté — c'est
    /// précisément son défaut — et deviner reviendrait à fabriquer des preuves de
    /// consentement. Les acceptations passées restent donc là où elles sont, et les
    /// nouvelles s'écrivent ici.
    /// ═══════════════════════════════════════════════════════════════════════════════
    /// </summary>
    public partial class AddLegalDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "legal_documents",
                schema: "marketing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_documents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "legal_document_versions",
                schema: "marketing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Content = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PublishedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    PublishedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_document_versions", x => x.Id);

                    // CASCADE : supprimer un document emporte ses versions. Ce n'est pas
                    // le geste courant — on ARCHIVE un document, on ne le supprime pas —
                    // mais laisser des versions orphelines derrière une suppression
                    // administrative serait pire : elles resteraient référencées par des
                    // acceptations pointant vers un document qui n'existe plus.
                    table.ForeignKey(
                        name: "FK_legal_document_versions_legal_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "marketing",
                        principalTable: "legal_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "legal_acceptances",
                schema: "marketing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedOnUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Context = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_legal_acceptances", x => x.Id);

                    // AUCUNE clé étrangère, ni vers les versions ni vers `identity.users`.
                    //
                    // Vers `users` : la règle du dépôt interdit une contrainte entre deux
                    // schémas de modules — c'est ce qui rend l'extraction d'un module
                    // mécanique.
                    //
                    // Vers les versions : une preuve de consentement ne doit pas pouvoir
                    // être emportée par une suppression en cascade. Si un document est
                    // supprimé, ces lignes doivent SURVIVRE et devenir inexploitables
                    // bruyamment, plutôt que disparaître silencieusement.
                });

            migrationBuilder.CreateIndex(
                name: "IX_legal_documents_Slug",
                schema: "marketing",
                table: "legal_documents",
                column: "Slug",
                unique: true);

            // L'INDEX QUI PORTE TOUT LE MODÈLE : un numéro de version n'est jamais
            // réemployé dans un même document. Sans lui, deux textes différents peuvent
            // porter le même numéro, et une acceptation ne désigne plus rien.
            migrationBuilder.CreateIndex(
                name: "IX_legal_document_versions_DocumentId_Version",
                schema: "marketing",
                table: "legal_document_versions",
                columns: new[] { "DocumentId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_document_versions_DocumentId_Status",
                schema: "marketing",
                table: "legal_document_versions",
                columns: new[] { "DocumentId", "Status" });

            // Une seule acceptation par personne et par version : la garde du handler lit
            // avant d'écrire, ce qui laisse passer deux appels simultanés — un double clic
            // sur « J'accepte » suffit.
            migrationBuilder.CreateIndex(
                name: "IX_legal_acceptances_UserId_VersionId",
                schema: "marketing",
                table: "legal_acceptances",
                columns: new[] { "UserId", "VersionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_legal_acceptances_VersionId",
                schema: "marketing",
                table: "legal_acceptances",
                column: "VersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // L'ORDRE COMPTE : les versions référencent les documents.
            migrationBuilder.DropTable(
                name: "legal_acceptances",
                schema: "marketing");

            migrationBuilder.DropTable(
                name: "legal_document_versions",
                schema: "marketing");

            migrationBuilder.DropTable(
                name: "legal_documents",
                schema: "marketing");
        }
    }
}
