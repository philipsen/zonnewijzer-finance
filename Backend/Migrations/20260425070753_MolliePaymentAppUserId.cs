using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class MolliePaymentAppUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MolliePayments_ChargeCardId_Year_Month",
                table: "MolliePayments");

            migrationBuilder.AddColumn<int>(
                name: "AppUserId",
                table: "MolliePayments",
                type: "INTEGER",
                nullable: true);

            // Map each historical row (per charge card) to the linked app user. Pick one user if a card
            // is linked to multiple users (rare) so the value is stable.
            migrationBuilder.Sql(
                """
                UPDATE MolliePayments
                SET AppUserId = (
                    SELECT ucc.UserId
                    FROM UserChargeCards AS ucc
                    WHERE ucc.ChargeCardId = MolliePayments.ChargeCardId
                    ORDER BY ucc.UserId
                    LIMIT 1
                );
                """);

            migrationBuilder.Sql("DELETE FROM MolliePayments WHERE AppUserId IS NULL;");

            // One payment per (app user, year, month) — merge duplicate rows from multiple cards in the old model.
            migrationBuilder.Sql(
                """
                DELETE FROM MolliePayments
                WHERE Id NOT IN (
                    SELECT MIN(Id) FROM MolliePayments
                    WHERE AppUserId IS NOT NULL
                    GROUP BY AppUserId, Year, Month
                );
                """);

            migrationBuilder.AlterColumn<int>(
                name: "AppUserId",
                table: "MolliePayments",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int?),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "ChargeCardId",
                table: "MolliePayments");

            migrationBuilder.CreateIndex(
                name: "IX_MolliePayments_AppUserId_Year_Month",
                table: "MolliePayments",
                columns: new[] { "AppUserId", "Year", "Month" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MolliePayments_Users_AppUserId",
                table: "MolliePayments",
                column: "AppUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MolliePayments_Users_AppUserId",
                table: "MolliePayments");

            migrationBuilder.DropIndex(
                name: "IX_MolliePayments_AppUserId_Year_Month",
                table: "MolliePayments");

            migrationBuilder.AddColumn<int>(
                name: "ChargeCardId",
                table: "MolliePayments",
                type: "INTEGER",
                nullable: true);

            // Best-effort reverse: one representative charge card per app user.
            migrationBuilder.Sql(
                """
                UPDATE MolliePayments
                SET ChargeCardId = (
                    SELECT MIN(ucc.ChargeCardId)
                    FROM UserChargeCards AS ucc
                    WHERE ucc.UserId = MolliePayments.AppUserId
                );
                """);

            migrationBuilder.Sql("DELETE FROM MolliePayments WHERE ChargeCardId IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "ChargeCardId",
                table: "MolliePayments",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(int?),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "AppUserId",
                table: "MolliePayments");

            migrationBuilder.CreateIndex(
                name: "IX_MolliePayments_ChargeCardId_Year_Month",
                table: "MolliePayments",
                columns: new[] { "ChargeCardId", "Year", "Month" },
                unique: true);
        }
    }
}
