using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChoNaBojo.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddEventLifecycleStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_EventJoinRequests_Status",
                table: "EventJoinRequests");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "SportsEvents",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "StatusChangedUtc",
                table: "SportsEvents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SportsEvents_Status_EstimatedEndsAtUtc",
                table: "SportsEvents",
                columns: new[] { "Status", "EstimatedEndsAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SportsEvents_Status",
                table: "SportsEvents",
                sql: "\"Status\" IN (1, 2, 3)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SportsEvents_StatusChangedUtc",
                table: "SportsEvents",
                sql: "(\r\n	\"Status\" = 1 AND \"StatusChangedUtc\" IS NULL\r\n)\r\nOR\r\n(\r\n	\"Status\" <> 1 AND \"StatusChangedUtc\" IS NOT NULL\r\n)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EventJoinRequests_Status",
                table: "EventJoinRequests",
                sql: "\"Status\" IN (1, 2, 3, 4, 5, 6)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SportsEvents_Status_EstimatedEndsAtUtc",
                table: "SportsEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SportsEvents_Status",
                table: "SportsEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SportsEvents_StatusChangedUtc",
                table: "SportsEvents");

            migrationBuilder.DropCheckConstraint(
                name: "CK_EventJoinRequests_Status",
                table: "EventJoinRequests");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "SportsEvents");

            migrationBuilder.DropColumn(
                name: "StatusChangedUtc",
                table: "SportsEvents");

            migrationBuilder.AddCheckConstraint(
                name: "CK_EventJoinRequests_Status",
                table: "EventJoinRequests",
                sql: "\"Status\" IN (1, 2, 3)");
        }
    }
}
