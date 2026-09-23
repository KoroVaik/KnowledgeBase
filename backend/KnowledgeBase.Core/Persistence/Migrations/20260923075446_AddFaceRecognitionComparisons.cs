using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceRecognitionComparisons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FaceRecognitionComparisonRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    JobId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReferenceFaceCount = table.Column<int>(type: "integer", nullable: false),
                    PersonCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceRecognitionComparisonRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceRecognitionComparisonRuns_ProcessingJobs_JobId",
                        column: x => x.JobId,
                        principalTable: "ProcessingJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FaceRecognitionComparisonPairs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RunId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FirstFaceOccurrenceId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SecondFaceOccurrenceId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsSamePerson = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceRecognitionComparisonPairs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceRecognitionComparisonPairs_FaceRecognitionComparisonRun~",
                        column: x => x.RunId,
                        principalTable: "FaceRecognitionComparisonRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FaceRecognitionComparisonResults",
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
                    Threshold = table.Column<double>(type: "double precision", nullable: true),
                    SamePersonPairs = table.Column<int>(type: "integer", nullable: false),
                    DifferentPersonPairs = table.Column<int>(type: "integer", nullable: false),
                    TruePositives = table.Column<int>(type: "integer", nullable: false),
                    FalsePositives = table.Column<int>(type: "integer", nullable: false),
                    TrueNegatives = table.Column<int>(type: "integer", nullable: false),
                    FalseNegatives = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceRecognitionComparisonResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceRecognitionComparisonResults_FaceRecognitionComparisonR~",
                        column: x => x.RunId,
                        principalTable: "FaceRecognitionComparisonRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FaceRecognitionComparisonScores",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PairId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ResultId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    IsMatch = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceRecognitionComparisonScores", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceRecognitionComparisonScores_FaceRecognitionComparisonPa~",
                        column: x => x.PairId,
                        principalTable: "FaceRecognitionComparisonPairs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FaceRecognitionComparisonScores_FaceRecognitionComparisonRe~",
                        column: x => x.ResultId,
                        principalTable: "FaceRecognitionComparisonResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaceRecognitionComparisonPairs_RunId_FirstFaceOccurrenceId_~",
                table: "FaceRecognitionComparisonPairs",
                columns: new[] { "RunId", "FirstFaceOccurrenceId", "SecondFaceOccurrenceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaceRecognitionComparisonResults_RunId_ModelId",
                table: "FaceRecognitionComparisonResults",
                columns: new[] { "RunId", "ModelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaceRecognitionComparisonRuns_CreatedAtUtc",
                table: "FaceRecognitionComparisonRuns",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FaceRecognitionComparisonRuns_JobId",
                table: "FaceRecognitionComparisonRuns",
                column: "JobId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaceRecognitionComparisonScores_PairId_ResultId",
                table: "FaceRecognitionComparisonScores",
                columns: new[] { "PairId", "ResultId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FaceRecognitionComparisonScores_ResultId",
                table: "FaceRecognitionComparisonScores",
                column: "ResultId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaceRecognitionComparisonScores");

            migrationBuilder.DropTable(
                name: "FaceRecognitionComparisonPairs");

            migrationBuilder.DropTable(
                name: "FaceRecognitionComparisonResults");

            migrationBuilder.DropTable(
                name: "FaceRecognitionComparisonRuns");
        }
    }
}
