using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotePlatform.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddClientDepartmentAndRoleCreator : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CreatedBy",
                table: "roles",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "clients",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.CreateIndex(
                name: "IX_clients_CompanyId_DepartmentId",
                table: "clients",
                columns: new[] { "CompanyId", "DepartmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_clients_DepartmentId",
                table: "clients",
                column: "DepartmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_clients_departments_DepartmentId",
                table: "clients",
                column: "DepartmentId",
                principalTable: "departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // 既有客戶回填所屬部門：先用負責人目前的部門，沒有負責人（或負責人沒有部門）的歸到公司最上層部門。
            migrationBuilder.Sql(@"
                UPDATE clients c
                JOIN company_memberships m ON m.UserId = c.OwnerUserId AND m.CompanyId = c.CompanyId
                SET c.DepartmentId = m.DepartmentId
                WHERE c.DepartmentId IS NULL AND m.DepartmentId IS NOT NULL;");
            migrationBuilder.Sql(@"
                UPDATE clients c
                SET c.DepartmentId = (
                    SELECT d.Id FROM departments d
                    WHERE d.CompanyId = c.CompanyId AND d.ParentId IS NULL
                    ORDER BY d.SortOrder, d.CreatedAt LIMIT 1)
                WHERE c.DepartmentId IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_clients_departments_DepartmentId",
                table: "clients");

            migrationBuilder.DropIndex(
                name: "IX_clients_CompanyId_DepartmentId",
                table: "clients");

            migrationBuilder.DropIndex(
                name: "IX_clients_DepartmentId",
                table: "clients");

            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "roles");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "clients");
        }
    }
}
