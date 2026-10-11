using System.ComponentModel.Composition;
using GitCommands;
using GitCommands.Config;
using GitExtensions.Extensibility;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Plugins;
using GitExtensions.Extensibility.Settings;
using GitExtensions.GitLab.Api;
using GitExtensions.GitLab.Api.Models;
using GitExtensions.GitLab.Forms;
using GitExtensions.GitLab.Hosting;
using GitUI;
using GitUIPluginInterfaces.RepositoryHosts;
using ResourceManager;
using MessageBoxes = GitExtensions.Extensibility.MessageBoxes;

namespace GitExtensions.GitLab;

/// <summary>
///  GitLab repository host: provides the GitLab merge request dialogs and the Git Extensions
///  "Repository hosts" features (pull requests, fork and clone, upstream remote, blame links).
/// </summary>
[Export(typeof(IGitPlugin))]
[Export(typeof(IRepositoryHostPlugin))]
public sealed class GitLabPlugin : GitPluginBase, IRepositoryHostPlugin
{
    private readonly TranslationString _viewInWebSite = new("View in {0}");
    private readonly TranslationString _createToken = new("Create a personal access token on GitLab (scope: api)");
    private readonly TranslationString _openLinkFailed = new("Failed to open the link: ");
    private readonly TranslationString _settingsLevelHint = new("Tip: the GitLab URL and the token can be set per repository (local or distributed settings) to work with several GitLab instances.");
    private readonly TranslationString _invalidInstanceUrl = new("The GitLab URL \"{0}\" configured in the GitLab plugin settings is not a valid http(s) URL.");
    private readonly TranslationString _noGitLabRemote = new("No remote of this repository points to the GitLab instance {0}.\r\n\r\nCheck the GitLab URL in the GitLab plugin settings.");
    private readonly TranslationString _error = new("Error");

    public const string DefaultGitLabUrl = "https://gitlab.com";
    private const string UpstreamRemoteName = "upstream";

    internal readonly StringSetting GitLabUrl = new("GitLabUrl", "GitLab URL", DefaultGitLabUrl);
    internal readonly PasswordSetting AccessToken = new("PersonalAccessToken", "Personal access token", "");

    private readonly Lock _clientLock = new();
    private readonly Dictionary<string, GitLabHostedRemote> _hostedRemotes = [];
    private GitLabClient? _client;
    private string? _clientToken;
    private IGitUICommands? _gitUICommands;

    public GitLabPlugin()
        : base(hasSettings: true)
    {
        Id = new Guid("80AFF0FD-22DF-4630-A560-E9DB6CCD6674");
        Name = "GitLab";
        Translate(AppSettings.CurrentTranslation);

        Instance ??= this;

        Icon = Images.GitLab;
    }

    internal static GitLabPlugin? Instance { get; private set; }

    internal IGitUICommands? UICommands => _gitUICommands;

    #region IGitPlugin

    public override IEnumerable<ISetting> GetSettings()
    {
        yield return GitLabUrl;
        yield return AccessToken;

        LinkLabel createTokenLink = new() { Text = _createToken.Text, AutoSize = true };
        createTokenLink.Click += (_, _) => OpenCreateTokenPage();
        yield return new PseudoSetting(createTokenLink);

        yield return new PseudoSetting(_settingsLevelHint.Text, height: 40, textboxSettings: textBox => textBox.WordWrap = true);
    }

    public override void Register(IGitUICommands gitUiCommands)
    {
        base.Register(gitUiCommands);
        _gitUICommands = gitUiCommands;
        ClearHostedRemotes();
    }

    public override void Unregister(IGitUICommands gitUiCommands)
    {
        _gitUICommands = null;
        ClearHostedRemotes();
        base.Unregister(gitUiCommands);
    }

    /// <summary>
    ///  Opens the merge requests dialog, or the plugin settings when the plugin is not configured yet.
    /// </summary>
    public override bool Execute(GitUIEventArgs args)
    {
        if (!EnsureConfigured(args.GitUICommands))
        {
            return false;
        }

        if (!EnsureRelevantRepository(args.OwnerForm))
        {
            return false;
        }

        args.GitUICommands.ShowModelessForm(args.OwnerForm, requiresValidWorkingDir: true, preEvent: null, postEvent: null,
            () => new MergeRequestsForm(args.GitUICommands, this));
        return false;
    }

    /// <summary>
    ///  Opens the settings when no access token is configured. Returns whether the plugin can be used.
    /// </summary>
    internal bool EnsureConfigured(IGitUICommands commands)
    {
        if (!ConfigurationOk)
        {
            commands.StartSettingsDialog(this);
        }

        return ConfigurationOk;
    }

    internal bool EnsureRelevantRepository(IWin32Window? owner)
    {
        if (!TryGetInstanceUrl(out Uri? instanceUrl))
        {
            MessageBoxes.ShowError(owner, string.Format(_invalidInstanceUrl.Text, GitLabUrl.ValueOrDefault(Settings)), _error.Text);
            return false;
        }

        if (!GitModuleIsRelevantToMe())
        {
            MessageBoxes.ShowError(owner, string.Format(_noGitLabRemote.Text, instanceUrl), _error.Text);
            return false;
        }

        return true;
    }

    private void OpenCreateTokenPage()
    {
        // The URL being edited in the settings dialog is not saved until OK or Apply.
        if (!GitLabRemoteParser.TryParseInstanceUrl(GitLabUrl.CustomControl?.Text, out Uri? instanceUrl)
            && !TryGetInstanceUrl(out instanceUrl))
        {
            instanceUrl = new Uri(DefaultGitLabUrl);
        }

        OpenUrl($"{instanceUrl.AbsoluteUri.TrimEnd('/')}/-/user_settings/personal_access_tokens?name=Git%20Extensions&scopes=api");
    }

    internal void OpenUrl(string url)
    {
        try
        {
            OsShellUtil.OpenUrlInDefaultBrowser(url);
        }
        catch (Exception ex)
        {
            MessageBoxes.ShowError(owner: null, _openLinkFailed.Text + ex.Message, _error.Text);
        }
    }

    #endregion

    #region GitLab client

    /// <summary>
    ///  Gets the client for the GitLab instance and the token of the current settings (which may be repository specific).
    /// </summary>
    internal GitLabClient Client
    {
        get
        {
            if (!TryGetInstanceUrl(out Uri? instanceUrl))
            {
                throw new InvalidOperationException(string.Format(_invalidInstanceUrl.Text, GitLabUrl.ValueOrDefault(Settings)));
            }

            string token = AccessToken.ValueOrDefault(Settings).Trim();
            lock (_clientLock)
            {
                if (_client is null || _client.InstanceUrl != instanceUrl || _clientToken != token)
                {
                    // The previous client is not disposed: requests may still be running on it.
                    _client = new GitLabClient(instanceUrl, token);
                    _clientToken = token;
                    ClearHostedRemotes();
                }

                return _client;
            }
        }
    }

    internal bool TryGetInstanceUrl([System.Diagnostics.CodeAnalysis.NotNullWhen(returnValue: true)] out Uri? instanceUrl)
        => GitLabRemoteParser.TryParseInstanceUrl(GitLabUrl.ValueOrDefault(Settings), out instanceUrl);

    /// <summary>
    ///  Gets the owner of the access token, or <see langword="null"/> when GitLab cannot be reached.
    /// </summary>
    internal User? TryGetCurrentUser()
    {
        if (!ConfigurationOk)
        {
            return null;
        }

        try
        {
            GitLabClient client = Client;
            return ThreadHelper.JoinableTaskFactory.Run(() => client.GetCurrentUserAsync());
        }
        catch (Exception)
        {
            return null;
        }
    }

    internal string? CurrentUsername => TryGetCurrentUser()?.Username;

    #endregion

    #region Hosted remotes

    /// <summary>
    ///  Returns the remotes of the current repository hosted on the configured GitLab instance.
    ///  No network access: the projects are loaded on demand.
    /// </summary>
    internal IReadOnlyList<GitLabHostedRemote> GetHostedRemotes()
    {
        IGitModule? module = _gitUICommands?.Module;
        if (module is null || !module.IsValidGitWorkingDir() || !TryGetInstanceUrl(out Uri? instanceUrl))
        {
            return [];
        }

        List<GitLabHostedRemote> hostedRemotes = [];
        foreach (string remoteName in module.GetRemoteNames())
        {
            string url = module.GetSetting(string.Format(SettingKeyString.RemoteUrl, remoteName));
            if (string.IsNullOrEmpty(url) || !GitLabRemoteParser.TryParse(url, instanceUrl, out string? projectPath))
            {
                continue;
            }

            string key = $"{instanceUrl}\n{remoteName}\n{url}";
            lock (_hostedRemotes)
            {
                if (!_hostedRemotes.TryGetValue(key, out GitLabHostedRemote? hostedRemote))
                {
                    hostedRemote = new GitLabHostedRemote(this, remoteName, url, projectPath, instanceUrl);
                    _hostedRemotes[key] = hostedRemote;
                }

                hostedRemotes.Add(hostedRemote);
            }
        }

        return hostedRemotes;
    }

    private void ClearHostedRemotes()
    {
        lock (_hostedRemotes)
        {
            _hostedRemotes.Clear();
        }
    }

    #endregion

    #region IRepositoryHostPlugin

    public bool ConfigurationOk => !string.IsNullOrWhiteSpace(AccessToken.ValueOrDefault(Settings));

    public bool GitModuleIsRelevantToMe() => GetHostedRemotes().Count > 0;

    public IReadOnlyList<IHostedRemote> GetHostedRemotesForModule() => GetHostedRemotes();

    public string? OwnerLogin => CurrentUsername;

    public IReadOnlyList<IHostedRepository> SearchForRepository(string search)
        => ToHostedRepositories(client => client.SearchProjectsAsync(search));

    public IReadOnlyList<IHostedRepository> GetRepositoriesOfUser(string user)
        => ToHostedRepositories(client => client.GetNamespaceProjectsAsync(user));

    public IReadOnlyList<IHostedRepository> GetMyRepos()
        => ToHostedRepositories(client => client.GetMyProjectsAsync());

    public IHostedRepository GetRepository(string user, string repositoryName)
    {
        GitLabClient client = Client;
        return new GitLabHostedRepository(this, ThreadHelper.JoinableTaskFactory.Run(() => client.GetProjectAsync($"{user}/{repositoryName}")));
    }

    private List<IHostedRepository> ToHostedRepositories(Func<GitLabClient, Task<IReadOnlyList<Project>>> getProjects)
    {
        GitLabClient client = Client;
        return [.. ThreadHelper.JoinableTaskFactory.Run(() => getProjects(client))
            .Select(project => (IHostedRepository)new GitLabHostedRepository(this, project))];
    }

    public async Task<string?> AddUpstreamRemoteAsync()
    {
        IGitModule? module = _gitUICommands?.Module;
        GitLabHostedRemote? myRemote = GetHostedRemotes().FirstOrDefault(remote => remote.IsOwnedByMe);
        if (module is null || myRemote is null)
        {
            return null;
        }

        GitLabHostedRepository repository = myRemote.GetRepository();
        string? parentUrl = repository.ParentUrl;
        if (parentUrl is null)
        {
            return null;
        }

        IReadOnlyList<Remote> remotes = await module.GetRemotesAsync();
        if (remotes.Any(remote => remote.Name == UpstreamRemoteName || remote.FetchUrl == parentUrl))
        {
            return null;
        }

        module.AddRemote(UpstreamRemoteName, parentUrl);
        return UpstreamRemoteName;
    }

    public void ConfigureContextMenu(ContextMenuStrip contextMenu)
    {
        const string hostedRemoteMenuItem = "GitLabHostedRemoteMenuItem";

        for (int i = contextMenu.Items.Count - 1; i >= 0; i--)
        {
            if (contextMenu.Items[i] is ToolStripMenuItem { Tag: hostedRemoteMenuItem })
            {
                contextMenu.Items.RemoveAt(i);
            }
        }

        IReadOnlyList<GitLabHostedRemote> hostedRemotes = GetHostedRemotes();
        if (hostedRemotes.Count == 0)
        {
            return;
        }

        ToolStripMenuItem viewInGitLabItem = new(string.Format(_viewInWebSite.Text, Name), Icon) { Tag = hostedRemoteMenuItem };
        contextMenu.Items.Add(viewInGitLabItem);

        foreach (GitLabHostedRemote hostedRemote in hostedRemotes.OrderBy(remote => remote.DisplayData))
        {
            ToolStripItem item = viewInGitLabItem.DropDownItems.Add(hostedRemote.DisplayData);
            item.Click += (_, _) =>
            {
                if (contextMenu.Tag is GitBlameContext blameContext)
                {
                    OpenUrl(hostedRemote.GetBlameUrl(blameContext.BlameId.ToString(), blameContext.FileName, blameContext.LineIndex + 1));
                }
            };
        }
    }

    #endregion
}
