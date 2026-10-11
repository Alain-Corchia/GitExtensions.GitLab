using System.Diagnostics;
using GitExtensions.Extensibility.Plugins;
using GitUI;
using GitUIPluginInterfaces.RepositoryHosts;

namespace GitExtensions.GitLab.Hosting;

/// <summary>
///  A remote of the local repository pointing to a project of the configured GitLab instance.
/// </summary>
[DebuggerDisplay("{DisplayData}")]
internal sealed class GitLabHostedRemote : IHostedRemote
{
    private readonly GitLabPlugin _plugin;
    private GitLabHostedRepository? _repository;

    public GitLabHostedRemote(GitLabPlugin plugin, string name, string remoteUrl, string projectPath, Uri instanceUrl)
    {
        _plugin = plugin;
        Name = name;
        RemoteUrl = remoteUrl;
        ProjectPath = projectPath;
        int lastSlash = projectPath.LastIndexOf('/');
        Owner = projectPath[..lastSlash];
        RemoteRepositoryName = projectPath[(lastSlash + 1)..];
        CloneProtocol = remoteUrl.IsUrlUsingHttp() ? GitProtocol.Https : GitProtocol.Ssh;
        WebUrl = $"{instanceUrl.AbsoluteUri.TrimEnd('/')}/{projectPath}";
    }

    /// <summary>
    ///  Name of the remote in the local repository, e.g. "origin".
    /// </summary>
    public string Name { get; }

    /// <summary>
    ///  Full path of the project, e.g. "group/subgroup/project".
    /// </summary>
    public string ProjectPath { get; }

    /// <summary>
    ///  Namespace of the project, e.g. "group/subgroup".
    /// </summary>
    public string Owner { get; }

    /// <summary>
    ///  Path of the project in its namespace, e.g. "project".
    /// </summary>
    public string RemoteRepositoryName { get; }

    public string RemoteUrl { get; }

    public GitProtocol CloneProtocol { get; }

    public string WebUrl { get; }

    public string Data => ProjectPath;

    public string DisplayData => $"{ProjectPath} ({Name})";

    /// <summary>
    ///  Whether the project is in the personal namespace of the token owner (typically a fork).
    /// </summary>
    public bool IsOwnedByMe => string.Equals(Owner, _plugin.CurrentUsername, StringComparison.OrdinalIgnoreCase);

    public IHostedRepository GetHostedRepository() => GetRepository();

    public GitLabHostedRepository GetRepository()
    {
        if (_repository is null)
        {
            Api.GitLabClient client = _plugin.Client;
            _repository = new GitLabHostedRepository(_plugin, ThreadHelper.JoinableTaskFactory.Run(() => client.GetProjectAsync(ProjectPath)))
            {
                CloneProtocol = CloneProtocol
            };
        }

        return _repository;
    }

    public string GetBlameUrl(string commitHash, string fileName, int lineIndex)
        => $"{WebUrl}/-/blame/{commitHash}/{EscapePath(fileName)}#L{lineIndex}";

    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    public override string ToString() => DisplayData;
}
