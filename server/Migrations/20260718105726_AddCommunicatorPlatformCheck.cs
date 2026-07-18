using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChoNaBojo.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunicatorPlatformCheck : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Users_CommunicatorPlatform",
                table: "Users",
                sql: "\"CommunicatorPlatform\" IS NULL OR \"CommunicatorPlatform\" IN (1, 2, 3)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Users_CommunicatorPlatform",
                table: "Users");
        }
    }
}
