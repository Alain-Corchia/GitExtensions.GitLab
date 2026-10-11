using System.Net;
using GitExtensions.Extensibility.Plugins;
using GitExtensions.GitLab.Api;
using GitExtensions.GitLab.Api.Models;
using GitUI;

namespace GitExtensions.GitLab.Hosting;

internal sealed class GitLabPullRequestDiscussion : IPullRequestDiscussion
{
    private readonly GitLabClient _client;
    private readonly MergeRequest _mergeRequest;

    public GitLabPullRequestDiscussion(GitLabClient client, MergeRequest mergeRequest)
    {
        _client = client;
        _mergeRequest = mergeRequest;
        ForceReload();
    }

    public List<IDiscussionEntry> Entries { get; private set; } = [];

    public void Post(string data)
    {
        ThreadHelper.JoinableTaskFactory.Run(() => _client.AddMergeRequestNoteAsync(_mergeRequest.ProjectId, _mergeRequest.Iid, data));
    }

    public void ForceReload()
    {
        (IReadOnlyList<Commit> commits, IReadOnlyList<Note> notes) = ThreadHelper.JoinableTaskFactory.Run(async () =>
        {
            Task<IReadOnlyList<Commit>> commitsTask = _client.GetMergeRequestCommitsAsync(_mergeRequest.ProjectId, _mergeRequest.Iid);
            Task<IReadOnlyList<Note>> notesTask = _client.GetMergeRequestNotesAsync(_mergeRequest.ProjectId, _mergeRequest.Iid);
            await Task.WhenAll(commitsTask, notesTask);
            return (await commitsTask, await notesTask);
        });

        List<IDiscussionEntry> entries =
        [
            new DiscussionComment(_mergeRequest.Author.Username, _mergeRequest.CreatedAt.LocalDateTime, _mergeRequest.Description),
            .. commits.Select(commit => new DiscussionCommit(commit.Id, commit.AuthorName, (commit.AuthoredDate ?? commit.CreatedAt).LocalDateTime, commit.Message ?? commit.Title)),
            .. notes.Where(note => !note.System).Select(note => new DiscussionComment(note.Author.Username, note.CreatedAt.LocalDateTime, note.Body))
        ];

        Entries = [.. entries.OrderBy(entry => entry.Created)];
    }

    /// <remarks>
    ///  Git Extensions renders the entries as HTML without encoding them.
    /// </remarks>
    private class DiscussionComment(string? author, DateTime created, string? body) : IDiscussionEntry
    {
        public string? Author { get; } = WebUtility.HtmlEncode(author);
        public DateTime Created { get; } = created;
        public string? Body { get; } = WebUtility.HtmlEncode(body)?.ReplaceLineEndings("<br/>");
    }

    private sealed class DiscussionCommit(string sha, string? author, DateTime created, string? body)
        : DiscussionComment(author, created, body), ICommitDiscussionEntry
    {
        public string? Sha { get; } = sha;
    }
}
