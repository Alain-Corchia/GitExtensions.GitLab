namespace GitExtensions.GitLab.Api.Models;

public sealed class FileDiff
{
    public string OldPath { get; init; } = "";
    public string NewPath { get; init; } = "";
    public string? AMode { get; init; }
    public string? BMode { get; init; }

    /// <summary>
    ///  Hunks of the unified diff, without the "diff --git" and "---"/"+++" header lines.
    /// </summary>
    public string Diff { get; init; } = "";

    public bool NewFile { get; init; }
    public bool RenamedFile { get; init; }
    public bool DeletedFile { get; init; }
    public bool? TooLarge { get; init; }
}

public sealed class CompareResult
{
    public IReadOnlyList<Commit> Commits { get; init; } = [];
    public IReadOnlyList<FileDiff> Diffs { get; init; } = [];
    public bool CompareTimeout { get; init; }
    public bool CompareSameRef { get; init; }
}
