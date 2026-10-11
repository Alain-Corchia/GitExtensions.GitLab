using GitExtensions.GitLab.Api.Models;

namespace GitExtensions.GitLab;

internal static class MergeRequestText
{
    /// <summary>
    ///  Default title of a merge request with several commits, as GitLab does: the humanized branch name.
    /// </summary>
    public static string GetDefaultTitle(string branchName)
    {
        string name = branchName[(branchName.LastIndexOf('/') + 1)..].Replace('-', ' ').Replace('_', ' ').Trim();
        return name.Length == 0 ? branchName : char.ToUpperInvariant(name[0]) + name[1..];
    }

    public static string GetStatus(MergeRequest mergeRequest) => mergeRequest.State switch
    {
        "merged" => "Merged",
        "closed" => "Closed",
        "locked" => "Locked",
        _ when mergeRequest.Draft => "Draft",
        _ when mergeRequest.HasConflicts => "Conflicts",
        _ => mergeRequest.DetailedMergeStatus switch
        {
            "mergeable" => "Ready to merge",
            "not_approved" or "approvals_syncing" => "Approval required",
            "ci_must_pass" => "Pipeline must pass",
            "ci_still_running" => "Pipeline running",
            "discussions_not_resolved" => "Unresolved threads",
            "need_rebase" => "Rebase needed",
            "conflict" => "Conflicts",
            "draft_status" => "Draft",
            "requested_changes" => "Changes requested",
            null or "" or "checking" or "unchecked" or "preparing" => "Open",
            { } status => char.ToUpperInvariant(status[0]) + status[1..].Replace('_', ' ')
        }
    };
}
