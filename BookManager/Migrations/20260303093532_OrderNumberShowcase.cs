using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookManagerApi.Migrations
{
    /// <inheritdoc />
    public partial class OrderNumberShowcase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE wishlist_price_reviews
                ADD COLUMN IF NOT EXISTS order_number text;

                ALTER TABLE book_copies
                ADD COLUMN IF NOT EXISTS order_number text;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "order_number",
                table: "wishlist_price_reviews");

            migrationBuilder.DropColumn(
                name: "order_number",
                table: "book_copies");
        }
    }
}
