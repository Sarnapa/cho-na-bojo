using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChoNaBojo.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddEventJoinRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventJoinRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    SportsEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequesterUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventJoinRequests", x => x.Id);
                    table.CheckConstraint("CK_EventJoinRequests_Status", "\"Status\" IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_EventJoinRequests_SportsEvents_SportsEventId",
                        column: x => x.SportsEventId,
                        principalTable: "SportsEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventJoinRequests_Users_RequesterUserId",
                        column: x => x.RequesterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SportsEvents_VenueId_EstimatedEndsAtUtc",
                table: "SportsEvents",
                columns: new[] { "VenueId", "EstimatedEndsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventJoinRequests_RequesterUserId",
                table: "EventJoinRequests",
                column: "RequesterUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventJoinRequests_SportsEventId_RequesterUserId",
                table: "EventJoinRequests",
                columns: new[] { "SportsEventId", "RequesterUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventJoinRequests_SportsEventId_Status",
                table: "EventJoinRequests",
                columns: new[] { "SportsEventId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventJoinRequests");

            migrationBuilder.DropIndex(
                name: "IX_SportsEvents_VenueId_EstimatedEndsAtUtc",
                table: "SportsEvents");
        }
    }
}
