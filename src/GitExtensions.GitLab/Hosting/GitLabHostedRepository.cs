using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Plugins;
using GitExtensions.GitLab.Api;
using GitExtensions.GitLab.Api.Models;
using GitUI;

namespace GitExtensions.GitLab.Hosting;

internal sealed class GitLabHostedRepository : IHostedRepository
{
    private readonly GitLabPlugin _plugin;
    private Project? _parent;

    public GitLabHostedRepository(GitLabPlugin plugin, Project project)
    {
        _plugin = plugin;
        Project = project;
    }

    public Project Project { get; }

    public long Id => Project.Id;
    public string? Owner => Project.Namespace?.FullPath;
    public string Name => Project.Path;
    public string Description => Project.Description ?? "";
    public bool IsAFork => Project.ForkedFromProject is not null;
    public bool IsMine => string.Equals(Owner, _plugin.CurrentUsername, StringComparison.OrdinalIgnoreCase);
    public bool IsPrivate => Project.Visibility == "private";
    public int Forks => Project.ForksCount;
    public string Homepage => Project.WebUrl;

    public string? ParentUrl => Parent is { } parent ? GetCloneUrl(parent) : null;
    public string? ParentOwner => Parent?.Namespace?.FullPath;

    public string CloneUrl => GetCloneUrl(Project);

    public GitProtocol CloneProtocol { get; set; } = GitProtocol.Ssh;

    public IReadOnlyList<GitProtocol> SupportedCloneProtocols { get; set; } = [GitProtocol.Ssh, GitProtocol.Https];

    /// <summary>
    ///  The project this one was forked from, with its clone URLs.
    /// </summary>
    private Project? Parent
    {
        get
        {
            if (Project.ForkedFromProject is not { } forkedFrom)
            {
                return null;
            }

            if (_parent is null)
            {
                GitLabClient client = _plugin.Client;
                _parent = !string.IsNullOrEmpty(forkedFrom.HttpUrlToRepo) && forkedFrom.Namespace is not null
                    ? forkedFrom
                    : ThreadHelper.JoinableTaskFactory.Run(() => client.GetProjectAsync(forkedFrom.Id));
            }

            return _parent;
        }
    }

    private string GetCloneUrl(Project project) => CloneProtocol == GitProtocol.Ssh ? project.SshUrlToRepo : project.HttpUrlToRepo;

    public IReadOnlyList<IHostedBranch> GetBranches()
    {
        GitLabClient client = _plugin.Client;
        return [.. ThreadHelper.JoinableTaskFactory.Run(() => client.GetBranchesAsync(Id))
            .Select(branch => ObjectId.TryParse(branch.Commit.Id, out ObjectId sha) ? new GitLabHostedBranch(branch.Name, sha) : null)
            .OfType<GitLabHostedBranch>()
            .OrderBy(branch => branch.Name, StringComparer.OrdinalIgnoreCase)];
    }

    public string GetDefaultBranch() => Project.DefaultBranch ?? "";

    public IHostedRepository Fork()
    {
        GitLabClient client = _plugin.Client;
        return new GitLabHostedRepository(_plugin, ThreadHelper.JoinableTaskFactory.Run(() => client.ForkProjectAsync(Id)));
    }

    public IReadOnlyList<IPullRequestInformation> GetPullRequests()
    {
        GitLabClient client = _plugin.Client;
        return [.. ThreadHelper.JoinableTaskFactory.Run(() => client.GetMergeRequestsAsync(Id, MergeRequestState.Opened, MergeRequestScope.All))
            .Select(mergeRequest => new GitLabPullRequest(_plugin, this, mergeRequest))];
    }

    /// <summary>
    ///  Called by the Git Extensions "Create pull request" dialog on the target repository.
    ///  The source branch is in the project owned by the user (fork) if there is one, else in this project.
    /// </summary>
    public int CreatePullRequest(string myBranch, string remoteBranch, string title, string body)
    {
        GitLabHostedRepository source = _plugin.GetHostedRemotes().FirstOrDefault(remote => remote.IsOwnedByMe)?.GetRepository() ?? this;
        CreateMergeRequestRequest request = new()
        {
            SourceBranch = myBranch,
            TargetBranch = remoteBranch,
            Title = title,
            Description = body,
            TargetProjectId = Id,
            RemoveSourceBranch = true
        };

        GitLabClient client = _plugin.Client;
        MergeRequest mergeRequest = ThreadHelper.JoinableTaskFactory.Run(() => client.CreateMergeRequestAsync(source.Id, request));
        return checked((int)mergeRequest.Iid);
    }

    public override string ToString() => Project.PathWithNamespace;
}

internal sealed class GitLabHostedBranch(string name, ObjectId sha) : IHostedBranch
{
    public string Name { get; } = name;
    public ObjectId Sha { get; } = sha;

    public override string ToString() => Name;
}
