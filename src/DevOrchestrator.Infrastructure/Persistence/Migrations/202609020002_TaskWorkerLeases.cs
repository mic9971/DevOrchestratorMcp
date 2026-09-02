using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevOrchestrator.Infrastructure.Persistence.Migrations;

[DbContext(typeof(OrchestratorDbContext))]
[Migration("202609020002_TaskWorkerLeases")]
public sealed class TaskWorkerLeases : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "LeaseOwner",
            table: "tasks",
            maxLength: 120,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LeaseExpiresAtUtc",
            table: "tasks",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastHeartbeatAtUtc",
            table: "tasks",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_tasks_ProjectId_Status_LeaseExpiresAtUtc",
            table: "tasks",
            columns: new[] { "ProjectId", "Status", "LeaseExpiresAtUtc" });

        migrationBuilder.Sql(
            "UPDATE tasks SET \"LeaseExpiresAtUtc\" = \"UpdatedAtUtc\" WHERE \"Status\" = 2 AND \"LeaseExpiresAtUtc\" IS NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_tasks_ProjectId_Status_LeaseExpiresAtUtc",
            table: "tasks");
        migrationBuilder.DropColumn(name: "LeaseOwner", table: "tasks");
        migrationBuilder.DropColumn(name: "LeaseExpiresAtUtc", table: "tasks");
        migrationBuilder.DropColumn(name: "LastHeartbeatAtUtc", table: "tasks");
    }
}
