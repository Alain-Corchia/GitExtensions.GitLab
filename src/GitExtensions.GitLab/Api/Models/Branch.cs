namespace GitExtensions.GitLab.Api.Models;

public sealed class Branch
{
    public string Name { get; init; } = "";
    public Commit Commit { get; init; } = new();
    public bool Default { get; init; }
    public bool Protected { get; init; }
    public bool Merged { get; init; }

    public override string ToString() => Name;
}

public sealed class Commit
{
    public string Id { get; init; } = "";
    public string ShortId { get; init; } = "";
    public string Title { get; init; } = "";
    public string? Message { get; init; }
    public string AuthorName { get; init; } = "";
    public string AuthorEmail { get; init; } = "";
    public DateTimeOffset? AuthoredDate { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public string? WebUrl { get; init; }
}
