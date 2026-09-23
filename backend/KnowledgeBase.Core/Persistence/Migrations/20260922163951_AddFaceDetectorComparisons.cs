using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceDetectorComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FaceComparisonRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    JobId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ImageWidth = table.Column<int>(type: "integer", nullable: true),
                    ImageHeight = table.Column<int>(type: "integer", nullable: true),
                    ContentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceComparisonRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceComparisonRuns_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FaceComparisonRuns_ProcessingJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ProcessingJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FaceComparisonResults",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RunId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ModelId = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ModelName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    ConfigurationJson = table.Column<string>(type: "jsonb", nullable: false),
                    Error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ElapsedMilliseconds = table.Column<double>(type: "double precision", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MissedFaces = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceComparisonResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceComparisonResults_FaceComparisonRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "FaceComparisonRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FaceComparisonDetections",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResultId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ordinal = table.Column<int>(type: "integer", nullable: false),
                    X = table.Column<float>(type: "real", nullable: false),
                    Y = table.Column<float>(type: "real", nullable: false),
                    Width = table.Column<float>(type: "real", nullable: false),
                    Height = table.Column<float>(type: "real", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    LandmarksJson = table.Column<string>(type: "jsonb", nullable: false),
                    WarningsJson = table.Column<string>(type: "jsonb", nullable: false),
                    IsFace = table.Column<bool>(type: "boolean", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceComparisonDetections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceComparisonDetections_FaceComparisonResults_ResultId",
                        column: x => x.ResultId,
                        principalTable: "FaceComparisonResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaceComparisonDetections_ResultId_Ordinal",
                table: "FaceComparisonDetections",
                columns: new[] { "ResultId", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaceComparisonResults_RunId_ModelId",
                table: "FaceComparisonResults",
                columns: new[] { "RunId", "ModelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaceComparisonRuns_AssetId_CreatedAtUtc",
                table: "FaceComparisonRuns",
                columns: new[] { "AssetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_FaceComparisonRuns_JobId",
                table: "FaceComparisonRuns",
                column: "JobId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaceComparisonDetections");

            migrationBuilder.DropTable(
                name: "FaceComparisonResults");

            migrationBuilder.DropTable(
                name: "FaceComparisonRuns");
        }
    }
}
