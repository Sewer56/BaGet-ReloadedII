using Microsoft.EntityFrameworkCore.Migrations;

namespace BaGetter.Database.Sqlite.Migrations;

public partial class ReloadedApiKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add ApiKey column to Packages table.
        // Note: EF Core tracks applied migrations in __EFMigrationsHistory.
        // If this migration is tracked, EF Core will skip it on subsequent runs.
        // However, EF Core does not check if the column already exists before attempting to add it.
        migrationBuilder.AddColumn<string>(
            name: "ApiKey",
            table: "Packages",
            type: "TEXT",
            maxLength: 128,
            nullable: false,
            defaultValue: "");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ApiKey",
            table: "Packages");
    }
}
