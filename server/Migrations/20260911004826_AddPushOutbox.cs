using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChoNaBojo.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddPushOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PushOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    EventKey = table.Column<string>(type: "text", nullable: false),
                    RecipientUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    SportsEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventJoinRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClaimedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushOutbox", x => x.Id);
                    table.CheckConstraint("CK_PushOutbox_Type", "\"Type\" IN (1, 2, 3)");
                    table.ForeignKey(
                        name: "FK_PushOutbox_EventJoinRequests_EventJoinRequestId",
                        column: x => x.EventJoinRequestId,
                        principalTable: "EventJoinRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PushOutbox_SportsEvents_SportsEventId",
                        column: x => x.SportsEventId,
                        principalTable: "SportsEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PushOutbox_Users_RecipientUserId",
                        column: x => x.RecipientUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PushDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    PushOutboxItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    PushInstallationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AcceptedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeadLetteredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "text", nullable: true),
                    FcmMessageId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDeliveries", x => x.Id);
                    table.CheckConstraint("CK_PushDeliveries_AttemptCount", "\"AttemptCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_PushDeliveries_PushInstallations_PushInstallationId",
                        column: x => x.PushInstallationId,
                        principalTable: "PushInstallations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PushDeliveries_PushOutbox_PushOutboxItemId",
                        column: x => x.PushOutboxItemId,
                        principalTable: "PushOutbox",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PushDeliveries_PushInstallationId",
                table: "PushDeliveries",
                column: "PushInstallationId");

            migrationBuilder.CreateIndex(
                name: "IX_PushDeliveries_PushOutboxItemId_PushInstallationId",
                table: "PushDeliveries",
                columns: new[] { "PushOutboxItemId", "PushInstallationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushOutbox_EventJoinRequestId",
                table: "PushOutbox",
                column: "EventJoinRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_PushOutbox_EventKey",
                table: "PushOutbox",
                column: "EventKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushOutbox_NextAttemptUtc_OccurredUtc",
                table: "PushOutbox",
                columns: new[] { "NextAttemptUtc", "OccurredUtc" },
                filter: "\"CompletedUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PushOutbox_RecipientUserId",
                table: "PushOutbox",
                column: "RecipientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PushOutbox_SportsEventId",
                table: "PushOutbox",
                column: "SportsEventId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PushDeliveries");

            migrationBuilder.DropTable(
                name: "PushOutbox");
        }
    }
}
