using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSceneClusteringAndIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SceneIdentities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AssetId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SceneIdentities_Assets_AssetId",
                        column: x => x.AssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SceneIdentities_AssetId",
                table: "SceneIdentities",
                column: "AssetId",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO "SceneIdentities" ("Id", "AssetId", "CreatedAtUtc")
                SELECT md5(random()::text || clock_timestamp()::text)::varchar(32), "AssetId", MIN("CreatedAtUtc")
                FROM "VisualEmbeddings"
                GROUP BY "AssetId"
                ON CONFLICT ("AssetId") DO NOTHING;
            """);

            migrationBuilder.AddColumn<string>(
                name: "SceneIdentityId",
                table: "VisualEmbeddings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "VisualEmbeddings" ve
                SET "SceneIdentityId" = si."Id"
                FROM "SceneIdentities" si
                WHERE ve."AssetId" = si."AssetId";
            """);

            migrationBuilder.AlterColumn<string>(
                name: "SceneIdentityId",
                table: "VisualEmbeddings",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "SceneClusterId",
                table: "PhotoAnalysisCandidates",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExcludedSceneGroups",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcludedSceneGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SceneClusteringRuns",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PipelineVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConfigurationHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneClusteringRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SceneClusters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RunId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LocationId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    HintLocationId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    HintScore = table.Column<double>(type: "double precision", nullable: true),
                    ExcludedGroupId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SceneClusters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SceneClusters_ExcludedSceneGroups_ExcludedGroupId",
                        column: x => x.ExcludedGroupId,
                        principalTable: "ExcludedSceneGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SceneClusters_SceneClusteringRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "SceneClusteringRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VisualEmbeddings_SceneIdentityId",
                table: "VisualEmbeddings",
                column: "SceneIdentityId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoAnalysisCandidates_SceneClusterId",
                table: "PhotoAnalysisCandidates",
                column: "SceneClusterId");

            migrationBuilder.CreateIndex(
                name: "IX_ExcludedSceneGroups_CreatedAtUtc",
                table: "ExcludedSceneGroups",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SceneClusteringRuns_CompletedAtUtc",
                table: "SceneClusteringRuns",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_SceneClusters_ExcludedGroupId",
                table: "SceneClusters",
                column: "ExcludedGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_SceneClusters_RunId_Kind",
                table: "SceneClusters",
                columns: new[] { "RunId", "Kind" });

            migrationBuilder.AddForeignKey(
                name: "FK_PhotoAnalysisCandidates_SceneClusters_SceneClusterId",
                table: "PhotoAnalysisCandidates",
                column: "SceneClusterId",
                principalTable: "SceneClusters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_VisualEmbeddings_SceneIdentities_SceneIdentityId",
                table: "VisualEmbeddings",
                column: "SceneIdentityId",
                principalTable: "SceneIdentities",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PhotoAnalysisCandidates_SceneClusters_SceneClusterId",
                table: "PhotoAnalysisCandidates");

            migrationBuilder.DropForeignKey(
                name: "FK_VisualEmbeddings_SceneIdentities_SceneIdentityId",
                table: "VisualEmbeddings");

            migrationBuilder.DropTable(
                name: "SceneClusters");

            migrationBuilder.DropTable(
                name: "SceneIdentities");

            migrationBuilder.DropTable(
                name: "ExcludedSceneGroups");

            migrationBuilder.DropTable(
                name: "SceneClusteringRuns");

            migrationBuilder.DropIndex(
                name: "IX_VisualEmbeddings_SceneIdentityId",
                table: "VisualEmbeddings");

            migrationBuilder.DropIndex(
                name: "IX_PhotoAnalysisCandidates_SceneClusterId",
                table: "PhotoAnalysisCandidates");

            migrationBuilder.DropColumn(
                name: "SceneIdentityId",
                table: "VisualEmbeddings");

            migrationBuilder.DropColumn(
                name: "SceneClusterId",
                table: "PhotoAnalysisCandidates");
        }
    }
}
