using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotePlatform.Api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateOfficeThemeComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Theme",
                table: "office_rooms",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "wood",
                comment: "地圖主題（wood / carpet / dark / park / space）",
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "wood",
                oldComment: "地圖主題（wood / carpet / dark）")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Theme",
                table: "office_rooms",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "wood",
                comment: "地圖主題（wood / carpet / dark）",
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldMaxLength: 20,
                oldDefaultValue: "wood",
                oldComment: "地圖主題（wood / carpet / dark / park / space）")
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");
        }
    }
}
