namespace GitExtensions.GitLab.Api.Models;

public sealed class User
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
    public string Name { get; init; } = "";
    public string? State { get; init; }
    public string? WebUrl { get; init; }

    public string DisplayName => string.IsNullOrEmpty(Name) ? $"@{Username}" : $"{Name} (@{Username})";

    public override string ToString() => DisplayName;
}
