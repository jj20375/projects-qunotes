using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotePlatform.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteBranding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "QuoteNumberPattern",
                table: "company_settings",
                type: "varchar(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Q{yyyy}{MM}-{seq:4}")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "EnglishName",
                table: "companies",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QuoteNumberPattern",
                table: "company_settings");

            migrationBuilder.DropColumn(
                name: "EnglishName",
                table: "companies");
        }
    }
}
