using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSceneObservationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SceneObservations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RunId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SubjectPersonId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    SubjectPersonName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    RelatedPersonId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    RelatedPersonName = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Evidence = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneObservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SceneObservations_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SceneObservations_People_RelatedPersonId",
                        column: x => x.RelatedPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SceneObservations_People_SubjectPersonId",
                        column: x => x.SubjectPersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SceneObservations_PhotoAnalysisRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "PhotoAnalysisRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SceneObservationReviewDecisions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ObservationId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneObservationReviewDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SceneObservationReviewDecisions_SceneObservations_Observati~",
                        column: x => x.ObservationId,
                        principalTable: "SceneObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SceneObservationReviewDecisions_ObservationId",
                table: "SceneObservationReviewDecisions",
                column: "ObservationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SceneObservations_AssetId_SupersededAtUtc",
                table: "SceneObservations",
                columns: new[] { "AssetId", "SupersededAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SceneObservations_RelatedPersonId",
                table: "SceneObservations",
                column: "RelatedPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_SceneObservations_RunId",
                table: "SceneObservations",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_SceneObservations_SubjectPersonId",
                table: "SceneObservations",
                column: "SubjectPersonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SceneObservationReviewDecisions");

            migrationBuilder.DropTable(
                name: "SceneObservations");
        }
    }
}
