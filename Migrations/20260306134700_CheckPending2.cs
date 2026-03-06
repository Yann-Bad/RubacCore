using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RubacCore.Migrations
{
    /// <inheritdoc />
    public partial class CheckPending2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Centres and UserCentres were already created by CheckPending.
            // This migration re-adds the LDAP fields that were dropped during migration cleanup.
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AuthProvider",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LdapDn",
                table: "Users");
        }
    }
}
