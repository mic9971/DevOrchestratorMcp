using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevOrchestrator.Infrastructure.Persistence.Migrations;

[DbContext(typeof(OrchestratorDbContext))]
[Migration("202609030001_WebhookDeadLetter")]
public sealed class WebhookDeadLetter : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "DeadLetteredAtUtc",
            table: "github_webhook_inbox",
            nullable: true);

        migrationBuilder.DropIndex(
            name: "IX_github_webhook_inbox_CompletedAtUtc_NextAttemptAtUtc_LeaseExpiresAtUtc",
            table: "github_webhook_inbox");

        migrationBuilder.CreateIndex(
            name: "IX_github_webhook_inbox_CompletedAtUtc_DeadLetteredAtUtc_NextAttemptAtUtc_LeaseExpiresAtUtc",
            table: "github_webhook_inbox",
            columns: new[] { "CompletedAtUtc", "DeadLetteredAtUtc", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_github_webhook_inbox_CompletedAtUtc_DeadLetteredAtUtc_NextAttemptAtUtc_LeaseExpiresAtUtc",
            table: "github_webhook_inbox");

        migrationBuilder.DropColumn(
            name: "DeadLetteredAtUtc",
            table: "github_webhook_inbox");

        migrationBuilder.CreateIndex(
            name: "IX_github_webhook_inbox_CompletedAtUtc_NextAttemptAtUtc_LeaseExpiresAtUtc",
            table: "github_webhook_inbox",
            columns: new[] { "CompletedAtUtc", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });
    }
}
