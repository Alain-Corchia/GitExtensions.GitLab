using GitExtensions.Extensibility.Plugins;
using GitExtensions.GitLab.Api;
using GitExtensions.GitLab.Api.Models;
using GitUI;

namespace GitExtensions.GitLab.Hosting;

/// <summary>
///  A merge request, as shown by the Git Extensions "View pull requests" dialog.
/// </summary>
internal sealed class GitLabPullRequest : IPullRequestInformation
{
    private readonly GitLabPlugin _plugin;
    private readonly GitLabHostedRepository _targetRepository;
    private readonly MergeRequest _mergeRequest;
    private GitLabHostedRepository? _sourceRepository;
    private string? _diffData;
    private GitLabPullRequestDiscussion? _discussion;

    public GitLabPullRequest(GitLabPlugin plugin, GitLabHostedRepository targetRepository, MergeRequest mergeRequest)
    {
        _plugin = plugin;
        _targetRepository = targetRepository;
        _mergeRequest = mergeRequest;
    }

    public string Title => _mergeRequest.Draft ? $"[Draft] {_mergeRequest.Title}" : _mergeRequest.Title;
    public string Body => _mergeRequest.Description ?? "";
    public string Owner => _mergeRequest.Author.Username;
    public DateTime Created => _mergeRequest.CreatedAt.LocalDateTime;

    public IHostedRepository BaseRepo => _targetRepository;

    public IHostedRepository HeadRepo
    {
        get
        {
            if (_mergeRequest.SourceProjectId == _targetRepository.Id)
            {
                return _targetRepository;
            }

            if (_sourceRepository is null)
            {
                GitLabClient client = _plugin.Client;
                _sourceRepository = new GitLabHostedRepository(_plugin, ThreadHelper.JoinableTaskFactory.Run(() => client.GetProjectAsync(_mergeRequest.SourceProjectId)));
            }

            return _sourceRepository;
        }
    }

    public string BaseSha => _mergeRequest.DiffRefs?.BaseSha ?? "";
    public string HeadSha => _mergeRequest.Sha ?? "";
    public string BaseRef => _mergeRequest.TargetBranch;
    public string HeadRef => _mergeRequest.SourceBranch;
    public string Id => _mergeRequest.Iid.ToString();

    public string DetailedInfo => $"Source: {HeadRepo.Owner}/{HeadRepo.Name}:{HeadRef}\nTarget: {BaseRepo.Owner}/{BaseRepo.Name}:{BaseRef}";

    public string FetchBranch => $"mr/{_mergeRequest.Iid}_{_mergeRequest.SourceBranch}";

    public async Task<string> GetDiffDataAsync()
    {
        if (_diffData is null)
        {
            IReadOnlyList<FileDiff> diffs = await _plugin.Client.GetMergeRequestDiffsAsync(_mergeRequest.ProjectId, _mergeRequest.Iid);
            _diffData = UnifiedDiff.Format(diffs);
        }

        return _diffData;
    }

    public void Close()
    {
        GitLabClient client = _plugin.Client;
        ThreadHelper.JoinableTaskFactory.Run(() => client.CloseMergeRequestAsync(_mergeRequest.ProjectId, _mergeRequest.Iid));
    }

    public IPullRequestDiscussion GetDiscussion() => _discussion ??= new GitLabPullRequestDiscussion(_plugin.Client, _mergeRequest);
}
