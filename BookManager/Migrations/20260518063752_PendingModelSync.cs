using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookManagerApi.Migrations
{
    /// <inheritdoc />
    public partial class PendingModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE wishlist_requests
                ADD COLUMN IF NOT EXISTS issn text;

                ALTER TABLE wishlist_requests
                ADD COLUMN IF NOT EXISTS issn_number text;

                ALTER TABLE wishlist_requests
                ADD COLUMN IF NOT EXISTS issn_volume text;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "issn",
                table: "wishlist_requests");

            migrationBuilder.DropColumn(
                name: "issn_number",
                table: "wishlist_requests");

            migrationBuilder.DropColumn(
                name: "issn_volume",
                table: "wishlist_requests");
        }
    }
}
