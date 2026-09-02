namespace DevOrchestrator.Domain.Identity;

public static class HumanRoles
{
    public const string Admin = "Admin";
    public const string Architect = "Architect";
    public const string Auditor = "Auditor";
    public const string Implementer = "Implementer";

    public static readonly string[] All = [Admin, Architect, Auditor, Implementer];

    public static bool IsValid(string role)
        => All.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string role)
        => All.FirstOrDefault(x => string.Equals(x, role, StringComparison.OrdinalIgnoreCase))
           ?? throw new ArgumentException($"Unknown role '{role}'.", nameof(role));
}

public sealed class HumanIdentityUser
{
    private HumanIdentityUser() { }

    public Guid Id { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Subject { get; private set; } = string.Empty;
    public string Login { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string? Email { get; private set; }
    public bool IsEnabled { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastLoginAtUtc { get; private set; }

    public static HumanIdentityUser Create(
        string provider,
        string subject,
        string login,
        string? displayName,
        string? email,
        DateTime nowUtc)
        => new()
        {
            Id = Guid.NewGuid(),
            Provider = Required(provider, nameof(provider), 40).ToLowerInvariant(),
            Subject = Required(subject, nameof(subject), 200),
            Login = Required(login, nameof(login), 120),
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? login.Trim() : displayName.Trim(),
            Email = NormalizeOptional(email),
            IsEnabled = true,
            CreatedAtUtc = AsUtc(nowUtc),
            LastLoginAtUtc = AsUtc(nowUtc)
        };

    public void RecordLogin(string login, string? displayName, string? email, DateTime nowUtc)
    {
        Login = Required(login, nameof(login), 120);
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Login : displayName.Trim();
        Email = NormalizeOptional(email);
        LastLoginAtUtc = AsUtc(nowUtc);
    }

    public void Enable() => IsEnabled = true;
    public void Disable() => IsEnabled = false;

    private static string Required(string value, string name, int maxLength)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0) throw new ArgumentException($"{name} is required.", name);
        if (normalized.Length > maxLength) throw new ArgumentException($"{name} exceeds {maxLength} characters.", name);
        return normalized;
    }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static DateTime AsUtc(DateTime value)
        => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public sealed class HumanIdentityRole
{
    private HumanIdentityRole() { }

    public Guid UserId { get; private set; }
    public string Role { get; private set; } = string.Empty;
    public DateTime GrantedAtUtc { get; private set; }
    public string GrantedBy { get; private set; } = string.Empty;

    public static HumanIdentityRole Create(Guid userId, string role, string grantedBy, DateTime nowUtc)
        => new()
        {
            UserId = userId,
            Role = HumanRoles.Normalize(role),
            GrantedAtUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime(),
            GrantedBy = string.IsNullOrWhiteSpace(grantedBy) ? "system" : grantedBy.Trim()
        };
}

public sealed class MachineCredential
{
    private MachineCredential() { }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string KeyHash { get; private set; } = string.Empty;
    public string KeyPrefix { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? LastUsedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string CreatedBy { get; private set; } = string.Empty;

    public static MachineCredential Create(
        string name,
        string keyHash,
        string keyPrefix,
        string role,
        DateTime nowUtc,
        DateTime? expiresAtUtc,
        string createdBy)
    {
        var normalizedRole = HumanRoles.Normalize(role);
        if (normalizedRole == HumanRoles.Admin)
            throw new ArgumentException("Machine credentials cannot receive the Admin role.", nameof(role));

        var now = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
        var expiry = expiresAtUtc?.Kind == DateTimeKind.Utc ? expiresAtUtc : expiresAtUtc?.ToUniversalTime();
        if (expiry.HasValue && expiry <= now) throw new ArgumentException("Credential expiry must be in the future.", nameof(expiresAtUtc));

        return new MachineCredential
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Credential name is required.", nameof(name)) : name.Trim(),
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            Role = normalizedRole,
            IsActive = true,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiry,
            CreatedBy = string.IsNullOrWhiteSpace(createdBy) ? "system" : createdBy.Trim()
        };
    }

    public bool IsUsable(DateTime nowUtc)
        => IsActive && RevokedAtUtc is null && (!ExpiresAtUtc.HasValue || ExpiresAtUtc.Value > nowUtc);

    public void MarkUsed(DateTime nowUtc) => LastUsedAtUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();

    public void Revoke(DateTime nowUtc)
    {
        if (!IsActive) return;
        IsActive = false;
        RevokedAtUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
    }
}

public sealed class SecurityAuditEvent
{
    private SecurityAuditEvent() { }

    public Guid Id { get; private set; }
    public string Actor { get; private set; } = string.Empty;
    public string ActorType { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public string ResourceType { get; private set; } = string.Empty;
    public string ResourceId { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static SecurityAuditEvent Create(
        string actor,
        string actorType,
        string action,
        string resourceType,
        string resourceId,
        DateTime nowUtc,
        string? reason = null,
        string? beforeJson = null,
        string? afterJson = null,
        string? ipAddress = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Actor = actor.Trim(),
            ActorType = actorType.Trim(),
            Action = action.Trim(),
            ResourceType = resourceType.Trim(),
            ResourceId = resourceId.Trim(),
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            BeforeJson = beforeJson,
            AfterJson = afterJson,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress.Trim(),
            CreatedAtUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime()
        };
}
