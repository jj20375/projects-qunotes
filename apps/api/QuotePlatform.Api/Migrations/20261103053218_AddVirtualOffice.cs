using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotePlatform.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddVirtualOffice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "office_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, comment: "個人設定唯一識別碼", collation: "ascii_general_ci"),
                    CompanyId = table.Column<Guid>(type: "char(36)", nullable: false, comment: "所屬公司識別碼", collation: "ascii_general_ci"),
                    MembershipId = table.Column<Guid>(type: "char(36)", nullable: false, comment: "所屬成員身分識別碼", collation: "ascii_general_ci"),
                    AvatarKey = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: true, comment: "角色外觀鍵；NULL 表示依使用者雜湊的預設角色")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "available", comment: "手動狀態（available / busy / meeting / break / away）")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StatusText = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: true, comment: "自訂狀態文字")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true, comment: "最後在線時間（UTC）"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false, comment: "最後更新時間戳記（UTC）")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_office_profiles", x => x.Id);
                    table.CheckConstraint("CK_office_profiles_Status", "Status IN ('available', 'busy', 'meeting', 'break', 'away')");
                    table.ForeignKey(
                        name: "FK_office_profiles_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_office_profiles_company_memberships_MembershipId",
                        column: x => x.MembershipId,
                        principalTable: "company_memberships",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "成員虛擬辦公室個人設定檔")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "office_rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, comment: "房間設定唯一識別碼", collation: "ascii_general_ci"),
                    CompanyId = table.Column<Guid>(type: "char(36)", nullable: false, comment: "所屬公司識別碼", collation: "ascii_general_ci"),
                    DepartmentId = table.Column<Guid>(type: "char(36)", nullable: false, comment: "所屬部門識別碼（一個部門一間辦公室）", collation: "ascii_general_ci"),
                    Theme = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, defaultValue: "wood", comment: "地圖主題（wood / carpet / dark）")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false, comment: "建立時間戳記（UTC）"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false, comment: "最後更新時間戳記（UTC）")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_office_rooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_office_rooms_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_office_rooms_departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "部門虛擬辦公室房間設定檔")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_office_profiles_CompanyId",
                table: "office_profiles",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_office_profiles_MembershipId",
                table: "office_profiles",
                column: "MembershipId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_office_rooms_CompanyId_DepartmentId",
                table: "office_rooms",
                columns: new[] { "CompanyId", "DepartmentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_office_rooms_DepartmentId",
                table: "office_rooms",
                column: "DepartmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "office_profiles");

            migrationBuilder.DropTable(
                name: "office_rooms");
        }
    }
}
