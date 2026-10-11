using GitExtensions.GitLab.Api;
using GitExtensions.GitLab.Api.Models;

namespace GitExtensions.GitLab.Tests;

public class MergeRequestTextTests
{
    [Theory]
    [InlineData("feature/add-login_page", "Add login page")]
    [InlineData("fix-123", "Fix 123")]
    [InlineData("main", "Main")]
    [InlineData("feature/", "feature/")]
    public void GetDefaultTitle_humanizes_the_branch_name(string branchName, string expected)
    {
        Assert.Equal(expected, MergeRequestText.GetDefaultTitle(branchName));
    }

    [Theory]
    [InlineData("merged", false, false, "mergeable", "Merged")]
    [InlineData("opened", true, false, "mergeable", "Draft")]
    [InlineData("opened", false, true, "conflict", "Conflicts")]
    [InlineData("opened", false, false, "mergeable", "Ready to merge")]
    [InlineData("opened", false, false, "not_approved", "Approval required")]
    [InlineData("opened", false, false, "checking", "Open")]
    [InlineData("opened", false, false, null, "Open")]
    [InlineData("opened", false, false, "some_new_status", "Some new status")]
    public void GetStatus_describes_the_merge_request(string state, bool draft, bool hasConflicts, string? detailedMergeStatus, string expected)
    {
        MergeRequest mergeRequest = new() { State = state, Draft = draft, HasConflicts = hasConflicts, DetailedMergeStatus = detailedMergeStatus };

        Assert.Equal(expected, MergeRequestText.GetStatus(mergeRequest));
    }

    [Fact]
    public void UnifiedDiff_rebuilds_git_diff_headers()
    {
        FileDiff modified = new() { OldPath = "src/a.cs", NewPath = "src/a.cs", Diff = "@@ -1 +1 @@\n-old\n+new\n" };
        FileDiff added = new() { OldPath = "b.txt", NewPath = "b.txt", NewFile = true, BMode = "100644", Diff = "@@ -0,0 +1 @@\n+b" };
        FileDiff renamed = new() { OldPath = "old.txt", NewPath = "new.txt", RenamedFile = true, Diff = "" };

        Assert.Equal("diff --git a/src/a.cs b/src/a.cs\n--- a/src/a.cs\n+++ b/src/a.cs\n@@ -1 +1 @@\n-old\n+new\n", UnifiedDiff.Format(modified));
        Assert.Equal("diff --git a/b.txt b/b.txt\nnew file mode 100644\n--- /dev/null\n+++ b/b.txt\n@@ -0,0 +1 @@\n+b\n", UnifiedDiff.Format(added));
        Assert.Equal("diff --git a/old.txt b/new.txt\nrename from old.txt\nrename to new.txt\n", UnifiedDiff.Format(renamed));
    }
}
