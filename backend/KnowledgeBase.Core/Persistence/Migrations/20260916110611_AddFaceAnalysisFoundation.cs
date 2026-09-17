using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceAnalysisFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessingJobs_AssetId",
                table: "ProcessingJobs");

            migrationBuilder.DropIndex(
                name: "IX_PhotoAnalysisCandidates_RunId_Kind_Rank",
                table: "PhotoAnalysisCandidates");

            migrationBuilder.AddColumn<string>(
                name: "SubjectFaceOccurrenceId",
                table: "PhotoAnalysisCandidates",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FaceOccurrences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RunId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    X = table.Column<int>(type: "integer", nullable: false),
                    Y = table.Column<int>(type: "integer", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    DetectionScore = table.Column<double>(type: "double precision", nullable: false),
                    LandmarksJson = table.Column<string>(type: "jsonb", nullable: false),
                    Embedding = table.Column<float[]>(type: "real[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceOccurrences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceOccurrences_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FaceOccurrences_PhotoAnalysisRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PhotoAnalysisRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PersonReferenceFaces",
                columns: table => new
                {
                    FaceOccurrenceId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PersonId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceDecisionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonReferenceFaces", x => x.FaceOccurrenceId);
                    table.ForeignKey(
                        name: "FK_PersonReferenceFaces_FaceOccurrences_FaceOccurrenceId",
                        column: x => x.FaceOccurrenceId,
                        principalTable: "FaceOccurrences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonReferenceFaces_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PersonReferenceFaces_PhotoAnalysisReviewDecisions_SourceDec~",
                        column: x => x.SourceDecisionId,
                        principalTable: "PhotoAnalysisReviewDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_AssetId_Kind",
                table: "ProcessingJobs",
                columns: new[] { "AssetId", "Kind" },
                unique: true,
                filter: "\"AssetId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnalysisCandidates_RunId_SubjectFaceOccurrenceId_Kind_~",
                table: "PhotoAnalysisCandidates",
                columns: new[] { "RunId", "SubjectFaceOccurrenceId", "Kind", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnalysisCandidates_SubjectFaceOccurrenceId",
                table: "PhotoAnalysisCandidates",
                column: "SubjectFaceOccurrenceId");

            migrationBuilder.CreateIndex(
                name: "IX_FaceOccurrences_AssetId_CreatedAtUtc",
                table: "FaceOccurrences",
                columns: new[] { "AssetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FaceOccurrences_RunId",
                table: "FaceOccurrences",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonReferenceFaces_PersonId",
                table: "PersonReferenceFaces",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonReferenceFaces_SourceDecisionId",
                table: "PersonReferenceFaces",
                column: "SourceDecisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_PhotoAnalysisCandidates_FaceOccurrences_SubjectFaceOccurren~",
                table: "PhotoAnalysisCandidates",
                column: "SubjectFaceOccurrenceId",
                principalTable: "FaceOccurrences",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PhotoAnalysisCandidates_FaceOccurrences_SubjectFaceOccurren~",
                table: "PhotoAnalysisCandidates");

            migrationBuilder.DropTable(
                name: "PersonReferenceFaces");

            migrationBuilder.DropTable(
                name: "FaceOccurrences");

            migrationBuilder.DropIndex(
                name: "IX_ProcessingJobs_AssetId_Kind",
                table: "ProcessingJobs");

            migrationBuilder.DropIndex(
                name: "IX_PhotoAnalysisCandidates_RunId_SubjectFaceOccurrenceId_Kind_~",
                table: "PhotoAnalysisCandidates");

            migrationBuilder.DropIndex(
                name: "IX_PhotoAnalysisCandidates_SubjectFaceOccurrenceId",
                table: "PhotoAnalysisCandidates");

            migrationBuilder.DropColumn(
                name: "SubjectFaceOccurrenceId",
                table: "PhotoAnalysisCandidates");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_AssetId",
                table: "ProcessingJobs",
                column: "AssetId",
                unique: true,
                filter: "\"AssetId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnalysisCandidates_RunId_Kind_Rank",
                table: "PhotoAnalysisCandidates",
                columns: new[] { "RunId", "Kind", "Rank" },
                unique: true);
        }
    }
}
