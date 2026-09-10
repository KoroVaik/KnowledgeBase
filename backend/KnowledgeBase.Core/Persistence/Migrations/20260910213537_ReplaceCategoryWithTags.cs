using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceCategoryWithTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Confirmed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoteTags",
                columns: table => new
                {
                    NoteId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TagId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteTags", x => new { x.NoteId, x.TagId });
                    table.ForeignKey(
                        name: "FK_NoteTags_Notes_NoteId",
                        column: x => x.NoteId,
                        principalTable: "Notes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_NoteTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoteTags_TagId",
                table: "NoteTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);

            // Back-fill before the drop: one Tag per distinct existing Category (the starting
            // vocabulary, so Confirmed = true), then a primary NoteTag for every note - binned
            // ones included, they keep their classification on screen.
            migrationBuilder.Sql("""
                INSERT INTO "Tags" ("Id", "Name", "Confirmed")
                SELECT replace(gen_random_uuid()::text, '-', ''), "Category", true
                FROM "Notes"
                GROUP BY "Category";
                """);

            migrationBuilder.Sql("""
                INSERT INTO "NoteTags" ("NoteId", "TagId", "Ordinal")
                SELECT n."Id", t."Id", 0
                FROM "Notes" AS n
                JOIN "Tags" AS t ON t."Name" = n."Category";
                """);

            migrationBuilder.DropIndex(
                name: "IX_Notes_Category",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Notes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "Notes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            // Rebuild Category from the primary tag; untagged notes keep the empty default.
            migrationBuilder.Sql("""
                UPDATE "Notes" AS n
                SET "Category" = t."Name"
                FROM "NoteTags" AS nt
                JOIN "Tags" AS t ON t."Id" = nt."TagId"
                WHERE nt."NoteId" = n."Id" AND nt."Ordinal" = 0;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Notes_Category",
                table: "Notes",
                column: "Category");

            migrationBuilder.DropTable(
                name: "NoteTags");

            migrationBuilder.DropTable(
                name: "Tags");
        }
    }
}
