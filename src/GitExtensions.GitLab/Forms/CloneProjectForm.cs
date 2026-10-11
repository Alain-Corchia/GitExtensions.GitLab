using GitCommands;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Plugins;
using GitExtensions.GitLab.Api;
using GitExtensions.GitLab.Api.Models;
using GitUI;
using GitUIPluginInterfaces.RepositoryHosts;
using ResourceManager;
using MessageBoxes = GitExtensions.Extensibility.MessageBoxes;

namespace GitExtensions.GitLab.Forms;

/// <summary>
///  Clones a GitLab project, or forks it first. Replaces the Git Extensions "Fork/Clone" dialog, which shows
///  only the project names: with forks, many projects of the list have the same name.
/// </summary>
internal sealed class CloneProjectForm : GitModuleForm
{
    private readonly TranslationString _caption = new("Clone GitLab project");
    private readonly TranslationString _loading = new("Loading...");
    private readonly TranslationString _projectCount = new("{0} of {1} projects");
    private readonly TranslationString _loadFailed = new("The projects could not be loaded: {0}");
    private readonly TranslationString _searchFailed = new("The search failed: {0}");
    private readonly TranslationString _addUpstream = new("Add the parent project {0} as remote \"upstream\"");
    private readonly TranslationString _addUpstreamNotFork = new("Add the parent project as remote \"upstream\" (the project is not a fork)");
    private readonly TranslationString _targetExists = new("The folder {0} already exists and is not empty.");
    private readonly TranslationString _openClonedRepository = new("Open the cloned repository {0}?");
    private readonly TranslationString _confirmFork = new("Fork {0} into your namespace {1}?");
    private readonly TranslationString _forkFailed = new("The project could not be forked: {0}");
    private readonly TranslationString _forked = new("{0} has been forked into {1}.\r\n\r\nGitLab may take a moment to copy the repository before it can be cloned.");

    private const string UpstreamRemoteName = "upstream";

    private readonly GitLabClient _client;
    private readonly List<Project> _projects = [];
    private string? _username;

    private readonly TextBox _filterTB = new() { Width = 260, PlaceholderText = "Filter by path or description" };
    private readonly ComboBox _kindCB = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    private readonly Button _searchButton = new() { Text = "Search all GitLab", AutoSize = true };
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false, HideSelection = false };
    private readonly Label _countLabel = new() { AutoSize = true, Margin = new Padding(6, 9, 0, 0) };
    private readonly ComboBox _protocolCB = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
    private readonly TextBox _parentFolderTB = new() { Dock = DockStyle.Fill };
    private readonly Button _browseButton = new() { Text = "...", AutoSize = true };
    private readonly TextBox _folderNameTB = new() { Dock = DockStyle.Fill };
    private readonly CheckBox _addUpstreamCB = new() { AutoSize = true, Checked = true };
    private readonly Label _cloneUrlLabel = new() { AutoSize = true, AutoEllipsis = true };
    private readonly Button _forkButton = new() { Text = "Fork...", AutoSize = true, Enabled = false };
    private readonly Button _cloneButton = new() { Text = "Clone", AutoSize = true, Enabled = false };
    private readonly Button _closeButton = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };

    public CloneProjectForm(IGitUICommands commands, GitLabPlugin plugin)
        : base(commands)
    {
        _client = plugin.Client;

        BuildLayout();
        InitializeComplete();

        Text = _caption.Text;
    }

    private enum ProjectKind
    {
        All,
        WithoutForks,
        ForksOnly,
        Mine
    }

    private void BuildLayout()
    {
        SuspendLayout();

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1000, 680);
        MinimumSize = new Size(700, 450);
        StartPosition = FormStartPosition.CenterParent;
        Icon = Images.GitLabIcon;
        CancelButton = _closeButton;

        _kindCB.Items.AddRange([
            new Choice<ProjectKind>(ProjectKind.All, "All projects"),
            new Choice<ProjectKind>(ProjectKind.WithoutForks, "Without forks"),
            new Choice<ProjectKind>(ProjectKind.ForksOnly, "Forks only"),
            new Choice<ProjectKind>(ProjectKind.Mine, "My projects")]);
        _kindCB.SelectedIndex = 1;

        _protocolCB.Items.AddRange([GitProtocol.Https, GitProtocol.Ssh]);

        FlowLayoutPanel toolbar = new() { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(6, 6, 6, 2) };
        toolbar.Controls.AddRange([CreateLabel("Filter"), _filterTB, CreateLabel("Show"), _kindCB, _searchButton, _countLabel]);

        _list.Columns.Add("Project", 330);
        _list.Columns.Add("Fork of", 220);
        _list.Columns.Add("Visibility", 80);
        _list.Columns.Add("Last activity", 120);
        _list.Columns.Add("Description", 400);

        TableLayoutPanel clonePanel = new() { Dock = DockStyle.Bottom, AutoSize = true, ColumnCount = 3, Padding = new Padding(6) };
        clonePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        clonePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        clonePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        clonePanel.Controls.Add(CreateLabel("Protocol"), 0, 0);
        clonePanel.Controls.Add(_protocolCB, 1, 0);
        clonePanel.Controls.Add(CreateLabel("Clone into"), 0, 1);
        clonePanel.Controls.Add(_parentFolderTB, 1, 1);
        clonePanel.Controls.Add(_browseButton, 2, 1);
        clonePanel.Controls.Add(CreateLabel("Folder name"), 0, 2);
        clonePanel.Controls.Add(_folderNameTB, 1, 2);
        clonePanel.Controls.Add(_addUpstreamCB, 1, 3);
        clonePanel.Controls.Add(_cloneUrlLabel, 1, 4);

        FlowLayoutPanel buttons = new() { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(6) };
        buttons.Controls.AddRange([_closeButton, _cloneButton, _forkButton]);

        Panel listPanel = new() { Dock = DockStyle.Fill, Padding = new Padding(6, 0, 6, 0) };
        listPanel.Controls.Add(_list);

        Controls.Add(listPanel);
        Controls.Add(clonePanel);
        Controls.Add(buttons);
        Controls.Add(toolbar);

        _filterTB.TextChanged += (_, _) => FillList();
        _kindCB.SelectedIndexChanged += (_, _) => FillList();
        _searchButton.Click += (_, _) => SearchAllProjects();
        _list.SelectedIndexChanged += (_, _) => UpdateCloneInfo();
        _list.DoubleClick += (_, _) => Clone();
        _protocolCB.SelectedIndexChanged += (_, _) => UpdateCloneInfo();
        _parentFolderTB.TextChanged += (_, _) => UpdateCloneInfo();
        _folderNameTB.TextChanged += (_, _) => UpdateCloneInfo();
        _browseButton.Click += (_, _) => BrowseParentFolder();
        _forkButton.Click += (_, _) => Fork();
        _cloneButton.Click += (_, _) => Clone();

        ResumeLayout(performLayout: false);
        PerformLayout();
    }

    protected override void OnRuntimeLoad(EventArgs e)
    {
        base.OnRuntimeLoad(e);

        // Same protocol as the current repository, and its parent folder as default destination.
        string workingDir = Module.WorkingDir.TrimEnd('\\', '/');
        string remoteUrl = Module.GetSetting($"remote.{Module.GetCurrentRemote()}.url");
        _protocolCB.SelectedItem = remoteUrl.IsUrlUsingHttp() || string.IsNullOrEmpty(remoteUrl) ? GitProtocol.Https : GitProtocol.Ssh;
        _parentFolderTB.Text = !string.IsNullOrWhiteSpace(AppSettings.DefaultCloneDestinationPath)
            ? AppSettings.DefaultCloneDestinationPath
            : Path.GetDirectoryName(workingDir) ?? workingDir;

        LoadProjects();
    }

    #region Project list

    private void LoadProjects()
    {
        _countLabel.Text = _loading.Text;
        ThreadHelper.FileAndForget(async () =>
        {
            IReadOnlyList<Project> projects;
            string? username;
            try
            {
                projects = await _client.GetMyProjectsAsync();
                username = (await _client.GetCurrentUserAsync()).Username;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await this.SwitchToMainThreadAsync();
                _countLabel.Text = string.Format(_loadFailed.Text, ex.Message);
                return;
            }

            await this.SwitchToMainThreadAsync();
            _username = username;
            AddProjects(projects);
        });
    }

    private void SearchAllProjects()
    {
        string search = _filterTB.Text.Trim();
        if (search.Length == 0)
        {
            return;
        }

        _searchButton.Enabled = false;
        ThreadHelper.FileAndForget(async () =>
        {
            try
            {
                IReadOnlyList<Project> projects = await _client.SearchProjectsAsync(search);
                await this.SwitchToMainThreadAsync();
                AddProjects(projects);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await this.SwitchToMainThreadAsync();
                MessageBoxes.ShowError(this, string.Format(_searchFailed.Text, ex.Message), Text);
            }
            finally
            {
                await this.SwitchToMainThreadAsync();
                _searchButton.Enabled = true;
            }
        });
    }

    private void AddProjects(IEnumerable<Project> projects)
    {
        foreach (Project project in projects)
        {
            if (!_projects.Any(known => known.Id == project.Id))
            {
                _projects.Add(project);
            }
        }

        FillList();
    }

    private void FillList()
    {
        long? selectedId = SelectedProject?.Id;
        ProjectKind kind = (_kindCB.SelectedItem as Choice<ProjectKind>)?.Value ?? ProjectKind.All;
        string filter = _filterTB.Text.Trim();

        Project[] visible = [.. _projects
            .Where(project => kind switch
            {
                ProjectKind.WithoutForks => project.ForkedFromProject is null,
                ProjectKind.ForksOnly => project.ForkedFromProject is not null,
                ProjectKind.Mine => string.Equals(project.Namespace?.FullPath, _username, StringComparison.OrdinalIgnoreCase),
                _ => true
            })
            .Where(project => filter.Length == 0
                || project.PathWithNamespace.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || project.Description?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(project => project.PathWithNamespace, StringComparer.OrdinalIgnoreCase)];

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (Project project in visible)
        {
            ListViewItem item = new([
                project.PathWithNamespace,
                project.ForkedFromProject?.PathWithNamespace ?? "",
                project.Visibility ?? "",
                project.LastActivityAt?.LocalDateTime.ToString("d") ?? "",
                project.Description?.ReplaceLineEndings(" ") ?? ""])
            {
                Tag = project
            };
            _list.Items.Add(item);
            if (project.Id == selectedId)
            {
                item.Selected = true;
            }
        }

        _list.EndUpdate();
        _countLabel.Text = string.Format(_projectCount.Text, visible.Length, _projects.Count);
        UpdateCloneInfo();
    }

    private Project? SelectedProject => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].Tag as Project : null;

    #endregion

    #region Clone and fork

    private string GetCloneUrl(Project project)
        => _protocolCB.SelectedItem is GitProtocol.Ssh ? project.SshUrlToRepo : project.HttpUrlToRepo;

    private string TargetFolder => Path.Combine(_parentFolderTB.Text.Trim(), _folderNameTB.Text.Trim());

    private Project? _infoProject;

    private void UpdateCloneInfo()
    {
        Project? project = SelectedProject;
        if (project != _infoProject)
        {
            _infoProject = project;
            _folderNameTB.Text = project?.Path ?? "";
        }

        _forkButton.Enabled = project is not null;
        _addUpstreamCB.Enabled = project?.ForkedFromProject is not null;
        _addUpstreamCB.Text = project?.ForkedFromProject is { } parent
            ? string.Format(_addUpstream.Text, parent.PathWithNamespace)
            : _addUpstreamNotFork.Text;

        _cloneUrlLabel.Text = project is null ? "" : $"git clone {GetCloneUrl(project)} \"{TargetFolder}\"";
        _cloneButton.Enabled = project is not null && _parentFolderTB.Text.Trim().Length > 0 && _folderNameTB.Text.Trim().Length > 0;
    }

    private void BrowseParentFolder()
    {
        using FolderBrowserDialog dialog = new() { InitialDirectory = _parentFolderTB.Text, UseDescriptionForTitle = true, Description = "Clone into" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _parentFolderTB.Text = dialog.SelectedPath;
        }
    }

    private void Clone()
    {
        if (SelectedProject is not { } project || !_cloneButton.Enabled)
        {
            return;
        }

        string target = TargetFolder;
        if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any())
        {
            MessageBoxes.ShowError(this, string.Format(_targetExists.Text, target), Text);
            return;
        }

        if (!UICommands.StartGitCommandProcessDialog(this, $"clone --progress \"{GetCloneUrl(project)}\" \"{target}\""))
        {
            return;
        }

        if (_addUpstreamCB.Enabled && _addUpstreamCB.Checked && project.ForkedFromProject is { } parent)
        {
            string parentUrl = GetCloneUrl(parent);
            if (string.IsNullOrEmpty(parentUrl))
            {
                // The parent is not always returned with its clone URLs.
                GitLabClient client = _client;
                parentUrl = GetCloneUrl(ThreadHelper.JoinableTaskFactory.Run(() => client.GetProjectAsync(parent.Id)));
            }

            UICommands.StartGitCommandProcessDialog(this, $"-C \"{target}\" remote add {UpstreamRemoteName} \"{parentUrl}\"");
        }

        if (MessageBoxes.Confirm(this, string.Format(_openClonedRepository.Text, target), Text))
        {
            DialogResult = DialogResult.OK;
            Close();
            UICommands.BrowseRepo?.SetWorkingDir(target);
        }
    }

    private void Fork()
    {
        if (SelectedProject is not { } project
            || !MessageBoxes.Confirm(this, string.Format(_confirmFork.Text, project.PathWithNamespace, _username), Text))
        {
            return;
        }

        _forkButton.Enabled = false;
        UseWaitCursor = true;
        ThreadHelper.FileAndForget(async () =>
        {
            Project fork;
            try
            {
                fork = await _client.ForkProjectAsync(project.Id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await this.SwitchToMainThreadAsync();
                UseWaitCursor = false;
                UpdateCloneInfo();
                MessageBoxes.ShowError(this, string.Format(_forkFailed.Text, ex.Message), Text);
                return;
            }

            await this.SwitchToMainThreadAsync();
            UseWaitCursor = false;
            AddProjects([fork]);
            foreach (ListViewItem item in _list.Items)
            {
                item.Selected = (item.Tag as Project)?.Id == fork.Id;
            }

            MessageBoxes.Show(this, string.Format(_forked.Text, project.PathWithNamespace, fork.PathWithNamespace), Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    #endregion

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(6, 7, 0, 0)
    };
}
