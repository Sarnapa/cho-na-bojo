using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChoNaBojo.Server.Migrations
{
    /// <inheritdoc />
    public partial class TranslateSportNamesToEnglish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "Football");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 2,
                column: "Name",
                value: "Basketball");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 3,
                column: "Name",
                value: "Volleyball");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 4,
                column: "Name",
                value: "Tennis");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 5,
                column: "Name",
                value: "Running");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 6,
                column: "Name",
                value: "Cycling");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 7,
                column: "Name",
                value: "Rollerblading");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 8,
                column: "Name",
                value: "Gym");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 9,
                column: "Name",
                value: "Street Workout");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 10,
                column: "Name",
                value: "Swimming");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "Piłka nożna");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 2,
                column: "Name",
                value: "Koszykówka");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 3,
                column: "Name",
                value: "Siatkówka");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 4,
                column: "Name",
                value: "Tenis");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 5,
                column: "Name",
                value: "Bieganie");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 6,
                column: "Name",
                value: "Kolarstwo");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 7,
                column: "Name",
                value: "Jazda na rolkach");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 8,
                column: "Name",
                value: "Siłownia");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 9,
                column: "Name",
                value: "Street workout");

            migrationBuilder.UpdateData(
                table: "Sports",
                keyColumn: "Id",
                keyValue: 10,
                column: "Name",
                value: "Pływanie");
        }
    }
}
