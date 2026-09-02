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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TargetProject>(entity =>
        {
            entity.ToTable("projects");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Key).IsUnique();
            entity.Property(x => x.Key).HasMaxLength(80);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.RepositoryUrl).HasMaxLength(500);
            entity.Property(x => x.DefaultBranch).HasMaxLength(200);
        });

        modelBuilder.Entity<DevelopmentTask>(entity =>
        {
            entity.ToTable("tasks");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.ProjectId, x.Code }).IsUnique();
            entity.HasIndex(x => new { x.ProjectId, x.Status, x.Priority });
            entity.Property(x => x.Code).HasMaxLength(80);
            entity.Property(x => x.Title).HasMaxLength(300);
            entity.Property(x => x.Objective).HasMaxLength(5000);
            entity.Property(x => x.Constraints).HasMaxLength(10000);
            entity.Property(x => x.ActiveBranch).HasMaxLength(300);
            entity.Property(x => x.LastCommitSha).HasMaxLength(120);
            entity.Property(x => x.PullRequestUrl).HasMaxLength(1000);
            entity.Property(x => x.BlockReason).HasMaxLength(2000);
            entity.Property(x => x.Revision).IsConcurrencyToken();

            entity.HasMany(x => x.AcceptanceCriteria)
                .WithOne()
                .HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Dependencies)
                .WithOne()
                .HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Evidence)
                .WithOne()
                .HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Reviews)
                .WithOne()
                .HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(x => x.Events)
                .WithOne()
                .HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.Metadata.FindNavigation(nameof(DevelopmentTask.AcceptanceCriteria))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);

            entity.Metadata.FindNavigation(nameof(DevelopmentTask.Dependencies))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);

            entity.Metadata.FindNavigation(nameof(DevelopmentTask.Evidence))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);

            entity.Metadata.FindNavigation(nameof(DevelopmentTask.Reviews))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);

            entity.Metadata.FindNavigation(nameof(DevelopmentTask.Events))!
                .SetPropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<AcceptanceCriterion>(entity =>
        {
            entity.ToTable("task_acceptance_criteria");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Description).HasMaxLength(1000);
        });

        modelBuilder.Entity<TaskDependency>(entity =>
        {
            entity.ToTable("task_dependencies");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TaskId, x.DependsOnTaskId }).IsUnique();
            entity.HasIndex(x => x.DependsOnTaskId);
        });

        modelBuilder.Entity<TaskEvidence>(entity =>
        {
            entity.ToTable("task_evidence");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Actor).HasMaxLength(120);
            entity.Property(x => x.Branch).HasMaxLength(300);
            entity.Property(x => x.CommitSha).HasMaxLength(120);
            entity.Property(x => x.PullRequestUrl).HasMaxLength(1000);
        });

        modelBuilder.Entity<TaskReview>(entity =>
        {
            entity.ToTable("task_reviews");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.Property(x => x.Actor).HasMaxLength(120);
            entity.Property(x => x.Summary).HasMaxLength(5000);
        });

        modelBuilder.Entity<TaskEvent>(entity =>
        {
            entity.ToTable("task_events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedOnAdd();
            entity.HasIndex(x => new { x.TaskId, x.CreatedAtUtc });
            entity.Property(x => x.EventType).HasMaxLength(120);
            entity.Property(x => x.Actor).HasMaxLength(120);
        });

        modelBuilder.Entity<GitHubWebhookDelivery>(entity =>
        {
            entity.ToTable("github_webhook_deliveries");
            entity.HasKey(x => x.DeliveryId);
            entity.Property(x => x.DeliveryId).HasMaxLength(120);
            entity.Property(x => x.EventName).HasMaxLength(80);
            entity.HasIndex(x => new { x.CompletedAtUtc, x.LeaseExpiresAtUtc });
        });
    }
}
