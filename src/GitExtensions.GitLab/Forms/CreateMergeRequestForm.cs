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
///  Creates a merge request, within a project or from a fork to its parent project.
/// </summary>
internal sealed class CreateMergeRequestForm : GitModuleForm
{
    private readonly TranslationString _caption = new("Create GitLab merge request");
    private readonly TranslationString _loadingProjects = new("Loading projects...");
    private readonly TranslationString _loadingBranches = new("Loading branches...");
    private readonly TranslationString _comparing = new("Comparing branches...");
    private readonly TranslationString _sameBranch = new("Source and target are the same branch.");
    private readonly TranslationString _nothingToMerge = new("The source branch has no commit that is not in the target branch.");
    private readonly TranslationString _compareTimeout = new("GitLab timed out while comparing the branches: the list of changes may be incomplete.");
    private readonly TranslationString _compareFailed = new("The branches cannot be compared: {0}");
    private readonly TranslationString _commitsTab = new("Commits ({0})");
    private readonly TranslationString _changesTab = new("Changes ({0})");
    private readonly TranslationString _noProject = new("None of the GitLab projects of the remotes could be loaded.");
    private readonly TranslationString _projectLoadFailed = new("The project of remote '{0}' could not be loaded: {1}");
    private readonly TranslationString _branchesLoadFailed = new("The branches of {0} could not be loaded: {1}");
    private readonly TranslationString _unknownBranch = new("The branch '{0}' does not exist in {1}.");
    private readonly TranslationString _titleRequired = new("The title is required.");
    private readonly TranslationString _creating = new("Creating the merge request...");
    private readonly TranslationString _creationFailed = new("The merge request could not be created.");
    private readonly TranslationString _created = new("Merge request !{0} created");
    private readonly TranslationString _openInBrowser = new("Open in browser");
    private readonly TranslationString _none = new("(none)");

    private const string DraftPrefix = "Draft: ";

    private readonly GitLabPlugin _plugin;
    private readonly GitLabClient _client;
    private readonly string? _preferredSourceBranch;
    private readonly Dictionary<long, IReadOnlyList<Branch>> _branchesByProject = [];
    private readonly Dictionary<long, Task<IReadOnlyList<Branch>>> _branchLoads = [];

    private readonly ComboBox _sourceProjectCB = CreateComboBox("sourceProjectCB", ComboBoxStyle.DropDownList);
    private readonly ComboBox _sourceBranchCB = CreateComboBox("sourceBranchCB", ComboBoxStyle.DropDown);
    private readonly ComboBox _targetProjectCB = CreateComboBox("targetProjectCB", ComboBoxStyle.DropDownList);
    private readonly ComboBox _targetBranchCB = CreateComboBox("targetBranchCB", ComboBoxStyle.DropDown);
    private readonly TextBox _titleTB = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _draftCB = new() { Text = "Draft", AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly TextBox _descriptionTB = new() { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true, AcceptsTab = false };
    private readonly ComboBox _assigneeCB = CreateComboBox("assigneeCB", ComboBoxStyle.DropDownList);
    private readonly ComboBox _reviewerCB = CreateComboBox("reviewerCB", ComboBoxStyle.DropDownList);
    private readonly TextBox _labelsTB = new() { Dock = DockStyle.Fill, PlaceholderText = "label1, label2" };
    private readonly CheckBox _removeSourceBranchCB = new() { Text = "Delete source branch when merge request is accepted", AutoSize = true, Checked = true };
    private readonly CheckBox _squashCB = new() { Text = "Squash commits when merge request is accepted", AutoSize = true };
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly TabPage _commitsPage = new();
    private readonly TabPage _changesPage = new();
    private readonly ListView _commitsList = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HeaderStyle = ColumnHeaderStyle.Nonclickable };
    private readonly ChangesView _changesView = new() { Dock = DockStyle.Fill };
    private readonly Label _statusLabel = new() { AutoSize = true, Anchor = AnchorStyles.Left, AutoEllipsis = true };
    private readonly Button _createButton = new() { Text = "Create merge request", AutoSize = true, Enabled = false };
    private readonly Button _cancelButton = new() { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

    private CancellationTokenSource? _compareCancellation;
    private bool _titleEditedByUser;
    private bool _updatingTitle;

    public CreateMergeRequestForm(IGitUICommands commands, GitLabPlugin plugin, string? preferredSourceBranch = null)
        : base(commands)
    {
        _plugin = plugin;
        _client = plugin.Client;
        _preferredSourceBranch = preferredSourceBranch;

        BuildLayout();
        InitializeComplete();

        Text = _caption.Text;
        UpdateTabTitles(commitCount: 0, changeCount: 0);
    }

    private void BuildLayout()
    {
        SuspendLayout();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1000, 760);
        MinimumSize = new Size(700, 560);
        StartPosition = FormStartPosition.CenterParent;
        ShowIcon = true;
        Icon = Images.GitLabIcon;
        CancelButton = _cancelButton;

        _commitsList.Columns.Add("Commit", 90);
        _commitsList.Columns.Add("Title", 520);
        _commitsList.Columns.Add("Author", 160);
        _commitsList.Columns.Add("Date", 140);
        _commitsPage.Controls.Add(_commitsList);
        _changesPage.Controls.Add(_changesView);
        _tabs.TabPages.Add(_changesPage);
        _tabs.TabPages.Add(_commitsPage);

        TableLayoutPanel fields = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(8, 8, 8, 0)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        fields.Controls.Add(CreateLabel("Source project"), 0, 0);
        fields.Controls.Add(_sourceProjectCB, 1, 0);
        fields.Controls.Add(CreateLabel("Source branch"), 2, 0);
        fields.Controls.Add(_sourceBranchCB, 3, 0);

        fields.Controls.Add(CreateLabel("Target project"), 0, 1);
        fields.Controls.Add(_targetProjectCB, 1, 1);
        fields.Controls.Add(CreateLabel("Target branch"), 2, 1);
        fields.Controls.Add(_targetBranchCB, 3, 1);

        TableLayoutPanel titlePanel = new() { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true, Margin = Padding.Empty };
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        titlePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        titlePanel.Controls.Add(_titleTB, 0, 0);
        titlePanel.Controls.Add(_draftCB, 1, 0);
        fields.Controls.Add(CreateLabel("Title"), 0, 2);
        fields.Controls.Add(titlePanel, 1, 2);
        fields.SetColumnSpan(titlePanel, 3);

        _descriptionTB.Height = 130;
        fields.Controls.Add(CreateLabel("Description"), 0, 3);
        fields.Controls.Add(_descriptionTB, 1, 3);
        fields.SetColumnSpan(_descriptionTB, 3);

        fields.Controls.Add(CreateLabel("Assignee"), 0, 4);
        fields.Controls.Add(_assigneeCB, 1, 4);
        fields.Controls.Add(CreateLabel("Reviewer"), 2, 4);
        fields.Controls.Add(_reviewerCB, 3, 4);

        fields.Controls.Add(CreateLabel("Labels"), 0, 5);
        fields.Controls.Add(_labelsTB, 1, 5);
        fields.SetColumnSpan(_labelsTB, 3);

        FlowLayoutPanel options = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        options.Controls.Add(_removeSourceBranchCB);
        options.Controls.Add(_squashCB);
        fields.Controls.Add(options, 1, 6);
        fields.SetColumnSpan(options, 3);

        TableLayoutPanel buttons = new() { Dock = DockStyle.Fill, ColumnCount = 3, AutoSize = true, Padding = new Padding(8, 4, 8, 8) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.Controls.Add(_statusLabel, 0, 0);
        buttons.Controls.Add(_createButton, 1, 0);
        buttons.Controls.Add(_cancelButton, 2, 0);

        TableLayoutPanel root = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(fields, 0, 0);
        root.Controls.Add(_tabs, 0, 1);
        root.Controls.Add(buttons, 0, 2);
        _tabs.Margin = new Padding(8, 4, 8, 0);
        Controls.Add(root);

        _sourceProjectCB.SelectedIndexChanged += (_, _) => OnProjectChanged(isSource: true);
        _targetProjectCB.SelectedIndexChanged += (_, _) => OnProjectChanged(isSource: false);
        _sourceBranchCB.SelectedIndexChanged += (_, _) => OnBranchChanged();
        _targetBranchCB.SelectedIndexChanged += (_, _) => OnBranchChanged();
        _sourceBranchCB.Validated += (_, _) => OnBranchChanged();
        _targetBranchCB.Validated += (_, _) => OnBranchChanged();
        _titleTB.TextChanged += (_, _) =>
        {
            if (!_updatingTitle)
            {
                _titleEditedByUser = true;
            }

            UpdateCreateButton();
        };
        _createButton.Click += (_, _) => CreateMergeRequest();

        ResumeLayout(performLayout: false);
        PerformLayout();
    }

    protected override void OnRuntimeLoad(EventArgs e)
    {
        base.OnRuntimeLoad(e);

        LoadDescriptionTemplate();
        LoadProjects();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _compareCancellation?.Cancel();
        base.OnFormClosed(e);
    }

    #region Projects and branches

    private void LoadProjects()
    {
        SetStatus(_loadingProjects.Text);
        IReadOnlyList<GitLabHostedRemote> remotes = _plugin.GetHostedRemotes();

        ThreadHelper.FileAndForget(async () =>
        {
            List<string> errors = [];
            List<ProjectItem> sources = [];
            foreach (GitLabHostedRemote remote in remotes)
            {
                try
                {
                    sources.Add(new ProjectItem(await _client.GetProjectAsync(remote.ProjectPath), remote.Name));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add(string.Format(_projectLoadFailed.Text, remote.Name, ex.Message));
                }
            }

            // Merge requests of a fork usually target the project it was forked from.
            List<ProjectItem> targets = [.. sources];
            foreach (Project fork in sources.Select(item => item.Project).Where(project => project.ForkedFromProject is not null).ToList())
            {
                long parentId = fork.ForkedFromProject!.Id;
                if (targets.Any(item => item.Project.Id == parentId))
                {
                    continue;
                }

                try
                {
                    targets.Add(new ProjectItem(await _client.GetProjectAsync(parentId), remoteName: null));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add(ex.Message);
                }
            }

            await this.SwitchToMainThreadAsync();

            if (errors.Count > 0)
            {
                MessageBoxes.Show(this, string.Join(Environment.NewLine + Environment.NewLine, errors), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (sources.Count == 0)
            {
                MessageBoxes.ShowError(this, _noProject.Text, Text);
                Close();
                return;
            }

            SetStatus(null);
            FillProjects(sources, targets);
        });
    }

    private void FillProjects(List<ProjectItem> sources, List<ProjectItem> targets)
    {
        string currentBranch = Module.GetSelectedBranch(emptyIfDetached: true);
        string trackedRemote = currentBranch.Length > 0 ? Module.GetSetting($"branch.{currentBranch}.remote") : "";

        _sourceProjectCB.Items.AddRange([.. sources]);
        _targetProjectCB.Items.AddRange([.. targets]);

        ProjectItem source = sources.FirstOrDefault(item => item.RemoteName == trackedRemote) ?? sources[0];
        long targetId = source.Project.ForkedFromProject?.Id ?? source.Project.Id;
        ProjectItem target = targets.FirstOrDefault(item => item.Project.Id == targetId) ?? targets.First(item => item.Project.Id == source.Project.Id);

        _sourceProjectCB.SelectedItem = source;
        _targetProjectCB.SelectedItem = target;
    }

    /// <summary>
    ///  Branch to propose as source: the requested one, else the upstream branch of the current branch, else the current branch.
    /// </summary>
    private string GetPreferredSourceBranch()
    {
        if (!string.IsNullOrEmpty(_preferredSourceBranch))
        {
            return _preferredSourceBranch;
        }

        string currentBranch = Module.GetSelectedBranch(emptyIfDetached: true);
        if (currentBranch.Length == 0)
        {
            return "";
        }

        const string headsPrefix = "refs/heads/";
        string mergeRef = Module.GetSetting($"branch.{currentBranch}.merge");
        return mergeRef.StartsWith(headsPrefix, StringComparison.Ordinal) ? mergeRef[headsPrefix.Length..] : currentBranch;
    }

    private void OnProjectChanged(bool isSource)
    {
        ComboBox projectCB = isSource ? _sourceProjectCB : _targetProjectCB;
        ComboBox branchCB = isSource ? _sourceBranchCB : _targetBranchCB;
        if (projectCB.SelectedItem is not ProjectItem item)
        {
            return;
        }

        string preferredBranch = isSource ? GetPreferredSourceBranch() : item.Project.DefaultBranch ?? "";
        LoadBranches(item.Project, branchCB, preferredBranch);

        if (!isSource)
        {
            LoadMembers(item.Project);
        }
    }

    private void LoadBranches(Project project, ComboBox branchCB, string preferredBranch)
    {
        branchCB.Items.Clear();
        branchCB.Text = _loadingBranches.Text;
        branchCB.Enabled = false;
        UpdateCreateButton();

        // Source and target are often the same project: both share a single request.
        if (!_branchLoads.TryGetValue(project.Id, out Task<IReadOnlyList<Branch>>? branchesTask) || branchesTask.IsFaulted || branchesTask.IsCanceled)
        {
            branchesTask = _client.GetBranchesAsync(project.Id);
            _branchLoads[project.Id] = branchesTask;
        }

        ThreadHelper.FileAndForget(async () =>
        {
            IReadOnlyList<Branch> branches;
            try
            {
                branches = await branchesTask;
            }
            catch (Exception ex)
            {
                await this.SwitchToMainThreadAsync();
                branchCB.Text = "";
                branchCB.Enabled = true;
                MessageBoxes.ShowError(this, string.Format(_branchesLoadFailed.Text, project.PathWithNamespace, ex.Message), Text);
                return;
            }

            await this.SwitchToMainThreadAsync();

            _branchesByProject[project.Id] = branches;
            if ((branchCB == _sourceBranchCB ? _sourceProjectCB : _targetProjectCB).SelectedItem is not ProjectItem { } selected
                || selected.Project.Id != project.Id)
            {
                // Another project was selected in the meantime.
                return;
            }

            branchCB.BeginUpdate();
            branchCB.Items.Clear();
            branchCB.Items.AddRange([.. branches.Select(branch => branch.Name).Order(StringComparer.OrdinalIgnoreCase)]);
            branchCB.EndUpdate();
            branchCB.Enabled = true;

            string? selection = branches.Any(branch => branch.Name == preferredBranch)
                ? preferredBranch
                : branches.FirstOrDefault(branch => branch.Default)?.Name ?? branches.FirstOrDefault()?.Name;
            branchCB.Text = "";
            branchCB.SelectedItem = selection;
            OnBranchChanged();
        });
    }

    private void LoadMembers(Project project)
    {
        _assigneeCB.Items.Clear();
        _reviewerCB.Items.Clear();
        UserItem none = new(null, _none.Text);
        _assigneeCB.Items.Add(none);
        _reviewerCB.Items.Add(none);
        _assigneeCB.SelectedIndex = 0;
        _reviewerCB.SelectedIndex = 0;

        ThreadHelper.FileAndForget(async () =>
        {
            IReadOnlyList<User> members;
            try
            {
                members = await _client.GetProjectMembersAsync(project.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Without the member list, the merge request can still be created without assignee and reviewer.
                System.Diagnostics.Trace.WriteLine($"GitLab: members of {project.PathWithNamespace} not loaded: {ex.Message}");
                return;
            }

            await this.SwitchToMainThreadAsync();

            if (_targetProjectCB.SelectedItem is not ProjectItem { } selected || selected.Project.Id != project.Id)
            {
                return;
            }

            UserItem[] items = [.. members
                .Where(member => member.State is null or "active")
                .DistinctBy(member => member.Id)
                .OrderBy(member => member.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(member => new UserItem(member, member.DisplayName))];
            _assigneeCB.Items.AddRange(items);
            _reviewerCB.Items.AddRange(items);
        });
    }

    #endregion

    #region Comparison

    private void OnBranchChanged()
    {
        UpdateCreateButton();

        if (!TryGetSelection(out ProjectItem? source, out string? sourceBranch, out ProjectItem? target, out string? targetBranch, validate: false))
        {
            return;
        }

        _compareCancellation?.Cancel();
        _compareCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = _compareCancellation.Token;

        _commitsList.Items.Clear();
        UpdateTabTitles(commitCount: 0, changeCount: 0);

        if (source.Project.Id == target.Project.Id && sourceBranch == targetBranch)
        {
            _changesView.Clear(_sameBranch.Text);
            return;
        }

        _changesView.Clear(_comparing.Text);
        long? fromProjectId = source.Project.Id == target.Project.Id ? null : target.Project.Id;
        string? headSha = FindBranch(source.Project, sourceBranch)?.Commit.Id;

        ThreadHelper.FileAndForget(async () =>
        {
            CompareResult comparison;
            try
            {
                comparison = await _client.CompareAsync(source.Project.Id, from: targetBranch, to: sourceBranch, fromProjectId, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await this.SwitchToMainThreadAsync(cancellationToken);
                _changesView.Clear(string.Format(_compareFailed.Text, ex.Message));
                return;
            }

            await this.SwitchToMainThreadAsync(cancellationToken);

            ShowComparison(comparison, headSha, sourceBranch);
        });
    }

    private void ShowComparison(CompareResult comparison, string? headSha, string sourceBranch)
    {
        _commitsList.BeginUpdate();
        foreach (Commit commit in comparison.Commits.Reverse())
        {
            _commitsList.Items.Add(new ListViewItem([commit.ShortId, commit.Title, commit.AuthorName, (commit.AuthoredDate ?? commit.CreatedAt).LocalDateTime.ToString("g")]));
        }

        _commitsList.EndUpdate();
        UpdateTabTitles(comparison.Commits.Count, comparison.Diffs.Count);

        string? message = comparison.CompareTimeout ? _compareTimeout.Text
            : comparison.Commits.Count == 0 ? _nothingToMerge.Text
            : null;
        _changesView.ShowDiffs(comparison.Diffs, baseSha: null, headSha ?? comparison.Commits.LastOrDefault()?.Id, message);

        if (!_titleEditedByUser)
        {
            // Same default as GitLab: the title of the single commit, else the humanized branch name.
            SetTitle(comparison.Commits.Count == 1 ? comparison.Commits[0].Title : MergeRequestText.GetDefaultTitle(sourceBranch));
        }
    }

    private void SetTitle(string title)
    {
        _updatingTitle = true;
        _titleTB.Text = title;
        _updatingTitle = false;
    }

    private void UpdateTabTitles(int commitCount, int changeCount)
    {
        _commitsPage.Text = string.Format(_commitsTab.Text, commitCount);
        _changesPage.Text = string.Format(_changesTab.Text, changeCount);
    }

    #endregion

    #region Creation

    private void UpdateCreateButton()
    {
        _createButton.Enabled = _sourceBranchCB.Enabled && _targetBranchCB.Enabled
            && _sourceProjectCB.SelectedItem is not null && _targetProjectCB.SelectedItem is not null
            && _sourceBranchCB.Text.Length > 0 && _targetBranchCB.Text.Length > 0
            && _titleTB.Text.Trim().Length > 0;
    }

    private Branch? FindBranch(Project project, string name)
        => _branchesByProject.TryGetValue(project.Id, out IReadOnlyList<Branch>? branches) ? branches.FirstOrDefault(branch => branch.Name == name) : null;

    private bool TryGetSelection(
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ProjectItem? source,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? sourceBranch,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out ProjectItem? target,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? targetBranch,
        bool validate)
    {
        source = _sourceProjectCB.SelectedItem as ProjectItem;
        target = _targetProjectCB.SelectedItem as ProjectItem;
        sourceBranch = _sourceBranchCB.Enabled ? _sourceBranchCB.Text.Trim() : null;
        targetBranch = _targetBranchCB.Enabled ? _targetBranchCB.Text.Trim() : null;
        if (source is null || target is null || string.IsNullOrEmpty(sourceBranch) || string.IsNullOrEmpty(targetBranch))
        {
            return false;
        }

        foreach ((ProjectItem project, string branch, ComboBox comboBox) in new[] { (source, sourceBranch, _sourceBranchCB), (target, targetBranch, _targetBranchCB) })
        {
            if (FindBranch(project.Project, branch) is null)
            {
                if (validate)
                {
                    MessageBoxes.ShowError(this, string.Format(_unknownBranch.Text, branch, project.Project.PathWithNamespace), Text);
                    comboBox.Focus();
                }

                return false;
            }
        }

        return true;
    }

    private void CreateMergeRequest()
    {
        if (!TryGetSelection(out ProjectItem? source, out string? sourceBranch, out ProjectItem? target, out string? targetBranch, validate: true))
        {
            return;
        }

        string title = _titleTB.Text.Trim();
        if (title.Length == 0)
        {
            MessageBoxes.ShowError(this, _titleRequired.Text, Text);
            return;
        }

        if (_draftCB.Checked && !title.StartsWith(DraftPrefix, StringComparison.OrdinalIgnoreCase))
        {
            title = DraftPrefix + title;
        }

        string labels = string.Join(',', _labelsTB.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        CreateMergeRequestRequest request = new()
        {
            SourceBranch = sourceBranch,
            TargetBranch = targetBranch,
            TargetProjectId = target.Project.Id,
            Title = title,
            Description = _descriptionTB.Text.Trim(),
            AssigneeIds = (_assigneeCB.SelectedItem as UserItem)?.User is { } assignee ? [assignee.Id] : null,
            ReviewerIds = (_reviewerCB.SelectedItem as UserItem)?.User is { } reviewer ? [reviewer.Id] : null,
            Labels = labels.Length > 0 ? labels : null,
            RemoveSourceBranch = _removeSourceBranchCB.Checked,
            Squash = _squashCB.Checked
        };

        _createButton.Enabled = false;
        SetStatus(_creating.Text);
        UseWaitCursor = true;

        ThreadHelper.FileAndForget(async () =>
        {
            MergeRequest mergeRequest;
            try
            {
                mergeRequest = await _client.CreateMergeRequestAsync(source.Project.Id, request);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await this.SwitchToMainThreadAsync();
                UseWaitCursor = false;
                SetStatus(null);
                UpdateCreateButton();
                MessageBoxes.ShowError(this, $"{_creationFailed.Text}{Environment.NewLine}{Environment.NewLine}{ex.Message}", Text);
                return;
            }

            await this.SwitchToMainThreadAsync();
            UseWaitCursor = false;
            SetStatus(null);
            ShowCreated(mergeRequest);
            DialogResult = DialogResult.OK;
            Close();
        });
    }

    private void ShowCreated(MergeRequest mergeRequest)
    {
        TaskDialogButton openButton = new(_openInBrowser.Text);
        TaskDialogPage page = new()
        {
            Caption = Text,
            Heading = string.Format(_created.Text, mergeRequest.Iid),
            Text = $"{mergeRequest.Title}{Environment.NewLine}{mergeRequest.WebUrl}",
            Icon = TaskDialogIcon.ShieldSuccessGreenBar,
            Buttons = { openButton, TaskDialogButton.Close },
            AllowCancel = true
        };

        if (TaskDialog.ShowDialog(this, page) == openButton)
        {
            _plugin.OpenUrl(mergeRequest.WebUrl);
        }
    }

    private void LoadDescriptionTemplate()
    {
        // GitLab uses .gitlab/merge_request_templates/Default.md as default description.
        string templatePath = Path.Join(Module.WorkingDir, ".gitlab", "merge_request_templates", "Default.md");
        try
        {
            if (File.Exists(templatePath))
            {
                _descriptionTB.Text = File.ReadAllText(templatePath).ReplaceLineEndings(Environment.NewLine);
            }
        }
        catch (IOException)
        {
            // The template is a convenience only.
        }
    }

    #endregion

    private void SetStatus(string? status)
    {
        _statusLabel.Text = status;
    }

    private static ComboBox CreateComboBox(string name, ComboBoxStyle style) => new()
    {
        Name = name,
        Dock = DockStyle.Fill,
        DropDownStyle = style,
        AutoCompleteMode = style == ComboBoxStyle.DropDown ? AutoCompleteMode.SuggestAppend : AutoCompleteMode.None,
        AutoCompleteSource = style == ComboBoxStyle.DropDown ? AutoCompleteSource.ListItems : AutoCompleteSource.None,
        MaxDropDownItems = 20
    };

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left | AnchorStyles.Top,
        Margin = new Padding(3, 7, 6, 3)
    };

    private sealed class ProjectItem(Project project, string? remoteName)
    {
        public Project Project { get; } = project;
        public string? RemoteName { get; } = remoteName;

        public override string ToString() => RemoteName is null ? Project.PathWithNamespace : $"{Project.PathWithNamespace} ({RemoteName})";
    }

    private sealed class UserItem(User? user, string text)
    {
        public User? User { get; } = user;

        public override string ToString() => text;
    }
}
