using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowReopenedCandidateDuplicates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PhotoAnalysisCandidates_RunId_SubjectFaceOccurrenceId_Kind_~",
                table: "PhotoAnalysisCandidates");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnalysisCandidates_RunId_SubjectFaceOccurrenceId_Kind_~",
                table: "PhotoAnalysisCandidates",
                columns: new[] { "RunId", "SubjectFaceOccurrenceId", "Kind", "Rank" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PhotoAnalysisCandidates_RunId_SubjectFaceOccurrenceId_Kind_~",
                table: "PhotoAnalysisCandidates");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnalysisCandidates_RunId_SubjectFaceOccurrenceId_Kind_~",
                table: "PhotoAnalysisCandidates",
                columns: new[] { "RunId", "SubjectFaceOccurrenceId", "Kind", "Rank" },
                unique: true);
        }
    }
}
