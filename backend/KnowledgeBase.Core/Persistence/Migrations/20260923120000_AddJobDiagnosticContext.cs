using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace KnowledgeBase.Core.Persistence.Migrations;

[DbContext(typeof(KnowledgeBaseDbContext))]
[Migration("20260923120000_AddJobDiagnosticContext")]
public sealed class AddJobDiagnosticContext : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "DiagnosticContext", table: "ProcessingJobs", type: "character varying(2000)", maxLength: 2000, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "DiagnosticContext", table: "ProcessingJobs");
}
