using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotePlatform.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, comment: "通知唯一識別碼", collation: "ascii_general_ci"),
                    CompanyId = table.Column<Guid>(type: "char(36)", nullable: false, comment: "所屬公司識別碼", collation: "ascii_general_ci"),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false, comment: "收件人使用者識別碼", collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false, comment: "通知類型（followup_due 跟進到期）")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false, comment: "相關專案識別碼", collation: "ascii_general_ci"),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: false, comment: "觸發通知的下次聯絡日（台灣日期）"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false, comment: "通知產生時間戳記（UTC）"),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true, comment: "收件人標示已讀的時間戳記（UTC）；NULL 為未讀")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.Id);
                    table.CheckConstraint("CK_notifications_Type", "Type IN ('followup_due')");
                    table.ForeignKey(
                        name: "FK_notifications_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_notifications_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "站內通知（跟進到期提醒）；同一收件人、類型、專案、到期日只有一則")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_CompanyId_UserId_ReadAt_CreatedAt",
                table: "notifications",
                columns: new[] { "CompanyId", "UserId", "ReadAt", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_ProjectId",
                table: "notifications",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_UserId_Type_ProjectId_DueOn",
                table: "notifications",
                columns: new[] { "UserId", "Type", "ProjectId", "DueOn" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");
        }
    }
}
