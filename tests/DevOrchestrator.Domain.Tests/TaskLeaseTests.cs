using DevOrchestrator.Domain.Tasks;

namespace DevOrchestrator.Domain.Tests;

public sealed class TaskLeaseTests
{
    [Fact]
    public void Claim_heartbeat_and_expired_reclaim_preserve_single_worker_ownership()
    {
        var projectId = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 9, 2, 6, 0, 0, TimeSpan.Zero);
        var task = DevelopmentTask.Create(
            projectId,
            "P5-001",
            "Lease task",
            "Prove worker leasing",
            ["Lease works"],
            null,
            TaskPriority.High,
            "architect",
            now);
        task.MarkReady("architect", now);

        task.Claim("implementer", "worker-a", "feature/a", now, TimeSpan.FromMinutes(10));
        Assert.Equal(DevelopmentTaskStatus.InProgress, task.Status);
        Assert.Equal("worker-a", task.LeaseOwner);
        Assert.Equal(now.AddMinutes(10), task.LeaseExpiresAtUtc);

        var heartbeatAt = now.AddMinutes(5);
        task.Heartbeat("implementer", "worker-a", heartbeatAt, TimeSpan.FromMinutes(10));
        Assert.Equal(heartbeatAt, task.LastHeartbeatAtUtc);
        Assert.Equal(now.AddMinutes(15), task.LeaseExpiresAtUtc);

        Assert.Throws<InvalidOperationException>(() =>
            task.Claim("implementer", "worker-b", null, now.AddMinutes(6), TimeSpan.FromMinutes(10)));
        Assert.Throws<InvalidOperationException>(() =>
            task.Heartbeat("implementer", "worker-b", now.AddMinutes(7), TimeSpan.FromMinutes(10)));

        var reclaimAt = now.AddMinutes(16);
        task.Claim("implementer", "worker-b", "feature/b", reclaimAt, TimeSpan.FromMinutes(10));
        Assert.Equal("worker-b", task.LeaseOwner);
        Assert.Equal(reclaimAt.AddMinutes(10), task.LeaseExpiresAtUtc);
        Assert.Equal("feature/b", task.ActiveBranch);
    }

    [Fact]
    public void Manual_expiry_makes_live_worker_lease_immediately_reclaimable()
    {
        var now = new DateTimeOffset(2026, 9, 2, 7, 0, 0, TimeSpan.Zero);
        var task = DevelopmentTask.Create(
            Guid.NewGuid(), "P6-OPS", "Operations", "Recover dead worker", ["Reclaim works"], null,
            TaskPriority.High, "architect", now);
        task.MarkReady("architect", now);
        task.Claim("implementer", "worker-dead", "feature/dead", now, TimeSpan.FromMinutes(10));

        var releaseAt = now.AddMinutes(1);
        task.ExpireLease("mcp:auditor", "worker process terminated", releaseAt);

        Assert.True(task.IsClaimable(releaseAt));
        Assert.Equal("worker-dead", task.LeaseOwner);
        Assert.Equal(releaseAt, task.LeaseExpiresAtUtc);

        task.Claim("implementer", "worker-recovery", "feature/recovery", releaseAt, TimeSpan.FromMinutes(10));
        Assert.Equal("worker-recovery", task.LeaseOwner);
        Assert.Equal("feature/recovery", task.ActiveBranch);
    }

    [Fact]
    public void Submit_for_review_releases_worker_lease()
    {
        var now = DateTimeOffset.UtcNow;
        var task = DevelopmentTask.Create(
            Guid.NewGuid(), "P5-002", "Release", "Release lease", ["Done"], null,
            TaskPriority.Normal, "architect", now);
        task.MarkReady("architect", now);
        task.Claim("implementer", "worker-a", "feature/a", now, TimeSpan.FromMinutes(10));
        task.AddEvidence("implementer", "feature/a", "abc123", null, "{}", now.AddMinutes(1));

        task.SubmitForReview("implementer", now.AddMinutes(2));

        Assert.Equal(DevelopmentTaskStatus.ReadyForReview, task.Status);
        Assert.Null(task.LeaseOwner);
        Assert.Null(task.LeaseExpiresAtUtc);
        Assert.Null(task.LastHeartbeatAtUtc);
    }
}
