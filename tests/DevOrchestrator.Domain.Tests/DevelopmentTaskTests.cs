using DevOrchestrator.Domain.Tasks;

namespace DevOrchestrator.Domain.Tests;

public sealed class DevelopmentTaskTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 2, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_task_requires_acceptance_criteria()
    {
        Assert.Throws<ArgumentException>(() =>
            DevelopmentTask.Create(
                Guid.NewGuid(),
                "T-001",
                "Test",
                "Objective",
                [],
                null,
                TaskPriority.Normal,
                "architect",
                Now));
    }

    [Fact]
    public void Implementer_cannot_submit_without_evidence()
    {
        var task = CreateReadyTask();
        task.Start("codex", "codex/T-001", Now.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() =>
            task.SubmitForReview("codex", Now.AddMinutes(2)));
    }

    [Fact]
    public void Changes_requested_returns_task_to_implementer_queue_state()
    {
        var task = CreateReadyTask();
        task.Start("codex", "codex/T-001", Now.AddMinutes(1));
        task.AddEvidence(
            "codex",
            "codex/T-001",
            "abc123",
            null,
            "{}",
            Now.AddMinutes(2));
        task.SubmitForReview("codex", Now.AddMinutes(3));

        task.ApplyReview(
            ReviewDecision.ChangesRequested,
            "chatgpt-auditor",
            "Needs changes",
            "[\"Fix architecture rule\"]",
            true,
            Now.AddMinutes(4));

        Assert.Equal(DevelopmentTaskStatus.ChangesRequested, task.Status);
        Assert.All(task.AcceptanceCriteria, criterion => Assert.False(criterion.IsSatisfied));
    }

    [Fact]
    public void Passing_review_completes_task_and_satisfies_criteria()
    {
        var task = CreateReadyTask();
        task.Start("codex", "codex/T-001", Now.AddMinutes(1));
        task.AddEvidence(
            "codex",
            "codex/T-001",
            "abc123",
            "https://github.com/example/repo/pull/1",
            "{}",
            Now.AddMinutes(2));
        task.SubmitForReview("codex", Now.AddMinutes(3));

        task.ApplyReview(
            ReviewDecision.Pass,
            "chatgpt-auditor",
            "All checks passed",
            "[]",
            true,
            Now.AddMinutes(4));

        Assert.Equal(DevelopmentTaskStatus.Done, task.Status);
        Assert.All(task.AcceptanceCriteria, criterion => Assert.True(criterion.IsSatisfied));
    }

    private static DevelopmentTask CreateReadyTask()
    {
        var task = DevelopmentTask.Create(
            Guid.NewGuid(),
            "T-001",
            "Implement one thing",
            "Implement a small isolated change.",
            ["Build passes", "Tests pass"],
            ["Do not change unrelated behavior"],
            TaskPriority.Normal,
            "chatgpt-architect",
            Now);

        task.MarkReady("chatgpt-architect", Now);
        return task;
    }
}
