using DevOrchestrator.Common;

namespace DevOrchestrator.Domain.Projects;

public sealed class TargetProject
{
    private TargetProject()
    {
    }

    private TargetProject(
        Guid id,
        string key,
        string name,
        string repositoryUrl,
        string defaultBranch,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Key = key;
        Name = name;
        RepositoryUrl = repositoryUrl;
        DefaultBranch = defaultBranch;
        CreatedAtUtc = createdAtUtc;
        IsActive = true;
    }

    public Guid Id { get; private set; }

    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string RepositoryUrl { get; private set; } = string.Empty;

    public string DefaultBranch { get; private set; } = "main";

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static TargetProject Create(
        string key,
        string name,
        string repositoryUrl,
        string defaultBranch,
        DateTimeOffset now)
    {
        key = Guard.NotBlank(key, nameof(key), 80).ToLowerInvariant();
        name = Guard.NotBlank(name, nameof(name), 200);
        repositoryUrl = Guard.NotBlank(repositoryUrl, nameof(repositoryUrl), 500);
        defaultBranch = Guard.NotBlank(defaultBranch, nameof(defaultBranch), 200);

        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var repositoryUri) ||
            (repositoryUri.Scheme != Uri.UriSchemeHttps && repositoryUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("repositoryUrl must be an absolute HTTP(S) URL.", nameof(repositoryUrl));
        }

        return new TargetProject(Guid.NewGuid(), key, name, repositoryUrl, defaultBranch, now);
    }

    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;
}
