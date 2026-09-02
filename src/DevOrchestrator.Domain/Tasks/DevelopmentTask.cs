using System.Text.Json;
using DevOrchestrator.Common;

namespace DevOrchestrator.Domain.Tasks;

public sealed class DevelopmentTask
{
    private static readonly TimeSpan CompatibilityLeaseDuration = TimeSpan.FromMinutes(15);
    private readonly List<AcceptanceCriterion> _acceptanceCriteria = [];
    private readonly List<TaskDependency> _dependencies = [];
    private readonly List<TaskEvidence> _evidence = [];
    private readonly List<TaskReview> _reviews = [];
    private readonly List<TaskEvent> _events = [];

    private DevelopmentTask()
    {
    }

    private DevelopmentTask(
        Guid id,
        Guid projectId,
        string code,
        string title,
        string objective,
        string constraints,
        TaskPriority priority,
        DateTimeOffset now)
    {
        Id = id;
        ProjectId = projectId;
        Code = code;
        Title = title;
        Objective = objective;
        Constraints = constraints;
        Priority = priority;
        Status = DevelopmentTaskStatus.Draft;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        Revision = 0;
    }

    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Objective { get; private set; } = string.Empty;
    public string Constraints { get; private set; } = string.Empty;
    public TaskPriority Priority { get; private set; }
    public DevelopmentTaskStatus Status { get; private set; }
    public string? ActiveBranch { get; private set; }
    public string? LastCommitSha { get; private set; }
    public string? PullRequestUrl { get; private set; }
    public string? BlockReason { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTimeOffset? LeaseExpiresAtUtc { get; private set; }
    public DateTimeOffset? LastHeartbeatAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public long Revision { get; private set; }

    public IReadOnlyCollection<AcceptanceCriterion> AcceptanceCriteria => _acceptanceCriteria;
    public IReadOnlyCollection<TaskDependency> Dependencies => _dependencies;
    public IReadOnlyCollection<TaskEvidence> Evidence => _evidence;
    public IReadOnlyCollection<TaskReview> Reviews => _reviews;
    public IReadOnlyCollection<TaskEvent> Events => _events;

    public static DevelopmentTask Create(
        Guid projectId,
        string code,
        string title,
        string objective,
        IEnumerable<string> acceptanceCriteria,
        IEnumerable<string>? constraints,
        TaskPriority priority,
        string actor,
        DateTimeOffset now)
    {
        code = Guard.NotBlank(code, nameof(code), 80).ToUpperInvariant();
        title = Guard.NotBlank(title, nameof(title), 300);
        objective = Guard.NotBlank(objective, nameof(objective), 5000);
        actor = Guard.NotBlank(actor, nameof(actor), 120);

        var criteria = acceptanceCriteria
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (criteria.Length == 0)
        {
            throw new ArgumentException("At least one acceptance criterion is required.", nameof(acceptanceCriteria));
        }

        var constraintText = constraints is null
            ? string.Empty
            : string.Join("\n", constraints.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()));

        var task = new DevelopmentTask(
            Guid.NewGuid(),
            projectId,
            code,
            title,
            objective,
            constraintText,
            priority,
            now);

        foreach (var criterion in criteria)
        {
            task._acceptanceCriteria.Add(new AcceptanceCriterion(task.Id, criterion));
        }

        task.AddEvent("task.created", actor, "{}", now);
        return task;
    }

    public void AddDependency(Guid dependsOnTaskId)
    {
        if (dependsOnTaskId == Id)
        {
            throw new InvalidOperationException("A task cannot depend on itself.");
        }

        if (_dependencies.Any(x => x.DependsOnTaskId == dependsOnTaskId))
        {
            return;
        }

        _dependencies.Add(new TaskDependency(Id, dependsOnTaskId));
    }

    public void MarkReady(string actor, DateTimeOffset now)
    {
        EnsureStatus(DevelopmentTaskStatus.Draft);
        ClearLease();
        Status = DevelopmentTaskStatus.Ready;
        Touch(now);
        AddEvent("task.ready", actor, "{}", now);
    }

    public void Start(string actor, string? branch, DateTimeOffset now)
        => Claim(actor, actor, branch, now, CompatibilityLeaseDuration);

    public void Claim(
        string actor,
        string workerId,
        string? branch,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        actor = Guard.NotBlank(actor, nameof(actor), 120);
        workerId = Guard.NotBlank(workerId, nameof(workerId), 120);
        ValidateLeaseDuration(leaseDuration);

        var reclaiming = Status == DevelopmentTaskStatus.InProgress;
        if (reclaiming)
        {
            if (!LeaseExpiresAtUtc.HasValue || LeaseExpiresAtUtc.Value > now)
            {
                throw new InvalidOperationException(
                    $"Task {Code} is already leased by '{LeaseOwner ?? "unknown"}' until {LeaseExpiresAtUtc:O}.");
            }
        }
        else if (Status is not (DevelopmentTaskStatus.Ready or DevelopmentTaskStatus.ChangesRequested))
        {
            throw new InvalidOperationException($"Task {Code} cannot start from status {Status}.");
        }

        Status = DevelopmentTaskStatus.InProgress;
        ActiveBranch = string.IsNullOrWhiteSpace(branch) ? ActiveBranch : branch.Trim();
        BlockReason = null;
        LeaseOwner = workerId;
        LastHeartbeatAtUtc = now;
        LeaseExpiresAtUtc = now.Add(leaseDuration);
        Touch(now);
        AddEvent(
            reclaiming ? "task.reclaimed" : "task.claimed",
            actor,
            JsonSerializer.Serialize(new { workerId, leaseExpiresAtUtc = LeaseExpiresAtUtc }),
            now);
    }

    public void Heartbeat(
        string actor,
        string workerId,
        DateTimeOffset now,
        TimeSpan leaseDuration)
    {
        EnsureStatus(DevelopmentTaskStatus.InProgress);
        workerId = Guard.NotBlank(workerId, nameof(workerId), 120);
        ValidateLeaseDuration(leaseDuration);

        if (!string.Equals(LeaseOwner, workerId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Task {Code} is leased by '{LeaseOwner ?? "unknown"}', not '{workerId}'.");
        }

        if (!LeaseExpiresAtUtc.HasValue || LeaseExpiresAtUtc.Value <= now)
        {
            throw new InvalidOperationException(
                $"Task {Code} lease has expired and must be reclaimed before heartbeat.");
        }

        LastHeartbeatAtUtc = now;
        LeaseExpiresAtUtc = now.Add(leaseDuration);
        Touch(now);
    }

    public bool IsClaimable(DateTimeOffset now)
        => Status is DevelopmentTaskStatus.Ready or DevelopmentTaskStatus.ChangesRequested
           || (Status == DevelopmentTaskStatus.InProgress
               && LeaseExpiresAtUtc.HasValue
               && LeaseExpiresAtUtc.Value <= now);

    public void AddEvidence(
        string actor,
        string branch,
        string commitSha,
        string? pullRequestUrl,
        string payloadJson,
        DateTimeOffset now)
    {
        if (Status != DevelopmentTaskStatus.InProgress)
        {
            throw new InvalidOperationException("Evidence can only be added while a task is in progress.");
        }

        var item = new TaskEvidence(Id, actor, branch, commitSha, pullRequestUrl, payloadJson, now);
        _evidence.Add(item);

        ActiveBranch = item.Branch;
        LastCommitSha = item.CommitSha;
        PullRequestUrl = item.PullRequestUrl ?? PullRequestUrl;
        Touch(now);
        AddEvent("task.evidence_added", actor, payloadJson, now);
    }

    public void SubmitForReview(string actor, DateTimeOffset now)
    {
        EnsureStatus(DevelopmentTaskStatus.InProgress);

        if (_evidence.Count == 0)
        {
            throw new InvalidOperationException("At least one evidence record is required before review.");
        }

        ClearLease();
        Status = DevelopmentTaskStatus.ReadyForReview;
        Touch(now);
        AddEvent("task.submitted_for_review", actor, "{}", now);
    }

    public void ApplyReview(
        ReviewDecision decision,
        string actor,
        string summary,
        string findingsJson,
        bool completeOnPass,
        DateTimeOffset now)
    {
        EnsureStatus(DevelopmentTaskStatus.ReadyForReview);
        _reviews.Add(new TaskReview(Id, decision, actor, summary, findingsJson, now));
        ClearLease();

        if (decision == ReviewDecision.Pass)
        {
            foreach (var criterion in _acceptanceCriteria)
            {
                criterion.MarkSatisfied();
            }

            Status = completeOnPass
                ? DevelopmentTaskStatus.Done
                : DevelopmentTaskStatus.ReadyForReview;

            AddEvent(completeOnPass ? "task.done" : "review.passed", actor, findingsJson, now);
        }
        else
        {
            foreach (var criterion in _acceptanceCriteria)
            {
                criterion.Reset();
            }

            Status = DevelopmentTaskStatus.ChangesRequested;
            AddEvent("review.changes_requested", actor, findingsJson, now);
        }

        Touch(now);
    }

    public void Block(string actor, string reason, DateTimeOffset now)
    {
        if (Status is DevelopmentTaskStatus.Done or DevelopmentTaskStatus.Cancelled)
        {
            throw new InvalidOperationException($"Task {Code} cannot be blocked from status {Status}.");
        }

        BlockReason = Guard.NotBlank(reason, nameof(reason), 2000);
        ClearLease();
        Status = DevelopmentTaskStatus.Blocked;
        Touch(now);
        AddEvent("task.blocked", actor, "{}", now);
    }

    public void ResumeFromBlocked(string actor, DateTimeOffset now)
    {
        EnsureStatus(DevelopmentTaskStatus.Blocked);
        BlockReason = null;
        ClearLease();
        Status = DevelopmentTaskStatus.Ready;
        Touch(now);
        AddEvent("task.resumed", actor, "{}", now);
    }

    public void Reopen(string actor, string reason, DateTimeOffset now)
    {
        if (Status != DevelopmentTaskStatus.Done)
        {
            throw new InvalidOperationException("Only completed tasks can be reopened.");
        }

        foreach (var criterion in _acceptanceCriteria)
        {
            criterion.Reset();
        }

        ClearLease();
        Status = DevelopmentTaskStatus.ChangesRequested;
        Touch(now);
        AddEvent("task.reopened", actor, JsonSerializer.Serialize(new { reason }), now);
    }

    private static void ValidateLeaseDuration(TimeSpan leaseDuration)
    {
        if (leaseDuration < TimeSpan.FromSeconds(30) || leaseDuration > TimeSpan.FromHours(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(leaseDuration),
                "Task lease duration must be between 30 seconds and 1 hour.");
        }
    }

    private void ClearLease()
    {
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
        LastHeartbeatAtUtc = null;
    }

    private void EnsureStatus(DevelopmentTaskStatus expected)
    {
        if (Status != expected)
        {
            throw new InvalidOperationException($"Task {Code} must be {expected}, current status is {Status}.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAtUtc = now;
        Revision = checked(Revision + 1);
    }

    private void AddEvent(string eventType, string actor, string payloadJson, DateTimeOffset now)
        => _events.Add(new TaskEvent(Id, eventType, actor, payloadJson, now));
}
