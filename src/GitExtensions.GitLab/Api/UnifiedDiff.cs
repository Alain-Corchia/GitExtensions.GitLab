using System.Text;
using GitExtensions.GitLab.Api.Models;

namespace GitExtensions.GitLab.Api;

/// <summary>
///  Rebuilds "git diff" output from the per-file diffs returned by the GitLab API.
/// </summary>
public static class UnifiedDiff
{
    public static string Format(FileDiff diff)
    {
        StringBuilder builder = new();
        builder.Append("diff --git a/").Append(diff.OldPath).Append(" b/").Append(diff.NewPath).Append('\n');

        if (diff.NewFile)
        {
            builder.Append("new file mode ").Append(diff.BMode).Append('\n');
        }
        else if (diff.DeletedFile)
        {
            builder.Append("deleted file mode ").Append(diff.AMode).Append('\n');
        }
        else if (diff.RenamedFile)
        {
            builder.Append("rename from ").Append(diff.OldPath).Append('\n');
            builder.Append("rename to ").Append(diff.NewPath).Append('\n');
        }

        if (diff.Diff.Length > 0)
        {
            builder.Append("--- ").Append(diff.NewFile ? "/dev/null" : "a/" + diff.OldPath).Append('\n');
            builder.Append("+++ ").Append(diff.DeletedFile ? "/dev/null" : "b/" + diff.NewPath).Append('\n');
            builder.Append(diff.Diff);
            if (!diff.Diff.EndsWith('\n'))
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    public static string Format(IEnumerable<FileDiff> diffs) => string.Concat(diffs.Select(Format));
}
