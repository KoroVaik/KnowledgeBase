using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhotoAnalysisAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhotoAnalysisRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PipelineVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfigurationHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoAnalysisRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoAnalysisRuns_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoAnalysisCandidates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RunId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubjectAssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProposedTargetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ProposedLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    SignalsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoAnalysisCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoAnalysisCandidates_Assets_SubjectAssetId",
                        column: x => x.SubjectAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoAnalysisCandidates_PhotoAnalysisRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PhotoAnalysisRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PhotoAnalysisReviewDecisions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CandidateId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChosenTargetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoAnalysisReviewDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoAnalysisReviewDecisions_PhotoAnalysisCandidates_Candid~",
                        column: x => x.CandidateId,
                        principalTable: "PhotoAnalysisCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnalysisCandidates_RunId_Kind_Rank",
                table: "PhotoAnalysisCandidates",
                columns: new[] { "RunId", "Kind", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnalysisCandidates_SubjectAssetId",
                table: "PhotoAnalysisCandidates",
                column: "SubjectAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnalysisReviewDecisions_CandidateId_DecidedAtUtc",
                table: "PhotoAnalysisReviewDecisions",
                columns: new[] { "CandidateId", "DecidedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnalysisRuns_AssetId_CompletedAtUtc",
                table: "PhotoAnalysisRuns",
                columns: new[] { "AssetId", "CompletedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PhotoAnalysisReviewDecisions");

            migrationBuilder.DropTable(
                name: "PhotoAnalysisCandidates");

            migrationBuilder.DropTable(
                name: "PhotoAnalysisRuns");
        }
    }
}
