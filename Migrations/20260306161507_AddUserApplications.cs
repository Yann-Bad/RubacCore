using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RubacCore.Migrations
{
    /// <inheritdoc />
    public partial class AddUserApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserApplications",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    ApplicationClientId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserApplications", x => new { x.UserId, x.ApplicationClientId });
                    table.ForeignKey(
                        name: "FK_UserApplications_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserApplications");
        }
    }
}
