using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HBA.Deliveries.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPartnersPricingAndZones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ═══════════════════════════════════════════════════════════════
            // REPRISE DES COURSES SANS POSITION — AVANT DE PASSER EN NOT NULL.
            //
            // La position devient obligatoire (tarification à la distance). EF
            // avait proposé « defaultValue: 0.0 » pour les lignes existantes :
            // c'est le point (0, 0), dans le golfe de Guinée, à 600 km au sud du
            // Bénin — la valeur que Coordinates.Create refuse explicitement, et
            // que la matérialisation EF ne revalide PAS. Une course ainsi
            // remplie serait dispatchée vers l'océan, sans qu'aucun contrôle ne
            // se déclenche.
            //
            // On procède donc en deux temps, et sans rien supprimer :
            //
            //   1. les courses ENCORE VIVANTES dont la position manque sont
            //      ANNULÉES. On s'apprête à inventer leur destination ; elles ne
            //      doivent pas partir. L'annulation est réversible à la main,
            //      une course mal livrée ne l'est pas.
            //
            //   2. les lignes restantes — toutes terminales — reçoivent le
            //      centre de Cotonou. C'est une valeur de remplissage assumée :
            //      plus rien n'agit sur ces courses, et un point plausible dans
            //      le pays vaut mieux qu'un point en mer.
            //
            // Sur une base vide — le cas de tous les environnements à ce jour —
            // ces deux instructions ne touchent aucune ligne.
            // ═══════════════════════════════════════════════════════════════
            migrationBuilder.Sql(@"
                UPDATE deliveries.deliveries
                   SET ""Status"" = 'Cancelled',
                       ""CancelledAtUtc"" = now() AT TIME ZONE 'UTC',
                       ""CancellationReason"" = 'Position absente : course annulée lors du passage à la tarification par distance.'
                 WHERE (pickup_latitude IS NULL OR pickup_longitude IS NULL
                        OR dropoff_latitude IS NULL OR dropoff_longitude IS NULL)
                   AND ""Status"" NOT IN ('Delivered', 'Cancelled');");

            migrationBuilder.Sql(@"
                UPDATE deliveries.deliveries
                   SET pickup_latitude   = COALESCE(pickup_latitude,   6.3703),
                       pickup_longitude  = COALESCE(pickup_longitude,  2.3912),
                       dropoff_latitude  = COALESCE(dropoff_latitude,  6.3703),
                       dropoff_longitude = COALESCE(dropoff_longitude, 2.3912)
                 WHERE pickup_latitude IS NULL OR pickup_longitude IS NULL
                    OR dropoff_latitude IS NULL OR dropoff_longitude IS NULL;");

            migrationBuilder.AlterColumn<double>(
                name: "pickup_longitude",
                schema: "deliveries",
                table: "deliveries",
                type: "double precision",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "pickup_latitude",
                schema: "deliveries",
                table: "deliveries",
                type: "double precision",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "dropoff_longitude",
                schema: "deliveries",
                table: "deliveries",
                type: "double precision",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.AlterColumn<double>(
                name: "dropoff_latitude",
                schema: "deliveries",
                table: "deliveries",
                type: "double precision",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "double precision",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "partners",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ContactEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DailyQuota = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WebhookUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    WebhookSecret = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partners", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "partner_api_keys",
                schema: "deliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Prefix = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    partner_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_partner_api_keys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_partner_api_keys_partners_partner_id",
                        column: x => x.partner_id,
                        principalSchema: "deliveries",
                        principalTable: "partners",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_partner_api_keys_partner_id",
                schema: "deliveries",
                table: "partner_api_keys",
                column: "partner_id");

            migrationBuilder.CreateIndex(
                name: "ux_partner_api_keys_prefix",
                schema: "deliveries",
                table: "partner_api_keys",
                column: "Prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_partners_status",
                schema: "deliveries",
                table: "partners",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "partner_api_keys",
                schema: "deliveries");

            migrationBuilder.DropTable(
                name: "partners",
                schema: "deliveries");

            migrationBuilder.AlterColumn<double>(
                name: "pickup_longitude",
                schema: "deliveries",
                table: "deliveries",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AlterColumn<double>(
                name: "pickup_latitude",
                schema: "deliveries",
                table: "deliveries",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AlterColumn<double>(
                name: "dropoff_longitude",
                schema: "deliveries",
                table: "deliveries",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");

            migrationBuilder.AlterColumn<double>(
                name: "dropoff_latitude",
                schema: "deliveries",
                table: "deliveries",
                type: "double precision",
                nullable: true,
                oldClrType: typeof(double),
                oldType: "double precision");
        }
    }
}
