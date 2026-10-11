using GitExtensions.Extensibility.Git;
using GitExtensions.GitLab.Api;
using GitExtensions.GitLab.Api.Models;
using GitUI;
using GitUI.Editor;
using GitUIPluginInterfaces;

namespace GitExtensions.GitLab.Forms;

/// <summary>
///  Shows the files changed by a merge request (or a comparison) and the diff of the selected file.
///  The commits may not exist in the local repository: the diffs come from GitLab.
/// </summary>
internal sealed class ChangesView : UserControl
{
    private readonly FileStatusList _fileStatusList;
    private readonly FileViewer _fileViewer;
    private readonly Label _messageLabel;
    private readonly Dictionary<string, FileDiff> _diffsByPath = [];

    public ChangesView()
    {
        SuspendLayout();

        _fileStatusList = new FileStatusList { Dock = DockStyle.Fill };
        _fileViewer = new FileViewer { Dock = DockStyle.Fill };
        _messageLabel = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(4, 0, 0, 0),
            Visible = false
        };

        // The size is set before the splitter distance so that the panels keep this ratio when docked.
        SplitContainer splitContainer = new()
        {
            Size = new Size(900, 400),
            SplitterDistance = 280,
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical
        };
        splitContainer.Panel1.Controls.Add(_fileStatusList);
        splitContainer.Panel2.Controls.Add(_fileViewer);

        Controls.Add(splitContainer);
        Controls.Add(_messageLabel);

        _fileStatusList.SelectedIndexChanged += FileStatusList_SelectedIndexChanged;

        ResumeLayout(performLayout: false);
    }

    public void Clear(string? message = null)
    {
        _diffsByPath.Clear();
        _fileStatusList.ClearDiffs();
        _fileViewer.Clear();
        ShowMessage(message);
    }

    public void ShowDiffs(IReadOnlyList<FileDiff> diffs, string? baseSha, string? headSha, string? message = null)
    {
        Clear(message);

        if (!ObjectId.TryParse(headSha, out ObjectId headId))
        {
            ShowMessage("The changes cannot be displayed: unknown head commit.");
            return;
        }

        GitRevision? baseRevision = ObjectId.TryParse(baseSha, out ObjectId baseId) ? new GitRevision(baseId) : null;

        List<GitItemStatus> items = new(diffs.Count);
        foreach (FileDiff diff in diffs)
        {
            if (!_diffsByPath.TryAdd(diff.NewPath, diff))
            {
                continue;
            }

            items.Add(new GitItemStatus(diff.NewPath)
            {
                OldName = diff.RenamedFile ? diff.OldPath : null,
                IsNew = diff.NewFile,
                IsDeleted = diff.DeletedFile,
                IsRenamed = diff.RenamedFile,
                IsChanged = !diff.NewFile && !diff.DeletedFile,
                IsTracked = true,
                Staged = StagedStatus.None
            });
        }

        _fileStatusList.SetDiffs(baseRevision, new GitRevision(headId), items);
    }

    private void ShowMessage(string? message)
    {
        _messageLabel.Text = message;
        _messageLabel.Visible = !string.IsNullOrEmpty(message);
    }

    private void FileStatusList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        string? path = _fileStatusList.SelectedItem?.Item.Name;
        if (path is null || !_diffsByPath.TryGetValue(path, out FileDiff? diff))
        {
            _fileViewer.Clear();
            return;
        }

        if (diff.Diff.Length == 0)
        {
            _fileViewer.ViewText(path, diff.TooLarge == true
                ? "The diff of this file is too large to be displayed."
                : "Binary file, or no textual change (mode change, rename without modification).");
            return;
        }

        _fileViewer.ViewFixedPatch(path, UnifiedDiff.Format(diff));
    }
}
