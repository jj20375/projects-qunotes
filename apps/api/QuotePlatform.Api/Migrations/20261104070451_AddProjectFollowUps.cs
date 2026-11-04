using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotePlatform.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastFollowUpAt",
                table: "projects",
                type: "datetime(6)",
                nullable: true,
                comment: "最新一筆跟進紀錄的發生時間（UTC）；由跟進紀錄重算");

            migrationBuilder.AddColumn<DateOnly>(
                name: "NextFollowUpOn",
                table: "projects",
                type: "date",
                nullable: true,
                comment: "下次聯絡日（台灣日期）；最新一筆未填或已按不用跟進時為 NULL");

            migrationBuilder.CreateTable(
                name: "project_followups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, comment: "跟進紀錄唯一識別碼", collation: "ascii_general_ci"),
                    CompanyId = table.Column<Guid>(type: "char(36)", nullable: false, comment: "所屬公司識別碼", collation: "ascii_general_ci"),
                    ProjectId = table.Column<Guid>(type: "char(36)", nullable: false, comment: "所屬專案識別碼", collation: "ascii_general_ci"),
                    Type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, comment: "類型（call / visit / meeting / email / message / other）")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false, comment: "互動發生時間（UTC）"),
                    ContactId = table.Column<Guid>(type: "char(36)", nullable: true, comment: "對象聯絡人識別碼；聯絡人刪除時設為 NULL", collation: "ascii_general_ci"),
                    ContactName = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true, comment: "對象姓名快照")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Summary = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, comment: "主旨")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Content = table.Column<string>(type: "text", nullable: true, comment: "內容")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NextFollowUpOn = table.Column<DateOnly>(type: "date", nullable: true, comment: "下次聯絡日（台灣日期）"),
                    NextAction = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true, comment: "下次要做的事")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NextDismissedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true, comment: "按「不用跟進」的時間（UTC）；NULL 表示仍需提醒"),
                    NextDismissedBy = table.Column<Guid>(type: "char(36)", nullable: true, comment: "按「不用跟進」的使用者識別碼", collation: "ascii_general_ci"),
                    CreatedBy = table.Column<Guid>(type: "char(36)", nullable: false, comment: "撰寫者使用者識別碼", collation: "ascii_general_ci"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false, comment: "建立時間戳記（UTC）"),
                    UpdatedBy = table.Column<Guid>(type: "char(36)", nullable: true, comment: "最後修改者使用者識別碼；未修改過為 NULL", collation: "ascii_general_ci"),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false, comment: "最後更新時間戳記（UTC）"),
                    IsDeleted = table.Column<bool>(type: "tinyint(1)", nullable: false, comment: "是否已軟刪除"),
                    DeletedAt = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true, comment: "軟刪除時間戳記（UTC）")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_project_followups", x => x.Id);
                    table.CheckConstraint("CK_project_followups_Type", "Type IN ('call', 'visit', 'meeting', 'email', 'message', 'other')");
                    table.ForeignKey(
                        name: "FK_project_followups_client_contacts_ContactId",
                        column: x => x.ContactId,
                        principalTable: "client_contacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_project_followups_companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_project_followups_projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "專案跟進紀錄（電話、拜訪、會議等客戶互動與下次聯絡日）")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_projects_CompanyId_NextFollowUpOn",
                table: "projects",
                columns: new[] { "CompanyId", "NextFollowUpOn" });

            migrationBuilder.CreateIndex(
                name: "IX_project_followups_CompanyId_OccurredAt",
                table: "project_followups",
                columns: new[] { "CompanyId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_project_followups_CompanyId_ProjectId_IsDeleted_OccurredAt",
                table: "project_followups",
                columns: new[] { "CompanyId", "ProjectId", "IsDeleted", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_project_followups_ContactId",
                table: "project_followups",
                column: "ContactId");

            migrationBuilder.CreateIndex(
                name: "IX_project_followups_ProjectId",
                table: "project_followups",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_followups");

            migrationBuilder.DropIndex(
                name: "IX_projects_CompanyId_NextFollowUpOn",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "LastFollowUpAt",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "NextFollowUpOn",
                table: "projects");
        }
    }
}
