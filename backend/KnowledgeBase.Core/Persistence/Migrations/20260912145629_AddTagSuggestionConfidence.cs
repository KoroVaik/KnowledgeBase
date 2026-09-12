using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTagSuggestionConfidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SuggestedMergeConfidence",
                table: "Tags",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            // One-time backfill for any row from before this column existed - not a model-level
            // default (see TagParentSuggestionConfiguration), so every future insert still sets
            // Confidence itself.
            migrationBuilder.AddColumn<string>(
                name: "Confidence",
                table: "TagParentSuggestions",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Medium");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SuggestedMergeConfidence",
                table: "Tags");

            migrationBuilder.DropColumn(
                name: "Confidence",
                table: "TagParentSuggestions");
        }
    }
}
