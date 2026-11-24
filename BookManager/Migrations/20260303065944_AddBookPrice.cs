using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookManagerApi.Migrations
{
    /// <inheritdoc />
    public partial class AddBookPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE books
                ADD COLUMN IF NOT EXISTS price numeric;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "price",
                table: "books");
        }
    }
}
