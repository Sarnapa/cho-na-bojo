using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChoNaBojo.Server.Migrations
{
    /// <inheritdoc />
    public partial class EnforceJoinRequestResolutionTimestamp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "EventJoinRequests"
                SET "UpdatedUtc" = "CreatedUtc"
                WHERE "Status" <> 1 AND "UpdatedUtc" IS NULL;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_EventJoinRequests_StatusUpdatedUtc",
                table: "EventJoinRequests",
                sql: "(\r\n	\"Status\" = 1 AND \"UpdatedUtc\" IS NULL\r\n)\r\nOR\r\n(\r\n	\"Status\" <> 1 AND \"UpdatedUtc\" IS NOT NULL\r\n)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EventJoinRequests_StatusUpdatedUtc",
                table: "EventJoinRequests");
        }
    }
}
