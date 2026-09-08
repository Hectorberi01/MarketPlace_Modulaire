using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Marketplace.Modules.Settlement.Infrastructure.Persistence;

#nullable disable

namespace Marketplace.Modules.Settlement.Infrastructure.Migrations;

/// <summary>
/// Portefeuille unique pour les quatre acteurs — vendeur, client, livreur, plateforme.
///
/// <para>
/// AVANT : deux tables aux colonnes de soldes figées, « seller_wallets » (pending,
/// available) et « platform_wallet » (commission, provider, shipping, refunds). Ajouter
/// un acteur voulait dire ajouter une table, un agrégat, un dépôt et un flux de retrait
/// — soit quatre occasions de se tromper sur le code qui déplace de l'argent.
/// </para>
///
/// <para>
/// APRÈS : « wallets » porte le propriétaire, « wallet_accounts » porte un solde par
/// sous-compte. Le grand livre modélisait déjà le monde ainsi ; les soldes le rejoignent.
/// </para>
///
/// <para>
/// LA REPRISE ÉCRIT DES LIGNES DE GRAND LIVRE, ET C'EST LE POINT LE PLUS IMPORTANT DE
/// CETTE MIGRATION. L'invariant visé est « somme des écritures d'un compte = son solde ».
/// Or le grand livre ne couvre pas tout l'historique : il a été introduit après les
/// premiers mouvements, et la libération d'un gain n'y écrivait qu'une ligne sur deux.
/// Recopier les soldes sans rien d'autre laisserait donc un écart permanent sur chaque
/// portefeuille, et le contrôle de réconciliation crierait au loup dès le premier jour.
/// On écrit donc une ÉCRITURE D'OUVERTURE par compte non nul, motif « migration » :
/// à partir de là, et pour toujours, la somme rend le solde.
/// </para>
/// </summary>
[DbContext(typeof(SettlementDbContext))]
[Migration("20260908120000_UnifiedWallets")]
public partial class UnifiedWallets : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "wallets",
            schema: "settlement",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_wallets", x => x.Id));

        migrationBuilder.CreateTable(
            name: "wallet_accounts",
            schema: "settlement",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                WalletId = table.Column<Guid>(type: "uuid", nullable: false),
                Account = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Balance = table.Column<decimal>(type: "numeric(18,2)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_wallet_accounts", x => x.Id);
                table.ForeignKey(
                    name: "FK_wallet_accounts_wallets_WalletId",
                    column: x => x.WalletId,
                    principalSchema: "settlement",
                    principalTable: "wallets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        // UN SEUL PORTEFEUILLE PAR COUPLE (TYPE, PROPRIÉTAIRE), et un seul solde par
        // compte. Sans ces deux contraintes, un défaut de code créerait des doublons et
        // le solde deviendrait une question d'ordre de lecture.
        migrationBuilder.CreateIndex(
            name: "IX_wallets_OwnerType_OwnerId",
            schema: "settlement", table: "wallets",
            columns: new[] { "OwnerType", "OwnerId" }, unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_wallet_accounts_WalletId_Account",
            schema: "settlement", table: "wallet_accounts",
            columns: new[] { "WalletId", "Account" }, unique: true);

        // ── Retraits : du vendeur au portefeuille ────────────────────────────────
        migrationBuilder.AddColumn<Guid>(
            name: "WalletId", schema: "settlement", table: "withdrawals",
            type: "uuid", nullable: false, defaultValue: Guid.Empty);

        migrationBuilder.AddColumn<string>(
            name: "OwnerType", schema: "settlement", table: "withdrawals",
            type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Seller");

        migrationBuilder.AddColumn<Guid>(
            name: "OwnerId", schema: "settlement", table: "withdrawals",
            type: "uuid", nullable: false, defaultValue: Guid.Empty);

        // ═════════════════════════════════════════════════════════════════════════
        // REPRISE DES DONNÉES
        // ═════════════════════════════════════════════════════════════════════════

        // 1. Un portefeuille par vendeur existant, en conservant ses dates.
        migrationBuilder.Sql(@"
            INSERT INTO settlement.wallets (""Id"", ""OwnerType"", ""OwnerId"", ""Currency"", ""CreatedAtUtc"", ""UpdatedAtUtc"")
            SELECT ""Id"", 'Seller', ""SellerId"", ""Currency"", ""CreatedAtUtc"", ""UpdatedAtUtc""
            FROM settlement.seller_wallets;");

        // 2. Le portefeuille de la plateforme. Son identifiant de propriétaire est la
        //    constante reprise telle quelle de l'ancien singleton : la clé ne bouge pas.
        migrationBuilder.Sql(@"
            INSERT INTO settlement.wallets (""Id"", ""OwnerType"", ""OwnerId"", ""Currency"", ""CreatedAtUtc"", ""UpdatedAtUtc"")
            SELECT ""Id"", 'Platform', ""Id"", ""Currency"", ""UpdatedAtUtc"", ""UpdatedAtUtc""
            FROM settlement.platform_wallet;");

        // 3. Les soldes deviennent des lignes. Un compte par acteur et par nature, même
        //    à zéro : l'agrégat les matérialise tous à la création, et une lecture ne
        //    doit pas avoir à distinguer « absent » de « nul ».
        migrationBuilder.Sql(@"
            INSERT INTO settlement.wallet_accounts (""Id"", ""WalletId"", ""Account"", ""Balance"")
            SELECT gen_random_uuid(), w.""Id"", a.account, a.balance
            FROM settlement.seller_wallets w
            CROSS JOIN LATERAL (VALUES
                ('Pending',   w.""PendingBalance""),
                ('Available', w.""AvailableBalance"")
            ) AS a(account, balance);");

        migrationBuilder.Sql(@"
            INSERT INTO settlement.wallet_accounts (""Id"", ""WalletId"", ""Account"", ""Balance"")
            SELECT gen_random_uuid(), p.""Id"", a.account, a.balance
            FROM settlement.platform_wallet p
            CROSS JOIN LATERAL (VALUES
                ('Commission', p.""CommissionBalance""),
                ('Provider',   p.""ProviderFeeBalance""),
                ('Shipping',   p.""ShippingBalance""),
                ('Refunds',    p.""RefundsBalance"")
            ) AS a(account, balance);");

        // 4. Rattachement des retraits existants à leur portefeuille vendeur.
        migrationBuilder.Sql(@"
            UPDATE settlement.withdrawals d
            SET ""WalletId"" = w.""Id"", ""OwnerType"" = 'Seller', ""OwnerId"" = d.""SellerId""
            FROM settlement.wallets w
            WHERE w.""OwnerType"" = 'Seller' AND w.""OwnerId"" = d.""SellerId"";");

        // 5. ÉCRITURES D'OUVERTURE. Voir l'explication en tête de migration : sans elles,
        //    la somme du grand livre ne rendrait jamais le solde, et le contrôle de
        //    réconciliation signalerait un écart sur chaque portefeuille repris.
        //    Un solde négatif produit un DÉBIT, un solde positif un CRÉDIT — la dette
        //    d'un vendeur contre-passé est aussi réelle que le gain d'un autre.
        migrationBuilder.Sql(@"
            INSERT INTO settlement.wallet_transactions
                (""Id"", ""OwnerId"", ""OwnerType"", ""Account"", ""Direction"", ""Amount"",
                 ""Currency"", ""Reason"", ""ReferenceType"", ""ReferenceId"", ""CreatedAtUtc"")
            SELECT
                gen_random_uuid(), w.""OwnerId"", w.""OwnerType"", a.""Account"",
                CASE WHEN a.""Balance"" >= 0 THEN 'Credit' ELSE 'Debit' END,
                ABS(a.""Balance""), w.""Currency"", 'migration', 'migration', w.""Id"", NOW()
            FROM settlement.wallet_accounts a
            JOIN settlement.wallets w ON w.""Id"" = a.""WalletId""
            WHERE a.""Balance"" <> 0;");

        // ── Les anciennes tables ne servent plus ─────────────────────────────────
        migrationBuilder.DropColumn(name: "SellerId", schema: "settlement", table: "withdrawals");
        migrationBuilder.DropTable(name: "seller_wallets", schema: "settlement");
        migrationBuilder.DropTable(name: "platform_wallet", schema: "settlement");

        migrationBuilder.CreateIndex(
            name: "IX_withdrawals_OwnerType_OwnerId",
            schema: "settlement", table: "withdrawals",
            columns: new[] { "OwnerType", "OwnerId" });

        // Les valeurs par défaut n'avaient qu'un rôle : permettre d'ajouter des colonnes
        // NOT NULL sur une table peuplée sans la réécrire. Elles n'ont plus de sens.
        migrationBuilder.AlterColumn<Guid>(
            name: "WalletId", schema: "settlement", table: "withdrawals",
            type: "uuid", nullable: false,
            oldClrType: typeof(Guid), oldType: "uuid", oldDefaultValue: Guid.Empty);

        migrationBuilder.AlterColumn<Guid>(
            name: "OwnerId", schema: "settlement", table: "withdrawals",
            type: "uuid", nullable: false,
            oldClrType: typeof(Guid), oldType: "uuid", oldDefaultValue: Guid.Empty);

        migrationBuilder.AlterColumn<string>(
            name: "OwnerType", schema: "settlement", table: "withdrawals",
            type: "character varying(20)", maxLength: 20, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(20)", oldMaxLength: 20,
            oldDefaultValue: "Seller");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Le retour en arrière RECONSTRUIT les deux anciennes tables à partir des soldes
        // repris. Il ne restaure PAS les écritures d'ouverture supprimées : elles n'ont
        // aucun sens dans l'ancien modèle, où le grand livre n'avait pas à rendre le solde.
        migrationBuilder.CreateTable(
            name: "seller_wallets",
            schema: "settlement",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SellerId = table.Column<Guid>(type: "uuid", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                PendingBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                AvailableBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_seller_wallets", x => x.Id));

        migrationBuilder.CreateTable(
            name: "platform_wallet",
            schema: "settlement",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                CommissionBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                ProviderFeeBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                ShippingBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                RefundsBalance = table.Column<decimal>(type: "numeric(18,2)", nullable: false, defaultValue: 0m),
                UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_platform_wallet", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_seller_wallets_SellerId",
            schema: "settlement", table: "seller_wallets", column: "SellerId", unique: true);

        migrationBuilder.Sql(@"
            INSERT INTO settlement.seller_wallets
                (""Id"", ""SellerId"", ""Currency"", ""PendingBalance"", ""AvailableBalance"", ""CreatedAtUtc"", ""UpdatedAtUtc"")
            SELECT w.""Id"", w.""OwnerId"", w.""Currency"",
                   COALESCE(MAX(CASE WHEN a.""Account"" = 'Pending'   THEN a.""Balance"" END), 0),
                   COALESCE(MAX(CASE WHEN a.""Account"" = 'Available' THEN a.""Balance"" END), 0),
                   w.""CreatedAtUtc"", w.""UpdatedAtUtc""
            FROM settlement.wallets w
            LEFT JOIN settlement.wallet_accounts a ON a.""WalletId"" = w.""Id""
            WHERE w.""OwnerType"" = 'Seller'
            GROUP BY w.""Id"", w.""OwnerId"", w.""Currency"", w.""CreatedAtUtc"", w.""UpdatedAtUtc"";");

        migrationBuilder.Sql(@"
            INSERT INTO settlement.platform_wallet
                (""Id"", ""Currency"", ""CommissionBalance"", ""ProviderFeeBalance"", ""ShippingBalance"", ""RefundsBalance"", ""UpdatedAtUtc"")
            SELECT w.""Id"", w.""Currency"",
                   COALESCE(MAX(CASE WHEN a.""Account"" = 'Commission' THEN a.""Balance"" END), 0),
                   COALESCE(MAX(CASE WHEN a.""Account"" = 'Provider'   THEN a.""Balance"" END), 0),
                   COALESCE(MAX(CASE WHEN a.""Account"" = 'Shipping'   THEN a.""Balance"" END), 0),
                   COALESCE(MAX(CASE WHEN a.""Account"" = 'Refunds'    THEN a.""Balance"" END), 0),
                   w.""UpdatedAtUtc""
            FROM settlement.wallets w
            LEFT JOIN settlement.wallet_accounts a ON a.""WalletId"" = w.""Id""
            WHERE w.""OwnerType"" = 'Platform'
            GROUP BY w.""Id"", w.""Currency"", w.""UpdatedAtUtc"";");

        migrationBuilder.AddColumn<Guid>(
            name: "SellerId", schema: "settlement", table: "withdrawals",
            type: "uuid", nullable: false, defaultValue: Guid.Empty);

        migrationBuilder.Sql(@"UPDATE settlement.withdrawals SET ""SellerId"" = ""OwnerId"";");

        migrationBuilder.Sql(@"DELETE FROM settlement.wallet_transactions WHERE ""ReferenceType"" = 'migration';");

        migrationBuilder.DropIndex(name: "IX_withdrawals_OwnerType_OwnerId", schema: "settlement", table: "withdrawals");
        migrationBuilder.DropColumn(name: "WalletId", schema: "settlement", table: "withdrawals");
        migrationBuilder.DropColumn(name: "OwnerType", schema: "settlement", table: "withdrawals");
        migrationBuilder.DropColumn(name: "OwnerId", schema: "settlement", table: "withdrawals");

        migrationBuilder.CreateIndex(
            name: "IX_withdrawals_SellerId", schema: "settlement", table: "withdrawals", column: "SellerId");

        migrationBuilder.DropTable(name: "wallet_accounts", schema: "settlement");
        migrationBuilder.DropTable(name: "wallets", schema: "settlement");
    }
}
