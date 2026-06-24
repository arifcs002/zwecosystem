using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecommerce.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyApprovalAndUserTypeStaticDates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("88888888-8888-8888-8888-888888888888"),
                column: "updated_at",
                value: new DateTime(2026, 6, 24, 8, 30, 10, 294, DateTimeKind.Utc).AddTicks(3931));

            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("99999999-9999-9999-9999-999999999999"),
                column: "updated_at",
                value: new DateTime(2026, 6, 24, 8, 30, 10, 294, DateTimeKind.Utc).AddTicks(2924));
        }
    }
}
