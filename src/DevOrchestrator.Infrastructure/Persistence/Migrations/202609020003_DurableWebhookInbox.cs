using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevOrchestrator.Infrastructure.Persistence.Migrations;

[DbContext(typeof(OrchestratorDbContext))]
[Migration("202609020003_DurableWebhookInbox")]
public sealed class DurableWebhookInbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "github_webhook_inbox",
            columns: table => new
            {
                DeliveryId = table.Column<string>(maxLength: 120, nullable: false),
                EventName = table.Column<string>(maxLength: 80, nullable: false),
                Action = table.Column<string>(maxLength: 80, nullable: false),
                RepositoryUrl = table.Column<string>(maxLength: 500, nullable: false),
                IssueNumber = table.Column<int>(nullable: false),
                AttemptCount = table.Column<int>(nullable: false),
                ReceivedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                NextAttemptAtUtc = table.Column<DateTimeOffset>(nullable: false),
                LeaseExpiresAtUtc = table.Column<DateTimeOffset>(nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(nullable: true),
                LastError = table.Column<string>(maxLength: 4000, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_github_webhook_inbox", x => x.DeliveryId));

        migrationBuilder.CreateIndex(
            name: "IX_github_webhook_inbox_CompletedAtUtc_NextAttemptAtUtc_LeaseExpiresAtUtc",
            table: "github_webhook_inbox",
            columns: new[] { "CompletedAtUtc", "NextAttemptAtUtc", "LeaseExpiresAtUtc" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropTable(name: "github_webhook_inbox");
}
