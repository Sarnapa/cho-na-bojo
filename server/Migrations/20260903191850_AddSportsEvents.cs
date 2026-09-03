using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChoNaBojo.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSportsEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SportsEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    OrganizerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    VenueId = table.Column<int>(type: "integer", nullable: false),
                    SportId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EstimatedEndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ParticipantLimit = table.Column<int>(type: "integer", nullable: false),
                    AutoAccept = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SportsEvents", x => x.Id);
                    table.CheckConstraint("CK_SportsEvents_ClientRequestId_NotEmpty", "\"ClientRequestId\" <> '00000000-0000-0000-0000-000000000000'::uuid");
                    table.CheckConstraint("CK_SportsEvents_Description_NotBlank", "\"Description\" IS NULL OR BTRIM(\"Description\") <> ''");
                    table.CheckConstraint("CK_SportsEvents_ParticipantLimit", "\"ParticipantLimit\" BETWEEN 2 AND 300");
                    table.CheckConstraint("CK_SportsEvents_TimeRange", "\"EstimatedEndsAtUtc\" > \"StartsAtUtc\"\r\nAND \"EstimatedEndsAtUtc\" <= \"StartsAtUtc\" + INTERVAL '24 hours'");
                    table.CheckConstraint("CK_SportsEvents_Title_NotBlank", "BTRIM(\"Title\") <> ''");
                    table.ForeignKey(
                        name: "FK_SportsEvents_Users_OrganizerUserId",
                        column: x => x.OrganizerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SportsEvents_VenueSports_VenueId_SportId",
                        columns: x => new { x.VenueId, x.SportId },
                        principalTable: "VenueSports",
                        principalColumns: new[] { "VenueId", "SportId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SportsEvents_OrganizerUserId_ClientRequestId",
                table: "SportsEvents",
                columns: new[] { "OrganizerUserId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SportsEvents_VenueId_SportId",
                table: "SportsEvents",
                columns: new[] { "VenueId", "SportId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SportsEvents");
        }
    }
}
