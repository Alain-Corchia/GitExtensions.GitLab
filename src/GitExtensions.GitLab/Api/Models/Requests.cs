namespace GitExtensions.GitLab.Api.Models;

/// <summary>
///  Body of POST /projects/:id/merge_requests, sent to the source project.
/// </summary>
public sealed class CreateMergeRequestRequest
{
    public required string SourceBranch { get; init; }
    public required string TargetBranch { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public long? TargetProjectId { get; init; }
    public IReadOnlyList<long>? AssigneeIds { get; init; }
    public IReadOnlyList<long>? ReviewerIds { get; init; }

    /// <summary>
    ///  Comma-separated label names.
    /// </summary>
    public string? Labels { get; init; }

    public bool RemoveSourceBranch { get; init; }
    public bool Squash { get; init; }
}

/// <summary>
///  Body of PUT /projects/:id/merge_requests/:iid/merge.
/// </summary>
public sealed class AcceptMergeRequestRequest
{
    public bool Squash { get; init; }
    public bool ShouldRemoveSourceBranch { get; init; }

    /// <summary>
    ///  Expected head SHA: the merge is refused if the source branch moved in the meantime.
    /// </summary>
    public string? Sha { get; init; }
}

public enum MergeRequestState
{
    Opened,
    Merged,
    Closed,
    All
}

public enum MergeRequestScope
{
    All,
    CreatedByMe,
    AssignedToMe,
    ReviewRequestedFromMe
}
