using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ChoNaBojo.Server.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "Sports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Venues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<Point>(type: "geometry(Point,4326)", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Venues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VenueSports",
                columns: table => new
                {
                    VenueId = table.Column<int>(type: "integer", nullable: false),
                    SportId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VenueSports", x => new { x.VenueId, x.SportId });
                    table.ForeignKey(
                        name: "FK_VenueSports_Sports_SportId",
                        column: x => x.SportId,
                        principalTable: "Sports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VenueSports_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Sports",
                columns: new[] { "Id", "Code", "Name" },
                values: new object[,]
                {
                    { 1, "football", "Piłka nożna" },
                    { 2, "basketball", "Koszykówka" },
                    { 3, "volleyball", "Siatkówka" },
                    { 4, "tennis", "Tenis" },
                    { 5, "running", "Bieganie" },
                    { 6, "cycling", "Kolarstwo" },
                    { 7, "rollerblading", "Jazda na rolkach" },
                    { 8, "gym", "Siłownia" },
                    { 9, "street_workout", "Street workout" },
                    { 10, "swimming", "Pływanie" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sports_Code",
                table: "Sports",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Venues_Location",
                table: "Venues",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_VenueSports_SportId",
                table: "VenueSports",
                column: "SportId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VenueSports");

            migrationBuilder.DropTable(
                name: "Sports");

            migrationBuilder.DropTable(
                name: "Venues");
        }
    }
}
