using System;
using System.Collections.Generic;
using Npgsql;

var connectionString =
    (args.Length > 0 ? args[0] : null)
    ?? Environment.GetEnvironmentVariable("CONNECTION_STRING")
    ?? "Host=localhost;Port=5432;Username=postgres;Password=admin;Database=bookmanager";

var expectedColumns = new Dictionary<string, string[]>
{
    ["books"] = new[] { "price" },
    ["book_copies"] = new[] { "order_number", "center_id", "created_at" },
    ["wishlist_requests"] = new[] { "center_id", "issn", "issn_volume", "issn_number" },
    ["users"] = new[] { "preferred_language" },
    ["wishlist_price_reviews"] = new[] { "reviewed_by_user_id" },
    ["audit_logs"] = new[] { "action", "created_at_utc" }
};

await using var conn = new NpgsqlConnection(connectionString);
await conn.OpenAsync();

foreach (var (table, columns) in expectedColumns)
{
    foreach (var column in columns)
    {
        await using var check = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @table
                  AND column_name = @column
            )
            """,
            conn);

        check.Parameters.AddWithValue("table", table);
        check.Parameters.AddWithValue("column", column);

        var exists = (bool)(await check.ExecuteScalarAsync() ?? false);
        if (!exists)
        {
            Console.Error.WriteLine($"Missing column: {table}.{column}");
            return 1;
        }
    }
}

await using (var cmd = new NpgsqlCommand(
    """
    CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
        migration_id character varying(150) NOT NULL,
        product_version character varying(32) NOT NULL,
        CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY (migration_id)
    );
    """,
    conn))
{
    await cmd.ExecuteNonQueryAsync();
}

var migrations = new[]
{
    "20260226114508_prod",
    "20260303065944_AddBookPrice",
    "20260303071310_priceColumn",
    "20260303090105_inDepthPriceReview",
    "20260303093532_OrderNumberShowcase",
    "20260518063752_PendingModelSync",
    "20260518064527_InitialCreate",
    "20260525063340_AddAuditLogs",
    "20260525110814_MigrationName",
    "20260525111346_FixSchema",
    "20260525111809_Baseline"
};

foreach (var migration in migrations)
{
    await using var insert = new NpgsqlCommand(
        """
        INSERT INTO "__EFMigrationsHistory" (migration_id, product_version)
        VALUES (@id, '10.0.2')
        ON CONFLICT (migration_id) DO NOTHING;
        """,
        conn);

    insert.Parameters.AddWithValue("id", migration);
    await insert.ExecuteNonQueryAsync();
}

Console.WriteLine("Baseline complete.");
return 0;
