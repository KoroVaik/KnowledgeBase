using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteKindAndSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "Notes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Notes",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Source");

            // Every note that exists today was made from a file. The default was only there to
            // fill them in; the model has none, so the column must not keep one.
            migrationBuilder.Sql("""ALTER TABLE "Notes" ALTER COLUMN "Kind" DROP DEFAULT;""");

            // Titles were free to repeat until now, and the index below would refuse to build
            // over duplicates. Newest of each title stays, the rest go to the bin - the same
            // rule a re-run follows when its note replaces the previous one.
            migrationBuilder.Sql("""
                UPDATE "Notes" AS n
                SET "DeletedAtUtc" = now()
                WHERE EXISTS (
                    SELECT 1 FROM "Notes" AS o
                    WHERE o."Title" = n."Title"
                      AND (o."CreatedAtUtc" > n."CreatedAtUtc"
                           OR (o."CreatedAtUtc" = n."CreatedAtUtc" AND o."Id" > n."Id"))
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Notes_Title",
                table: "Notes",
                column: "Title",
                unique: true,
                filter: "\"DeletedAtUtc\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notes_Title",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "Notes");
        }
    }
}
