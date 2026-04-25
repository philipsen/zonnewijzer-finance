using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class UserChargeCardManyToMany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserChargeCards",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChargeCardId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserChargeCards", x => new { x.UserId, x.ChargeCardId });
                    table.ForeignKey(
                        name: "FK_UserChargeCards_ChargeCards_ChargeCardId",
                        column: x => x.ChargeCardId,
                        principalTable: "ChargeCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserChargeCards_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserChargeCards_ChargeCardId",
                table: "UserChargeCards",
                column: "ChargeCardId",
                unique: true);

            // Preserve existing User.ChargeCardId links (one card per user in the old model).
            migrationBuilder.Sql(
                """
                INSERT INTO "UserChargeCards" ("UserId", "ChargeCardId")
                SELECT "Id", "ChargeCardId" FROM "Users" WHERE "ChargeCardId" IS NOT NULL;
                """);

            migrationBuilder.DropColumn(
                name: "ChargeCardId",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserChargeCards");

            migrationBuilder.AddColumn<int>(
                name: "ChargeCardId",
                table: "Users",
                type: "INTEGER",
                nullable: true);
        }
    }
}
