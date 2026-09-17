using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoAnalysisCandidateSupersession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAtUtc",
                table: "PhotoAnalysisCandidates",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnalysisCandidates_Kind_SubjectAssetId_SupersededAtUtc",
                table: "PhotoAnalysisCandidates",
                columns: new[] { "Kind", "SubjectAssetId", "SupersededAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PhotoAnalysisCandidates_Kind_SubjectAssetId_SupersededAtUtc",
                table: "PhotoAnalysisCandidates");

            migrationBuilder.DropColumn(
                name: "SupersededAtUtc",
                table: "PhotoAnalysisCandidates");
        }
    }
}
