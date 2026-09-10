using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeBase.Core.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddJobKindAndPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessingJobs_AssetId",
                table: "ProcessingJobs");

            migrationBuilder.AlterColumn<string>(
                name: "AssetId",
                table: "ProcessingJobs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ProcessingJobs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "BuildSourceNote");

            // Every job that exists today turns a file into a note. The default was only there
            // to fill them in; the model has none, so the column must not keep one.
            migrationBuilder.Sql("""ALTER TABLE "ProcessingJobs" ALTER COLUMN "Kind" DROP DEFAULT;""");

            migrationBuilder.AddColumn<string>(
                name: "Payload",
                table: "ProcessingJobs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_AssetId",
                table: "ProcessingJobs",
                column: "AssetId",
                unique: true,
                filter: "\"AssetId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProcessingJobs_AssetId",
                table: "ProcessingJobs");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ProcessingJobs");

            migrationBuilder.DropColumn(
                name: "Payload",
                table: "ProcessingJobs");

            migrationBuilder.AlterColumn<string>(
                name: "AssetId",
                table: "ProcessingJobs",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessingJobs_AssetId",
                table: "ProcessingJobs",
                column: "AssetId",
                unique: true);
        }
    }
}
