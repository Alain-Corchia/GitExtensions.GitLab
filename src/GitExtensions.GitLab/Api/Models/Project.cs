namespace GitExtensions.GitLab.Api.Models;

public sealed class Project
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string PathWithNamespace { get; init; } = "";
    public string? Description { get; init; }
    public string WebUrl { get; init; } = "";
    public string? DefaultBranch { get; init; }
    public string HttpUrlToRepo { get; init; } = "";
    public string SshUrlToRepo { get; init; } = "";
    public string? Visibility { get; init; }
    public int ForksCount { get; init; }
    public DateTimeOffset? LastActivityAt { get; init; }
    public ProjectNamespace? Namespace { get; init; }
    public Project? ForkedFromProject { get; init; }

    public override string ToString() => PathWithNamespace;
}

public sealed class ProjectNamespace
{
    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public string FullPath { get; init; } = "";

    /// <summary>
    ///  "user" for a personal namespace, "group" otherwise.
    /// </summary>
    public string Kind { get; init; } = "";
}
