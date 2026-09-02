using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DevOrchestrator.Infrastructure.Persistence.Migrations;

[DbContext(typeof(OrchestratorDbContext))]
[Migration("202609020001_InitialProductionSchema")]
public sealed class InitialProductionSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "projects",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                Key = table.Column<string>(maxLength: 80, nullable: false),
                Name = table.Column<string>(maxLength: 200, nullable: false),
                RepositoryUrl = table.Column<string>(maxLength: 500, nullable: false),
                DefaultBranch = table.Column<string>(maxLength: 200, nullable: false),
                IsActive = table.Column<bool>(nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_projects", x => x.Id));

        migrationBuilder.CreateTable(
            name: "tasks",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                ProjectId = table.Column<Guid>(nullable: false),
                Code = table.Column<string>(maxLength: 80, nullable: false),
                Title = table.Column<string>(maxLength: 300, nullable: false),
                Objective = table.Column<string>(maxLength: 5000, nullable: false),
                Constraints = table.Column<string>(maxLength: 10000, nullable: false),
                Priority = table.Column<int>(nullable: false),
                Status = table.Column<int>(nullable: false),
                ActiveBranch = table.Column<string>(maxLength: 300, nullable: true),
                LastCommitSha = table.Column<string>(maxLength: 120, nullable: true),
                PullRequestUrl = table.Column<string>(maxLength: 1000, nullable: true),
                BlockReason = table.Column<string>(maxLength: 2000, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(nullable: false),
                Revision = table.Column<long>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_tasks", x => x.Id));

        migrationBuilder.CreateTable(
            name: "github_webhook_deliveries",
            columns: table => new
            {
                DeliveryId = table.Column<string>(maxLength: 120, nullable: false),
                EventName = table.Column<string>(maxLength: 80, nullable: false),
                ReceivedAtUtc = table.Column<DateTime>(nullable: false),
                LeaseExpiresAtUtc = table.Column<DateTime>(nullable: false),
                CompletedAtUtc = table.Column<DateTime>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_github_webhook_deliveries", x => x.DeliveryId));

        migrationBuilder.CreateTable(
            name: "task_acceptance_criteria",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TaskId = table.Column<Guid>(nullable: false),
                Description = table.Column<string>(maxLength: 1000, nullable: false),
                IsSatisfied = table.Column<bool>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_task_acceptance_criteria", x => x.Id);
                table.ForeignKey("FK_task_acceptance_criteria_tasks_TaskId", x => x.TaskId, "tasks", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "task_dependencies",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TaskId = table.Column<Guid>(nullable: false),
                DependsOnTaskId = table.Column<Guid>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_task_dependencies", x => x.Id);
                table.ForeignKey("FK_task_dependencies_tasks_TaskId", x => x.TaskId, "tasks", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "task_evidence",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TaskId = table.Column<Guid>(nullable: false),
                Actor = table.Column<string>(maxLength: 120, nullable: false),
                Branch = table.Column<string>(maxLength: 300, nullable: false),
                CommitSha = table.Column<string>(maxLength: 120, nullable: false),
                PullRequestUrl = table.Column<string>(maxLength: 1000, nullable: true),
                PayloadJson = table.Column<string>(nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_task_evidence", x => x.Id);
                table.ForeignKey("FK_task_evidence_tasks_TaskId", x => x.TaskId, "tasks", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "task_events",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TaskId = table.Column<Guid>(nullable: false),
                EventType = table.Column<string>(maxLength: 120, nullable: false),
                Actor = table.Column<string>(maxLength: 120, nullable: false),
                PayloadJson = table.Column<string>(nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_task_events", x => x.Id);
                table.ForeignKey("FK_task_events_tasks_TaskId", x => x.TaskId, "tasks", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "task_reviews",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false),
                TaskId = table.Column<Guid>(nullable: false),
                Decision = table.Column<int>(nullable: false),
                Actor = table.Column<string>(maxLength: 120, nullable: false),
                Summary = table.Column<string>(maxLength: 5000, nullable: false),
                FindingsJson = table.Column<string>(nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_task_reviews", x => x.Id);
                table.ForeignKey("FK_task_reviews_tasks_TaskId", x => x.TaskId, "tasks", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_projects_Key", "projects", "Key", unique: true);
        migrationBuilder.CreateIndex("IX_tasks_ProjectId_Code", "tasks", new[] { "ProjectId", "Code" }, unique: true);
        migrationBuilder.CreateIndex("IX_tasks_ProjectId_Status_Priority", "tasks", new[] { "ProjectId", "Status", "Priority" });
        migrationBuilder.CreateIndex("IX_github_webhook_deliveries_CompletedAtUtc_LeaseExpiresAtUtc", "github_webhook_deliveries", new[] { "CompletedAtUtc", "LeaseExpiresAtUtc" });
        migrationBuilder.CreateIndex("IX_task_acceptance_criteria_TaskId", "task_acceptance_criteria", "TaskId");
        migrationBuilder.CreateIndex("IX_task_dependencies_DependsOnTaskId", "task_dependencies", "DependsOnTaskId");
        migrationBuilder.CreateIndex("IX_task_dependencies_TaskId_DependsOnTaskId", "task_dependencies", new[] { "TaskId", "DependsOnTaskId" }, unique: true);
        migrationBuilder.CreateIndex("IX_task_evidence_TaskId", "task_evidence", "TaskId");
        migrationBuilder.CreateIndex("IX_task_events_TaskId_CreatedAtUtc", "task_events", new[] { "TaskId", "CreatedAtUtc" });
        migrationBuilder.CreateIndex("IX_task_reviews_TaskId", "task_reviews", "TaskId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("github_webhook_deliveries");
        migrationBuilder.DropTable("task_acceptance_criteria");
        migrationBuilder.DropTable("task_dependencies");
        migrationBuilder.DropTable("task_evidence");
        migrationBuilder.DropTable("task_events");
        migrationBuilder.DropTable("task_reviews");
        migrationBuilder.DropTable("projects");
        migrationBuilder.DropTable("tasks");
    }
}
