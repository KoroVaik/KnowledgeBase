using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEventCandidateFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventClusteringRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PipelineVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ModelKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ConfigurationHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventClusteringRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EventClusters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RunId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    SignalsJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventClusters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventClusters_EventClusteringRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "EventClusteringRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventCandidates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ClusterId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SuggestedOccurredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventCandidates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventCandidates_EventClusters_ClusterId",
                        column: x => x.ClusterId,
                        principalTable: "EventClusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventClusterPhotos",
                columns: table => new
                {
                    ClusterId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventClusterPhotos", x => new { x.ClusterId, x.AssetId });
                    table.ForeignKey(
                        name: "FK_EventClusterPhotos_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventClusterPhotos_EventClusters_ClusterId",
                        column: x => x.ClusterId,
                        principalTable: "EventClusters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EventCandidateReviewDecisions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CandidateId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ChosenEventId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SelectedAssetIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventCandidateReviewDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventCandidateReviewDecisions_EventCandidates_CandidateId",
                        column: x => x.CandidateId,
                        principalTable: "EventCandidates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventCandidateReviewDecisions_CandidateId",
                table: "EventCandidateReviewDecisions",
                column: "CandidateId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventCandidates_ClusterId_CreatedAtUtc",
                table: "EventCandidates",
                columns: new[] { "ClusterId", "CreatedAtUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventCandidates_SupersededAtUtc_CreatedAtUtc",
                table: "EventCandidates",
                columns: new[] { "SupersededAtUtc", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EventClusteringRuns_CompletedAtUtc",
                table: "EventClusteringRuns",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_EventClusterPhotos_AssetId",
                table: "EventClusterPhotos",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_EventClusters_RunId_Score",
                table: "EventClusters",
                columns: new[] { "RunId", "Score" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventCandidateReviewDecisions");

            migrationBuilder.DropTable(
                name: "EventClusterPhotos");

            migrationBuilder.DropTable(
                name: "EventCandidates");

            migrationBuilder.DropTable(
                name: "EventClusters");

            migrationBuilder.DropTable(
                name: "EventClusteringRuns");
        }
    }
}
