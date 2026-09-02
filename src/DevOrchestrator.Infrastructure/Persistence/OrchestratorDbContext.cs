using DevOrchestrator.Domain.Projects;
using DevOrchestrator.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DevOrchestrator.Infrastructure.Persistence;

public sealed class OrchestratorDbContext(DbContextOptions<OrchestratorDbContext> options)
    : DbContext(options)
{
    public DbSet<TargetProject> Projects => Set<TargetProject>();
    public DbSet<DevelopmentTask> DevelopmentTasks => Set<DevelopmentTask>();
    public DbSet<AcceptanceCriterion> AcceptanceCriteria => Set<AcceptanceCriterion>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<TaskEvidence> TaskEvidence => Set<TaskEvidence>();
    public DbSet<TaskReview> TaskReviews => Set<TaskReview>();
    public DbSet<TaskEvent> TaskEvents => Set<TaskEvent>();
    public DbSet<GitHubWebhookDelivery> GitHubWebhookDeliveries => Set<GitHubWebhookDelivery>();
    public DbSet<GitHubWebhookInboxItem> GitHubWebhookInbox => Set<GitHubWebhookInboxItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => OrchestratorModel.Configure(modelBuilder);
}
