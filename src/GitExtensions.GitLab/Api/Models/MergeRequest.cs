using System.Text.Json.Serialization;

namespace GitExtensions.GitLab.Api.Models;

public sealed class MergeRequest
{
    public long Id { get; init; }
    public long Iid { get; init; }
    public long ProjectId { get; init; }
    public string Title { get; init; } = "";
    public string? Description { get; init; }

    /// <summary>
    ///  opened, closed, locked or merged.
    /// </summary>
    public string State { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? MergedAt { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public string SourceBranch { get; init; } = "";
    public string TargetBranch { get; init; } = "";
    public long SourceProjectId { get; init; }
    public long TargetProjectId { get; init; }
    public User Author { get; init; } = new();
    public IReadOnlyList<User> Assignees { get; init; } = [];
    public IReadOnlyList<User> Reviewers { get; init; } = [];
    public IReadOnlyList<string> Labels { get; init; } = [];
    public bool Draft { get; init; }
    public bool HasConflicts { get; init; }
    public string? DetailedMergeStatus { get; init; }
    public string? Sha { get; init; }
    public string? MergeCommitSha { get; init; }
    public bool Squash { get; init; }
    public bool? ForceRemoveSourceBranch { get; init; }
    public int UserNotesCount { get; init; }
    public string WebUrl { get; init; } = "";

    /// <summary>
    ///  Only returned when requesting a single merge request.
    /// </summary>
    public DiffRefs? DiffRefs { get; init; }

    /// <summary>
    ///  Only returned when requesting a single merge request.
    /// </summary>
    public Pipeline? HeadPipeline { get; init; }

    /// <summary>
    ///  Permissions of the current user. Only returned when requesting a single merge request.
    /// </summary>
    [JsonPropertyName("user")]
    public MergeRequestPermissions? Permissions { get; init; }

    public bool IsOpened => State == "opened";
}

public sealed class DiffRefs
{
    public string? BaseSha { get; init; }
    public string? HeadSha { get; init; }
    public string? StartSha { get; init; }
}

public sealed class Pipeline
{
    public long Id { get; init; }
    public string Status { get; init; } = "";
    public string? WebUrl { get; init; }
}

public sealed class MergeRequestPermissions
{
    public bool CanMerge { get; init; }
}
