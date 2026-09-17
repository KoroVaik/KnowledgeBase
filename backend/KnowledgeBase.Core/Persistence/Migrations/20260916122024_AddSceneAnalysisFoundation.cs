using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSceneAnalysisFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VisualEmbeddings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RunId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Embedding = table.Column<float[]>(type: "real[]", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VisualEmbeddings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VisualEmbeddings_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_VisualEmbeddings_PhotoAnalysisRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PhotoAnalysisRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocationObservations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LocationId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VisualEmbeddingId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceDecisionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ConfirmedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LocationObservations_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationObservations_Locations_LocationId",
                        column: x => x.LocationId,
                        principalTable: "Locations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationObservations_PhotoAnalysisReviewDecisions_SourceDec~",
                        column: x => x.SourceDecisionId,
                        principalTable: "PhotoAnalysisReviewDecisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationObservations_VisualEmbeddings_VisualEmbeddingId",
                        column: x => x.VisualEmbeddingId,
                        principalTable: "VisualEmbeddings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocationObservations_AssetId",
                table: "LocationObservations",
                column: "AssetId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationObservations_LocationId",
                table: "LocationObservations",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationObservations_SourceDecisionId",
                table: "LocationObservations",
                column: "SourceDecisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LocationObservations_VisualEmbeddingId",
                table: "LocationObservations",
                column: "VisualEmbeddingId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VisualEmbeddings_AssetId_CreatedAtUtc",
                table: "VisualEmbeddings",
                columns: new[] { "AssetId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VisualEmbeddings_RunId",
                table: "VisualEmbeddings",
                column: "RunId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LocationObservations");

            migrationBuilder.DropTable(
                name: "VisualEmbeddings");
        }
    }
}
