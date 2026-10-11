using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtensions.GitLab.Api;
using GitExtensions.GitLab.Api.Models;
using GitExtensions.GitLab.Hosting;
using GitUI;
using ResourceManager;
using MessageBoxes = GitExtensions.Extensibility.MessageBoxes;

namespace GitExtensions.GitLab.Forms;

/// <summary>
///  Lists the merge requests of a GitLab project and acts on them: review the changes, comment,
///  approve, merge, close or reopen, check out the source branch.
/// </summary>
internal sealed class MergeRequestsForm : GitModuleForm
{
    private readonly TranslationString _caption = new("GitLab merge requests");
    private readonly TranslationString _loading = new("Loading...");
    private readonly TranslationString _noMergeRequest = new("No merge request");
    private readonly TranslationString _projectLoadFailed = new("The project {0} could not be loaded: {1}");
    private readonly TranslationString _listLoadFailed = new("The merge requests could not be loaded: {0}");
    private readonly TranslationString _detailsLoadFailed = new("The merge request details could not be loaded: {0}");
    private readonly TranslationString _changesLoadFailed = new("The changes could not be loaded: {0}");
    private readonly TranslationString _actionFailed = new("The operation failed.");
    private readonly TranslationString _confirmClose = new("Close merge request !{0}?");
    private readonly TranslationString _mergeHeading = new("Merge !{0} into {1}?");
    private readonly TranslationString _mergeCommit = new("Create a merge commit");
    private readonly TranslationString _squashAndMerge = new("Squash commits and merge");
    private readonly TranslationString _deleteSourceBranch = new("Delete source branch");
    private readonly TranslationString _merge = new("Merge");
    private readonly TranslationString _approve = new("Approve");
    private readonly TranslationString _revokeApproval = new("Revoke approval");
    private readonly TranslationString _close = new("Close");
    private readonly TranslationString _reopen = new("Reopen");
    private readonly TranslationString _changesTab = new("Changes ({0})");
    private readonly TranslationString _commitsTab = new("Commits ({0})");
    private readonly TranslationString _discussionHeader = new("Discussion ({0})");
    private readonly TranslationString _noDescription = new("No description.");

    private readonly GitLabPlugin _plugin;
    private readonly GitLabClient _client;

    private readonly ComboBox _projectCB = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
    private readonly ComboBox _stateCB = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    private readonly ComboBox _scopeCB = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 170 };
    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Button _newButton = new() { Text = "New merge request...", AutoSize = true };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    private readonly Label _titleLabel = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 4) };
    private readonly Label _summaryLabel = new() { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 4) };
    private readonly Button _openButton = new() { Text = "Open in browser", AutoSize = true };
    private readonly Button _checkoutButton = new() { Text = "Checkout source branch", AutoSize = true };
    private readonly Button _approveButton = new() { AutoSize = true };
    private readonly Button _mergeButton = new() { AutoSize = true };
    private readonly Button _closeButton = new() { AutoSize = true };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _overviewPage = new() { Text = "Overview" };
    private readonly TabPage _changesPage = new();
    private readonly TabPage _commitsPage = new();
    private readonly RichTextBox _overviewBox = new() { Dock = DockStyle.Fill, ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = true };
    private readonly ChangesView _changesView = new() { Dock = DockStyle.Fill };
    private readonly ListView _commitsList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private readonly TextBox _commentTB = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, PlaceholderText = "Write a comment (Markdown)" };
    private readonly Button _commentButton = new() { Text = "Comment", AutoSize = true, Dock = DockStyle.Right };

    private GitLabHostedRemote? _remote;
    private Project? _project;
    private MergeRequest? _current;
    private Approvals? _approvals;
    private CancellationTokenSource? _listCancellation;
    private CancellationTokenSource? _detailsCancellation;

    public MergeRequestsForm(IGitUICommands commands, GitLabPlugin plugin)
        : base(commands)
    {
        _plugin = plugin;
        _client = plugin.Client;

        BuildLayout();
        InitializeComplete();

        Text = _caption.Text;
        ShowMergeRequest(null);
    }

    private void BuildLayout()
    {
        SuspendLayout();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1200, 800);
        MinimumSize = new Size(800, 500);
        StartPosition = FormStartPosition.CenterParent;
        Icon = Images.GitLabIcon;
        ShowInTaskbar = true;

        _stateCB.Items.AddRange([
            new Choice<MergeRequestState>(MergeRequestState.Opened, "Open"),
            new Choice<MergeRequestState>(MergeRequestState.Merged, "Merged"),
            new Choice<MergeRequestState>(MergeRequestState.Closed, "Closed"),
            new Choice<MergeRequestState>(MergeRequestState.All, "All")]);
        _stateCB.SelectedIndex = 0;
        _scopeCB.Items.AddRange([
            new Choice<MergeRequestScope>(MergeRequestScope.All, "All"),
            new Choice<MergeRequestScope>(MergeRequestScope.CreatedByMe, "Created by me"),
            new Choice<MergeRequestScope>(MergeRequestScope.AssignedToMe, "Assigned to me"),
            new Choice<MergeRequestScope>(MergeRequestScope.ReviewRequestedFromMe, "Review requested from me")]);
        _scopeCB.SelectedIndex = 0;

        FlowLayoutPanel toolbar = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6, 6, 6, 2) };
        toolbar.Controls.AddRange([
            CreateLabel("Project"), _projectCB,
            CreateLabel("State"), _stateCB,
            CreateLabel("Scope"), _scopeCB,
            _refreshButton, _newButton]);

        _list.Columns.Add("!", 50);
        _list.Columns.Add("Title", 300);
        _list.Columns.Add("Author", 110);
        _list.Columns.Add("Status", 110);
        _list.Columns.Add("Updated", 110);

        _titleLabel.Font = new Font(Font.FontFamily, Font.Size * 1.3f, FontStyle.Bold);

        FlowLayoutPanel actions = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        actions.Controls.AddRange([_openButton, _checkoutButton, _approveButton, _mergeButton, _closeButton]);

        _commitsList.Columns.Add("Commit", 90);
        _commitsList.Columns.Add("Title", 420);
        _commitsList.Columns.Add("Author", 150);
        _commitsList.Columns.Add("Date", 130);
        _overviewPage.Controls.Add(_overviewBox);
        _changesPage.Controls.Add(_changesView);
        _commitsPage.Controls.Add(_commitsList);
        _tabs.TabPages.AddRange([_overviewPage, _changesPage, _commitsPage]);

        Panel commentPanel = new() { Dock = DockStyle.Bottom, Height = 90, Padding = new Padding(0, 4, 0, 0) };
        commentPanel.Controls.Add(_commentTB);
        commentPanel.Controls.Add(_commentButton);

        Panel details = new() { Dock = DockStyle.Fill, Padding = new Padding(6) };
        details.SizeChanged += (_, _) =>
        {
            Size maximumSize = new(Math.Max(100, details.ClientSize.Width - details.Padding.Horizontal), 0);
            _titleLabel.MaximumSize = maximumSize;
            _summaryLabel.MaximumSize = maximumSize;
        };
        details.Controls.Add(_tabs);
        details.Controls.Add(commentPanel);
        details.Controls.Add(actions);
        details.Controls.Add(_summaryLabel);
        details.Controls.Add(_titleLabel);

        // The size is set before the splitter distance so that the panels keep this ratio when docked.
        SplitContainer split = new() { Size = new Size(1200, 800), SplitterDistance = 460, Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1.Controls.Add(_list);
        split.Panel1.Padding = new Padding(6, 0, 0, 6);
        split.Panel2.Controls.Add(details);

        Controls.Add(split);
        Controls.Add(toolbar);

        _projectCB.SelectedIndexChanged += (_, _) => LoadProject();
        _stateCB.SelectedIndexChanged += (_, _) => LoadList();
        _scopeCB.SelectedIndexChanged += (_, _) => LoadList();
        _refreshButton.Click += (_, _) => LoadList(_current?.Iid);
        _newButton.Click += (_, _) => CreateMergeRequest();
        _list.SelectedIndexChanged += (_, _) => ShowMergeRequest(_list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as MergeRequest : null);
        _list.DoubleClick += (_, _) => OpenInBrowser();
        _openButton.Click += (_, _) => OpenInBrowser();
        _checkoutButton.Click += (_, _) => CheckoutSourceBranch();
        _approveButton.Click += (_, _) => ToggleApproval();
        _mergeButton.Click += (_, _) => Merge();
        _closeButton.Click += (_, _) => CloseOrReopen();
        _commentButton.Click += (_, _) => AddComment();
        _commentTB.TextChanged += (_, _) => _commentButton.Enabled = _current is not null && _commentTB.Text.Trim().Length > 0;

        ResumeLayout(performLayout: false);
        PerformLayout();
    }

    protected override void OnRuntimeLoad(EventArgs e)
    {
        base.OnRuntimeLoad(e);

        IReadOnlyList<GitLabHostedRemote> remotes = _plugin.GetHostedRemotes();
        _projectCB.Items.AddRange([.. remotes]);

        string currentBranch = Module.GetSelectedBranch(emptyIfDetached: true);
        string trackedRemote = currentBranch.Length > 0 ? Module.GetSetting($"branch.{currentBranch}.remote") : "";
        GitLabHostedRemote? remote = remotes.FirstOrDefault(r => r.Name == trackedRemote) ?? remotes.FirstOrDefault();
        if (remote is not null)
        {
            _projectCB.SelectedItem = remote;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _listCancellation?.Cancel();
        _detailsCancellation?.Cancel();
        base.OnFormClosed(e);
    }

    #region List

    private void LoadProject()
    {
        if (_projectCB.SelectedItem is not GitLabHostedRemote remote)
        {
            return;
        }

        _remote = remote;
        _project = null;
        SetListMessage(_loading.Text);
        ShowMergeRequest(null);

        ThreadHelper.FileAndForget(async () =>
        {
            Project project;
            try
            {
                project = await _client.GetProjectAsync(remote.ProjectPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await this.SwitchToMainThreadAsync();
                SetListMessage(string.Format(_projectLoadFailed.Text, remote.ProjectPath, ex.Message));
                return;
            }

            await this.SwitchToMainThreadAsync();
            if (_remote != remote)
            {
                return;
            }

            _project = project;
            LoadList();
        });
    }

    private void LoadList(long? iidToSelect = null)
    {
        if (_project is not { } project
            || _stateCB.SelectedItem is not Choice<MergeRequestState> state
            || _scopeCB.SelectedItem is not Choice<MergeRequestScope> scope)
        {
            return;
        }

        _listCancellation?.Cancel();
        _listCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _listCancellation.Token;
        SetListMessage(_loading.Text);

        ThreadHelper.FileAndForget(async () =>
        {
            IReadOnlyList<MergeRequest> mergeRequests;
            try
            {
                mergeRequests = await _client.GetMergeRequestsAsync(project.Id, state.Value, scope.Value, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await this.SwitchToMainThreadAsync(cancellationToken);
                SetListMessage(string.Format(_listLoadFailed.Text, ex.Message));
                return;
            }

            await this.SwitchToMainThreadAsync(cancellationToken);
            FillList(mergeRequests, iidToSelect);
        });
    }

    private void FillList(IReadOnlyList<MergeRequest> mergeRequests, long? iidToSelect)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (MergeRequest mergeRequest in mergeRequests)
        {
            _list.Items.Add(new ListViewItem([
                mergeRequest.Iid.ToString(),
                mergeRequest.Title,
                mergeRequest.Author.Username,
                MergeRequestText.GetStatus(mergeRequest),
                mergeRequest.UpdatedAt.LocalDateTime.ToString("g")])
            {
                Tag = mergeRequest,
                ToolTipText = $"{mergeRequest.SourceBranch} → {mergeRequest.TargetBranch}"
            });
        }

        _list.EndUpdate();

        if (_list.Items.Count == 0)
        {
            SetListMessage(_noMergeRequest.Text);
            ShowMergeRequest(null);
            return;
        }

        ListViewItem selection = _list.Items.Cast<ListViewItem>().FirstOrDefault(item => ((MergeRequest)item.Tag!).Iid == iidToSelect) ?? _list.Items[0];
        selection.Selected = true;
        selection.EnsureVisible();
    }

    private void SetListMessage(string message)
    {
        _list.Items.Clear();
        _list.Items.Add(new ListViewItem(["", message]));
    }

    #endregion

    #region Details

    private void ShowMergeRequest(MergeRequest? mergeRequest)
    {
        _detailsCancellation?.Cancel();
        _current = mergeRequest;
        _approvals = null;

        _overviewBox.Clear();
        _commitsList.Items.Clear();
        _changesView.Clear();
        _commentTB.Clear();
        UpdateTabTitles(null, null);
        UpdateActions();

        if (mergeRequest is null)
        {
            _titleLabel.Text = "";
            _summaryLabel.Text = "";
            return;
        }

        _titleLabel.Text = $"!{mergeRequest.Iid} {mergeRequest.Title}";
        _summaryLabel.Text = GetSummary(mergeRequest, approvals: null);
        _changesView.Clear(_loading.Text);
        LoadDetails(mergeRequest);
    }

    private void LoadDetails(MergeRequest mergeRequest)
    {
        _detailsCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _detailsCancellation.Token;
        long projectId = mergeRequest.ProjectId;
        long iid = mergeRequest.Iid;

        // Runs on a background thread. The requests run in parallel; every failure is observed.
        ThreadHelper.FileAndForget(async () =>
        {
            Task<Result<MergeRequest>> detailsTask = Result.Of(_client.GetMergeRequestAsync(projectId, iid, cancellationToken));
            Task<Result<IReadOnlyList<Note>>> notesTask = Result.Of(_client.GetMergeRequestNotesAsync(projectId, iid, cancellationToken));
            Task<Result<IReadOnlyList<Commit>>> commitsTask = Result.Of(_client.GetMergeRequestCommitsAsync(projectId, iid, cancellationToken));
            Task<Result<IReadOnlyList<FileDiff>>> diffsTask = Result.Of(_client.GetMergeRequestDiffsAsync(projectId, iid, cancellationToken));
            Task<Result<Approvals>> approvalsTask = Result.Of(_client.GetMergeRequestApprovalsAsync(projectId, iid, cancellationToken));

            Result<MergeRequest> details = await detailsTask;
            Result<IReadOnlyList<Note>> notes = await notesTask;
            Result<IReadOnlyList<Commit>> commits = await commitsTask;
            Result<Approvals> approvals = await approvalsTask;

            await this.SwitchToMainThreadAsync(cancellationToken);

            if ((details.Error ?? notes.Error ?? commits.Error) is { } error)
            {
                _overviewBox.Text = string.Format(_detailsLoadFailed.Text, error.Message);
                _changesView.Clear();
                return;
            }

            // Approvals may be unavailable (e.g. old GitLab versions): the rest of the details is still useful.
            _current = details.Value;
            _approvals = approvals.Value;
            _titleLabel.Text = $"!{details.Value!.Iid} {details.Value.Title}";
            _summaryLabel.Text = GetSummary(details.Value, approvals.Value);
            RenderOverview(details.Value, notes.Value!);
            FillCommits(commits.Value!);
            UpdateActions();

            Result<IReadOnlyList<FileDiff>> diffs = await diffsTask;
            await this.SwitchToMainThreadAsync(cancellationToken);
            if (diffs.Error is not null)
            {
                _changesView.Clear(string.Format(_changesLoadFailed.Text, diffs.Error.Message));
                UpdateTabTitles(null, commits.Value!.Count);
                return;
            }

            _changesView.ShowDiffs(diffs.Value!, details.Value.DiffRefs?.BaseSha, details.Value.DiffRefs?.HeadSha ?? details.Value.Sha);
            UpdateTabTitles(diffs.Value!.Count, commits.Value!.Count);
        });
    }

    private static string GetSummary(MergeRequest mergeRequest, Approvals? approvals)
    {
        List<string> lines =
        [
            $"{MergeRequestText.GetStatus(mergeRequest)} · {mergeRequest.SourceBranch} → {mergeRequest.TargetBranch} · by {mergeRequest.Author.DisplayName}, {mergeRequest.CreatedAt.LocalDateTime:g}"
        ];

        List<string> details = [];
        if (mergeRequest.HeadPipeline is { } pipeline)
        {
            details.Add($"Pipeline: {pipeline.Status}");
        }

        if (approvals is not null)
        {
            details.Add(approvals.ApprovedBy.Count == 0
                ? "No approval"
                : $"Approved by {string.Join(", ", approvals.ApprovedBy.Select(approver => approver.User.Username))}");
            if (approvals.ApprovalsLeft is > 0)
            {
                details.Add($"{approvals.ApprovalsLeft} approval(s) left");
            }
        }

        if (mergeRequest.Assignees.Count > 0)
        {
            details.Add($"Assignees: {string.Join(", ", mergeRequest.Assignees.Select(user => user.Username))}");
        }

        if (mergeRequest.Reviewers.Count > 0)
        {
            details.Add($"Reviewers: {string.Join(", ", mergeRequest.Reviewers.Select(user => user.Username))}");
        }

        if (mergeRequest.Labels.Count > 0)
        {
            details.Add($"Labels: {string.Join(", ", mergeRequest.Labels)}");
        }

        if (details.Count > 0)
        {
            lines.Add(string.Join(" · ", details));
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void RenderOverview(MergeRequest mergeRequest, IReadOnlyList<Note> notes)
    {
        Font regular = _overviewBox.Font;
        using Font bold = new(regular, FontStyle.Bold);
        using Font italic = new(regular, FontStyle.Italic);

        _overviewBox.SuspendLayout();
        _overviewBox.Clear();

        Append(string.IsNullOrWhiteSpace(mergeRequest.Description) ? _noDescription.Text : mergeRequest.Description.Trim(), regular);
        Append(Environment.NewLine + Environment.NewLine, regular);
        Append(mergeRequest.WebUrl + Environment.NewLine + Environment.NewLine, regular);

        Append(string.Format(_discussionHeader.Text, notes.Count(note => !note.System)) + Environment.NewLine, bold);
        foreach (Note note in notes)
        {
            if (note.System)
            {
                Append($"{note.CreatedAt.LocalDateTime:g}  {note.Author.Username} {note.Body}{Environment.NewLine}", italic, SystemColors.GrayText);
                continue;
            }

            Append(Environment.NewLine, regular);
            Append($"{note.Author.DisplayName} — {note.CreatedAt.LocalDateTime:g}{Environment.NewLine}", bold);
            Append(note.Body.Trim() + Environment.NewLine, regular);
        }

        _overviewBox.SelectionStart = 0;
        _overviewBox.ScrollToCaret();
        _overviewBox.ResumeLayout();

        void Append(string text, Font font, Color? color = null)
        {
            _overviewBox.SelectionStart = _overviewBox.TextLength;
            _overviewBox.SelectionLength = 0;
            _overviewBox.SelectionFont = font;
            _overviewBox.SelectionColor = color ?? _overviewBox.ForeColor;
            _overviewBox.AppendText(text.ReplaceLineEndings("\n"));
        }
    }

    private void FillCommits(IReadOnlyList<Commit> commits)
    {
        _commitsList.BeginUpdate();
        _commitsList.Items.Clear();
        foreach (Commit commit in commits)
        {
            _commitsList.Items.Add(new ListViewItem([commit.ShortId, commit.Title, commit.AuthorName, (commit.AuthoredDate ?? commit.CreatedAt).LocalDateTime.ToString("g")]));
        }

        _commitsList.EndUpdate();
    }

    private void UpdateTabTitles(int? changeCount, int? commitCount)
    {
        _changesPage.Text = changeCount is null ? "Changes" : string.Format(_changesTab.Text, changeCount);
        _commitsPage.Text = commitCount is null ? "Commits" : string.Format(_commitsTab.Text, commitCount);
    }

    private void UpdateActions()
    {
        MergeRequest? mergeRequest = _current;
        bool isOpened = mergeRequest?.IsOpened is true;

        _openButton.Enabled = mergeRequest is not null;
        _checkoutButton.Enabled = mergeRequest is not null && _remote is not null && mergeRequest.State != "merged";

        _approveButton.Text = _approvals?.UserHasApproved is true ? _revokeApproval.Text : _approve.Text;
        _approveButton.Enabled = isOpened && _approvals is { } approvals && (approvals.UserHasApproved || approvals.UserCanApprove);

        _mergeButton.Text = _merge.Text + "...";
        _mergeButton.Enabled = isOpened && mergeRequest!.Permissions?.CanMerge is true && !mergeRequest.Draft && !mergeRequest.HasConflicts;

        _closeButton.Text = mergeRequest?.State == "closed" ? _reopen.Text : _close.Text;
        _closeButton.Enabled = mergeRequest is not null && mergeRequest.State is "opened" or "closed";

        _commentTB.Enabled = mergeRequest is not null;
        _commentButton.Enabled = mergeRequest is not null && _commentTB.Text.Trim().Length > 0;
    }

    #endregion

    #region Actions

    private void OpenInBrowser()
    {
        if (_current is { } mergeRequest)
        {
            _plugin.OpenUrl(mergeRequest.WebUrl);
        }
    }

    private void CreateMergeRequest()
    {
        using CreateMergeRequestForm form = new(UICommands, _plugin);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            LoadList();
        }
    }

    /// <summary>
    ///  Fetches the source branch of the merge request and opens the checkout dialog.
    ///  The branch of a fork is fetched from the target project, which publishes it as refs/merge-requests/IID/head.
    /// </summary>
    private void CheckoutSourceBranch()
    {
        if (_current is not { } mergeRequest || _remote is not { } remote)
        {
            return;
        }

        bool sameProject = mergeRequest.SourceProjectId == mergeRequest.TargetProjectId;
        string remoteBranch = sameProject ? mergeRequest.SourceBranch : $"merge-requests/{mergeRequest.Iid}";
        string sourceRef = sameProject ? $"refs/heads/{mergeRequest.SourceBranch}" : $"refs/merge-requests/{mergeRequest.Iid}/head";

        if (!UICommands.StartGitCommandProcessDialog(this, $"fetch --no-tags \"{remote.Name}\" \"+{sourceRef}:refs/remotes/{remote.Name}/{remoteBranch}\""))
        {
            return;
        }

        UICommands.RepoChangedNotifier.Notify();
        UICommands.StartCheckoutRemoteBranch(this, $"{remote.Name}/{remoteBranch}");
    }

    private void ToggleApproval()
    {
        if (_current is not { } mergeRequest || _approvals is not { } approvals)
        {
            return;
        }

        RunAction(approvals.UserHasApproved
            ? () => _client.UnapproveMergeRequestAsync(mergeRequest.ProjectId, mergeRequest.Iid)
            : () => _client.ApproveMergeRequestAsync(mergeRequest.ProjectId, mergeRequest.Iid, mergeRequest.Sha));
    }

    private void Merge()
    {
        if (_current is not { } mergeRequest)
        {
            return;
        }

        TaskDialogRadioButton mergeCommit = new(_mergeCommit.Text) { Checked = !mergeRequest.Squash };
        TaskDialogRadioButton squash = new(_squashAndMerge.Text) { Checked = mergeRequest.Squash };
        TaskDialogButton mergeButton = new(_merge.Text);
        TaskDialogPage page = new()
        {
            Caption = Text,
            Heading = string.Format(_mergeHeading.Text, mergeRequest.Iid, mergeRequest.TargetBranch),
            Text = mergeRequest.Title,
            Icon = TaskDialogIcon.Information,
            RadioButtons = { mergeCommit, squash },
            Verification = new TaskDialogVerificationCheckBox(_deleteSourceBranch.Text, mergeRequest.ForceRemoveSourceBranch ?? true),
            Buttons = { mergeButton, TaskDialogButton.Cancel },
            DefaultButton = mergeButton
        };

        if (TaskDialog.ShowDialog(this, page) != mergeButton)
        {
            return;
        }

        AcceptMergeRequestRequest request = new()
        {
            Squash = squash.Checked,
            ShouldRemoveSourceBranch = page.Verification.Checked,
            Sha = mergeRequest.Sha
        };

        RunAction(() => _client.AcceptMergeRequestAsync(mergeRequest.ProjectId, mergeRequest.Iid, request));
    }

    private void CloseOrReopen()
    {
        if (_current is not { } mergeRequest)
        {
            return;
        }

        if (mergeRequest.State == "closed")
        {
            RunAction(() => _client.ReopenMergeRequestAsync(mergeRequest.ProjectId, mergeRequest.Iid));
            return;
        }

        if (MessageBoxes.Confirm(this, string.Format(_confirmClose.Text, mergeRequest.Iid), Text, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2))
        {
            RunAction(() => _client.CloseMergeRequestAsync(mergeRequest.ProjectId, mergeRequest.Iid));
        }
    }

    private void AddComment()
    {
        if (_current is not { } mergeRequest || _commentTB.Text.Trim() is not { Length: > 0 } body)
        {
            return;
        }

        RunAction(() => _client.AddMergeRequestNoteAsync(mergeRequest.ProjectId, mergeRequest.Iid, body));
    }

    /// <summary>
    ///  Runs an operation on the current merge request, then reloads the list and the merge request.
    /// </summary>
    private void RunAction(Func<Task> action)
    {
        long? iid = _current?.Iid;
        UseWaitCursor = true;
        Enabled = false;

        ThreadHelper.FileAndForget(async () =>
        {
            Exception? error = null;
            try
            {
                await action();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = ex;
            }

            await this.SwitchToMainThreadAsync();
            UseWaitCursor = false;
            Enabled = true;

            if (error is not null)
            {
                MessageBoxes.ShowError(this, $"{_actionFailed.Text}{Environment.NewLine}{Environment.NewLine}{error.Message}", Text);
                return;
            }

            LoadList(iid);
        });
    }

    #endregion

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(6, 7, 0, 0)
    };

    private readonly record struct Result<T>(T? Value, Exception? Error);

    private static class Result
    {
        /// <summary>
        ///  Awaits <paramref name="task"/> and captures its failure. Cancellation is propagated.
        /// </summary>
        public static async Task<Result<T>> Of<T>(Task<T> task)
        {
            try
            {
                return new Result<T>(await task, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new Result<T>(default, ex);
            }
        }
    }
}
