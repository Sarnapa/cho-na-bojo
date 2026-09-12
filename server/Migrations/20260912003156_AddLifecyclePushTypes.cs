using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChoNaBojo.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLifecyclePushTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PushOutbox_Type",
                table: "PushOutbox");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PushOutbox_Type",
                table: "PushOutbox",
                sql: "\"Type\" IN (1, 2, 3, 4, 5, 6)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PushOutbox_Type",
                table: "PushOutbox");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PushOutbox_Type",
                table: "PushOutbox",
                sql: "\"Type\" IN (1, 2, 3)");
        }
    }
}
