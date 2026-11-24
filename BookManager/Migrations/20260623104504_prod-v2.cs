using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookManagerApi.Migrations
{
    /// <inheritdoc />
    public partial class prodv2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE IF EXISTS audit_logs
                DROP CONSTRAINT IF EXISTS fk_audit_logs_users_user_id;

                DROP INDEX IF EXISTS ix_audit_logs_action;
                DROP INDEX IF EXISTS ix_audit_logs_created_at_utc;
                DROP INDEX IF EXISTS ix_audit_logs_user_id;

                ALTER TABLE audit_logs
                ADD COLUMN IF NOT EXISTS new_values text;

                ALTER TABLE audit_logs
                ADD COLUMN IF NOT EXISTS old_values text;

                ALTER TABLE audit_logs
                ALTER COLUMN status_code DROP NOT NULL;

                ALTER TABLE audit_logs
                ALTER COLUMN action DROP NOT NULL;

                ALTER TABLE audit_logs
                ALTER COLUMN created_at_utc DROP DEFAULT;

                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'audit_logs'
                          AND column_name = 'created_at_utc'
                          AND data_type = 'timestamp without time zone'
                    ) THEN
                        ALTER TABLE audit_logs
                        ALTER COLUMN created_at_utc TYPE timestamp with time zone
                        USING created_at_utc AT TIME ZONE 'UTC';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "new_values",
                table: "audit_logs");

            migrationBuilder.DropColumn(
                name: "old_values",
                table: "audit_logs");

            migrationBuilder.AlterColumn<int>(
                name: "status_code",
                table: "audit_logs",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "created_at_utc",
                table: "audit_logs",
                type: "timestamp without time zone",
                nullable: false,
                defaultValueSql: "(NOW() AT TIME ZONE 'UTC')",
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<string>(
                name: "action",
                table: "audit_logs",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_action",
                table: "audit_logs",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_created_at_utc",
                table: "audit_logs",
                column: "created_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_user_id",
                table: "audit_logs",
                column: "user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_audit_logs_users_user_id",
                table: "audit_logs",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
