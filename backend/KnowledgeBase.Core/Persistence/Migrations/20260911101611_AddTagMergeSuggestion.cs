using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTagMergeSuggestion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SuggestedMergeIntoId",
                table: "Tags",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_SuggestedMergeIntoId",
                table: "Tags",
                column: "SuggestedMergeIntoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tags_Tags_SuggestedMergeIntoId",
                table: "Tags",
                column: "SuggestedMergeIntoId",
                principalTable: "Tags",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Aggregate notes used to take tags from the model. Now a synthesis carries exactly
            // the tag it was built from, and the index carries none.
            migrationBuilder.Sql("""
                DELETE FROM "NoteTags" AS nt
                USING "Notes" AS n
                WHERE nt."NoteId" = n."Id" AND n."Kind" IN ('Synthesis', 'Index');
                """);

            migrationBuilder.Sql("""
                INSERT INTO "NoteTags" ("NoteId", "TagId", "Ordinal")
                SELECT n."Id", t."Id", 0
                FROM "Notes" AS n
                JOIN "Tags" AS t ON t."Name" = n."SynthesisGroup"
                WHERE n."Kind" = 'Synthesis';
                """);

            // Tags only a synthesis had invented are now on no note at all.
            migrationBuilder.Sql("""
                DELETE FROM "Tags" AS t
                WHERE NOT t."Confirmed"
                  AND NOT EXISTS (SELECT 1 FROM "NoteTags" AS nt WHERE nt."TagId" = t."Id");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tags_Tags_SuggestedMergeIntoId",
                table: "Tags");

            migrationBuilder.DropIndex(
                name: "IX_Tags_SuggestedMergeIntoId",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "SuggestedMergeIntoId",
                table: "Tags");
        }
    }
}
