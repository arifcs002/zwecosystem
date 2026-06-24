using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecommerce.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyExtraFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bank_account_name",
                table: "companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bank_name",
                table: "companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "bkash_number",
                table: "companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "company_mobile",
                table: "companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "facebook_link",
                table: "companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "instagram_link",
                table: "companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nagad_number",
                table: "companies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_mobile",
                table: "companies",
                type: "text",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "companies",
                keyColumn: "id",
                keyValue: new Guid("b1111111-1111-1111-1111-111111111111"),
                columns: new[] { "bank_account_name", "bank_name", "bkash_number", "company_mobile", "facebook_link", "instagram_link", "nagad_number", "owner_mobile" },
                values: new object[] { null, null, null, null, null, null, null, null });

            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("88888888-8888-8888-8888-888888888888"),
                column: "updated_at",
                value: new DateTime(2026, 6, 24, 9, 3, 50, 257, DateTimeKind.Utc).AddTicks(1671));

            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("99999999-9999-9999-9999-999999999999"),
                column: "updated_at",
                value: new DateTime(2026, 6, 24, 9, 3, 50, 257, DateTimeKind.Utc).AddTicks(661));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bank_account_name",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "bank_name",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "bkash_number",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "company_mobile",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "facebook_link",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "instagram_link",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "nagad_number",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "owner_mobile",
                table: "companies");

            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("88888888-8888-8888-8888-888888888888"),
                column: "updated_at",
                value: new DateTime(2026, 6, 24, 8, 31, 49, 184, DateTimeKind.Utc).AddTicks(2346));

            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("99999999-9999-9999-9999-999999999999"),
                column: "updated_at",
                value: new DateTime(2026, 6, 24, 8, 31, 49, 184, DateTimeKind.Utc).AddTicks(1383));
        }
    }
}
