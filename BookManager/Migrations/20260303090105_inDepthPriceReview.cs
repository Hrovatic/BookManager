using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookManagerApi.Migrations
{
    /// <inheritdoc />
    public partial class inDepthPriceReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE wishlist_price_reviews
                ADD COLUMN IF NOT EXISTS source text;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source",
                table: "wishlist_price_reviews");
        }
    }
}
