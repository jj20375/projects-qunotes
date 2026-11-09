using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuotePlatform.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddQuoteItemPriceLocked : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PriceLocked",
                table: "quote_items",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false,
                comment: "議價調整總價時鎖定此列單價，不參與分攤");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceLocked",
                table: "quote_items");
        }
    }
}
