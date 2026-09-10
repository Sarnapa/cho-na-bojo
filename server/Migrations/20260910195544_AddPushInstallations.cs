using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChoNaBojo.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPushInstallations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PushInstallations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceRegistrationId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DisabledUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushInstallations", x => x.Id);
                    table.CheckConstraint("CK_PushInstallations_DeviceRegistrationId_NotBlank", "BTRIM(\"DeviceRegistrationId\") <> ''");
                    table.CheckConstraint("CK_PushInstallations_LastSeenUtc", "\"LastSeenUtc\" >= \"CreatedUtc\"");
                    table.ForeignKey(
                        name: "FK_PushInstallations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushInstallations_DeviceRegistrationId",
                table: "PushInstallations",
                column: "DeviceRegistrationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushInstallations_UserId",
                table: "PushInstallations",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushInstallations");
        }
    }
}
