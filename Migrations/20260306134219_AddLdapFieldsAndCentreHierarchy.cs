using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RubacCore.Migrations
{
    /// <inheritdoc />
    public partial class AddLdapFieldsAndCentreHierarchy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Re-add LDAP fields that were accidentally reverted during migration reconciliation
            migrationBuilder.AddColumn<string>(
                name: "AuthProvider",
                table: "Users",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "local");

            migrationBuilder.AddColumn<string>(
                name: "LdapDn",
                table: "Users",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Centres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SubdivisionAdministrative = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Centres", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Centres_Centres_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Centres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UserCentres",
                columns: table => new
                {
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CentreId = table.Column<int>(type: "int", nullable: false),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCentres", x => new { x.UserId, x.CentreId });
                    table.ForeignKey(
                        name: "FK_UserCentres_Centres_CentreId",
                        column: x => x.CentreId,
                        principalTable: "Centres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserCentres_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Centres_Code",
                table: "Centres",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Centres_ParentId",
                table: "Centres",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_UserCentres_CentreId",
                table: "UserCentres",
                column: "CentreId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserCentres");

            migrationBuilder.DropTable(
                name: "Centres");

            migrationBuilder.DropColumn(
                name: "AuthProvider",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LdapDn",
                table: "Users");
        }
    }
}
