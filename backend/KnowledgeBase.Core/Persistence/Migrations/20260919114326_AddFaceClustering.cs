using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFaceClustering : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FaceClusterId",
                table: "PhotoAnalysisCandidates",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FaceClusteringRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PipelineVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConfigurationHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceClusteringRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IgnoredFaceGroups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgnoredFaceGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FaceClusters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RunId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PersonId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    HintPersonId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    HintScore = table.Column<double>(type: "double precision", nullable: true),
                    IgnoredGroupId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaceClusters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FaceClusters_FaceClusteringRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "FaceClusteringRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FaceClusters_IgnoredFaceGroups_IgnoredGroupId",
                        column: x => x.IgnoredGroupId,
                        principalTable: "IgnoredFaceGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnalysisCandidates_FaceClusterId",
                table: "PhotoAnalysisCandidates",
                column: "FaceClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_FaceClusteringRuns_CompletedAtUtc",
                table: "FaceClusteringRuns",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FaceClusters_IgnoredGroupId",
                table: "FaceClusters",
                column: "IgnoredGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_FaceClusters_RunId_Kind",
                table: "FaceClusters",
                columns: new[] { "RunId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_IgnoredFaceGroups_CreatedAtUtc",
                table: "IgnoredFaceGroups",
                column: "CreatedAtUtc");

            migrationBuilder.AddForeignKey(
                name: "FK_PhotoAnalysisCandidates_FaceClusters_FaceClusterId",
                table: "PhotoAnalysisCandidates",
                column: "FaceClusterId",
                principalTable: "FaceClusters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Before clustering, Rejected/Merged on a person proposal meant "close this face"; now Rejected
            // means "not this person, keep it in Unsorted". Faces closed the old way move into one ignored
            // group instead: a superseded copy of their decided proposal carries the Ignored decision, so
            // every candidate keeps at most one decision and the original stays in the audit trail.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT o."IdentityId", d."Kind", c."Id" AS "CandidateId", d."Id" AS "DecisionId",
                           row_number() OVER (PARTITION BY o."IdentityId" ORDER BY d."DecidedAtUtc" DESC, d."Id" DESC) AS "Position"
                    FROM "PhotoAnalysisReviewDecisions" d
                    JOIN "PhotoAnalysisCandidates" c ON c."Id" = d."CandidateId"
                    JOIN "FaceOccurrences" o ON o."Id" = c."SubjectFaceOccurrenceId"
                    WHERE c."Kind" = 'Person' AND o."IdentityId" IS NOT NULL
                ),
                closed AS MATERIALIZED (
                    SELECT ranked."CandidateId", ranked."DecisionId",
                           replace(gen_random_uuid()::text, '-', '') AS "CopyId",
                           replace(gen_random_uuid()::text, '-', '') AS "IgnoredDecisionId"
                    FROM ranked
                    WHERE ranked."Position" = 1 AND ranked."Kind" IN ('Rejected', 'Merged')
                      AND NOT EXISTS (
                          SELECT 1 FROM "PersonReferenceFaces" r
                          JOIN "FaceOccurrences" ro ON ro."Id" = r."FaceOccurrenceId"
                          WHERE ro."IdentityId" = ranked."IdentityId")
                ),
                ignored_group AS (
                    INSERT INTO "IgnoredFaceGroups" ("Id", "CreatedAtUtc")
                    SELECT replace(gen_random_uuid()::text, '-', ''), now()
                    WHERE EXISTS (SELECT 1 FROM closed)
                    RETURNING "Id"
                ),
                copies AS (
                    INSERT INTO "PhotoAnalysisCandidates"
                        ("Id", "RunId", "Kind", "SubjectAssetId", "SubjectFaceOccurrenceId", "ProposedTargetId", "ProposedLabel",
                         "Rank", "Score", "SignalsJson", "CreatedAtUtc", "SupersededAtUtc")
                    SELECT closed."CopyId", c."RunId", c."Kind", c."SubjectAssetId", c."SubjectFaceOccurrenceId", NULL, 'Ignored face',
                           c."Rank", c."Score", jsonb_build_object('migratedFromDecisionId', closed."DecisionId"), now(), now()
                    FROM closed
                    JOIN "PhotoAnalysisCandidates" c ON c."Id" = closed."CandidateId"
                    RETURNING "Id"
                )
                INSERT INTO "PhotoAnalysisReviewDecisions" ("Id", "CandidateId", "Kind", "ChosenTargetId", "Note", "DecidedAtUtc")
                SELECT closed."IgnoredDecisionId", copies."Id", 'Ignored', (SELECT "Id" FROM ignored_group),
                       'Migrated: closed before face clustering existed.', now()
                FROM closed
                JOIN copies ON copies."Id" = closed."CopyId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "PhotoAnalysisReviewDecisions" WHERE "Kind" = 'Ignored';
                DELETE FROM "PhotoAnalysisCandidates" WHERE "SignalsJson" ? 'migratedFromDecisionId';
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_PhotoAnalysisCandidates_FaceClusters_FaceClusterId",
                table: "PhotoAnalysisCandidates");

            migrationBuilder.DropTable(
                name: "FaceClusters");

            migrationBuilder.DropTable(
                name: "FaceClusteringRuns");

            migrationBuilder.DropTable(
                name: "IgnoredFaceGroups");

            migrationBuilder.DropIndex(
                name: "IX_PhotoAnalysisCandidates_FaceClusterId",
                table: "PhotoAnalysisCandidates");

            migrationBuilder.DropColumn(
                name: "FaceClusterId",
                table: "PhotoAnalysisCandidates");
        }
    }
}
