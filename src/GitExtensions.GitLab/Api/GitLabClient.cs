using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GitExtensions.GitLab.Api.Models;

namespace GitExtensions.GitLab.Api;

/// <summary>
///  Client of the GitLab REST API v4 (https://docs.gitlab.com/api/rest/).
///  An instance is bound to one GitLab instance and one access token.
/// </summary>
public sealed class GitLabClient : IDisposable
{
    private const int PageSize = 100;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private Task<User>? _currentUser;

    public GitLabClient(Uri instanceUrl, string accessToken, HttpMessageHandler? handler = null)
    {
        InstanceUrl = instanceUrl;
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler);
        _httpClient.BaseAddress = new Uri(instanceUrl.AbsoluteUri.TrimEnd('/') + "/api/v4/");
        _httpClient.Timeout = TimeSpan.FromSeconds(60);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("GitExtensions.GitLab/2.0");
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        // Personal, project, group and OAuth access tokens are all accepted as bearer tokens.
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.Trim());
    }

    public Uri InstanceUrl { get; }

    public void Dispose() => _httpClient.Dispose();

    #region Users and projects

    /// <summary>
    ///  Returns the owner of the access token. The result is cached for the lifetime of the client.
    /// </summary>
    public Task<User> GetCurrentUserAsync(CancellationToken cancellationToken = default)
    {
        Task<User>? currentUser = _currentUser;
        if (currentUser is null || currentUser.IsFaulted || currentUser.IsCanceled)
        {
            currentUser = GetAsync<User>("user", CancellationToken.None);
            _currentUser = currentUser;
        }

        return currentUser.WaitAsync(cancellationToken);
    }

    public Task<Project> GetProjectAsync(string pathWithNamespace, CancellationToken cancellationToken = default)
        => GetAsync<Project>($"projects/{Uri.EscapeDataString(pathWithNamespace)}", cancellationToken);

    public Task<Project> GetProjectAsync(long projectId, CancellationToken cancellationToken = default)
        => GetAsync<Project>($"projects/{projectId}", cancellationToken);

    public Task<IReadOnlyList<Project>> SearchProjectsAsync(string search, CancellationToken cancellationToken = default)
        => GetPagedAsync<Project>(Query("projects", ("search", search), ("order_by", "last_activity_at")), maxPages: 1, cancellationToken);

    public Task<IReadOnlyList<Project>> GetMyProjectsAsync(CancellationToken cancellationToken = default)
        => GetPagedAsync<Project>(Query("projects", ("membership", "true"), ("order_by", "last_activity_at")), maxPages: 5, cancellationToken);

    /// <summary>
    ///  Returns the projects of a user, or of a group (including its subgroups).
    /// </summary>
    public async Task<IReadOnlyList<Project>> GetNamespaceProjectsAsync(string namespacePath, CancellationToken cancellationToken = default)
    {
        string escapedPath = Uri.EscapeDataString(namespacePath);
        try
        {
            return await GetPagedAsync<Project>($"users/{escapedPath}/projects", maxPages: 5, cancellationToken).ConfigureAwait(false);
        }
        catch (GitLabApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return await GetPagedAsync<Project>(Query($"groups/{escapedPath}/projects", ("include_subgroups", "true")), maxPages: 5, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task<Project> ForkProjectAsync(long projectId, CancellationToken cancellationToken = default)
        => SendAsync<Project>(HttpMethod.Post, $"projects/{projectId}/fork", new { }, cancellationToken);

    public Task<IReadOnlyList<Branch>> GetBranchesAsync(long projectId, CancellationToken cancellationToken = default)
        => GetPagedAsync<Branch>($"projects/{projectId}/repository/branches", maxPages: 50, cancellationToken);

    /// <summary>
    ///  Returns the members of a project, including the members inherited from its groups.
    /// </summary>
    public Task<IReadOnlyList<User>> GetProjectMembersAsync(long projectId, CancellationToken cancellationToken = default)
        => GetPagedAsync<User>($"projects/{projectId}/members/all", maxPages: 10, cancellationToken);

    /// <summary>
    ///  Compares two refs using their merge base, as a merge request does.
    /// </summary>
    /// <param name="projectId">Project containing <paramref name="to"/>.</param>
    /// <param name="fromProjectId">Project containing <paramref name="from"/>, when it is another project (fork).</param>
    public Task<CompareResult> CompareAsync(long projectId, string from, string to, long? fromProjectId = null, CancellationToken cancellationToken = default)
        => GetAsync<CompareResult>(
            Query($"projects/{projectId}/repository/compare",
                ("from", from),
                ("to", to),
                ("from_project_id", fromProjectId?.ToString(CultureInfo.InvariantCulture))),
            cancellationToken);

    #endregion

    #region Merge requests

    public async Task<IReadOnlyList<MergeRequest>> GetMergeRequestsAsync(long projectId, MergeRequestState state, MergeRequestScope scope, CancellationToken cancellationToken = default)
    {
        string? reviewerUsername = scope == MergeRequestScope.ReviewRequestedFromMe
            ? (await GetCurrentUserAsync(cancellationToken).ConfigureAwait(false)).Username
            : null;

        string url = Query($"projects/{projectId}/merge_requests",
            ("state", state switch
            {
                MergeRequestState.Opened => "opened",
                MergeRequestState.Merged => "merged",
                MergeRequestState.Closed => "closed",
                _ => "all"
            }),
            ("scope", scope switch
            {
                MergeRequestScope.CreatedByMe => "created_by_me",
                MergeRequestScope.AssignedToMe => "assigned_to_me",
                _ => "all"
            }),
            ("reviewer_username", reviewerUsername),
            ("order_by", "updated_at"));

        // Merged and closed merge requests accumulate forever: only the most recent ones are listed.
        int maxPages = state == MergeRequestState.Opened ? 10 : 2;
        return await GetPagedAsync<MergeRequest>(url, maxPages, cancellationToken).ConfigureAwait(false);
    }

    public Task<MergeRequest> GetMergeRequestAsync(long projectId, long iid, CancellationToken cancellationToken = default)
        => GetAsync<MergeRequest>($"projects/{projectId}/merge_requests/{iid}", cancellationToken);

    public Task<MergeRequest> CreateMergeRequestAsync(long sourceProjectId, CreateMergeRequestRequest request, CancellationToken cancellationToken = default)
        => SendAsync<MergeRequest>(HttpMethod.Post, $"projects/{sourceProjectId}/merge_requests", request, cancellationToken);

    public async Task<IReadOnlyList<FileDiff>> GetMergeRequestDiffsAsync(long projectId, long iid, CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetPagedAsync<FileDiff>($"projects/{projectId}/merge_requests/{iid}/diffs", maxPages: 30, cancellationToken).ConfigureAwait(false);
        }
        catch (GitLabApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // GitLab older than 15.7 only has the (since deprecated) /changes endpoint.
            MergeRequestChanges changes = await GetAsync<MergeRequestChanges>($"projects/{projectId}/merge_requests/{iid}/changes", cancellationToken).ConfigureAwait(false);
            return changes.Changes;
        }
    }

    public Task<IReadOnlyList<Commit>> GetMergeRequestCommitsAsync(long projectId, long iid, CancellationToken cancellationToken = default)
        => GetPagedAsync<Commit>($"projects/{projectId}/merge_requests/{iid}/commits", maxPages: 10, cancellationToken);

    public Task<IReadOnlyList<Note>> GetMergeRequestNotesAsync(long projectId, long iid, CancellationToken cancellationToken = default)
        => GetPagedAsync<Note>(Query($"projects/{projectId}/merge_requests/{iid}/notes", ("sort", "asc"), ("order_by", "created_at")), maxPages: 20, cancellationToken);

    public Task<Note> AddMergeRequestNoteAsync(long projectId, long iid, string body, CancellationToken cancellationToken = default)
        => SendAsync<Note>(HttpMethod.Post, $"projects/{projectId}/merge_requests/{iid}/notes", new { body }, cancellationToken);

    public Task<Approvals> GetMergeRequestApprovalsAsync(long projectId, long iid, CancellationToken cancellationToken = default)
        => GetAsync<Approvals>($"projects/{projectId}/merge_requests/{iid}/approvals", cancellationToken);

    public Task ApproveMergeRequestAsync(long projectId, long iid, string? sha, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"projects/{projectId}/merge_requests/{iid}/approve", new { sha }, cancellationToken);

    public Task UnapproveMergeRequestAsync(long projectId, long iid, CancellationToken cancellationToken = default)
        => SendAsync(HttpMethod.Post, $"projects/{projectId}/merge_requests/{iid}/unapprove", new { }, cancellationToken);

    public Task<MergeRequest> AcceptMergeRequestAsync(long projectId, long iid, AcceptMergeRequestRequest request, CancellationToken cancellationToken = default)
        => SendAsync<MergeRequest>(HttpMethod.Put, $"projects/{projectId}/merge_requests/{iid}/merge", request, cancellationToken);

    public Task<MergeRequest> CloseMergeRequestAsync(long projectId, long iid, CancellationToken cancellationToken = default)
        => SendAsync<MergeRequest>(HttpMethod.Put, $"projects/{projectId}/merge_requests/{iid}", new { state_event = "close" }, cancellationToken);

    public Task<MergeRequest> ReopenMergeRequestAsync(long projectId, long iid, CancellationToken cancellationToken = default)
        => SendAsync<MergeRequest>(HttpMethod.Put, $"projects/{projectId}/merge_requests/{iid}", new { state_event = "reopen" }, cancellationToken);

    private sealed class MergeRequestChanges
    {
        public IReadOnlyList<FileDiff> Changes { get; init; } = [];
    }

    #endregion

    #region HTTP plumbing

    internal static string Query(string path, params (string Name, string? Value)[] parameters)
    {
        StringBuilder builder = new(path);
        char separator = path.Contains('?') ? '&' : '?';
        foreach ((string name, string? value) in parameters)
        {
            if (value is null)
            {
                continue;
            }

            builder.Append(separator).Append(name).Append('=').Append(Uri.EscapeDataString(value));
            separator = '&';
        }

        return builder.ToString();
    }

    private Task<T> GetAsync<T>(string relativeUrl, CancellationToken cancellationToken)
        => SendAsync<T>(HttpMethod.Get, relativeUrl, body: null, cancellationToken);

    private async Task<IReadOnlyList<T>> GetPagedAsync<T>(string relativeUrl, int maxPages, CancellationToken cancellationToken)
    {
        List<T> result = [];
        for (int page = 1; page <= maxPages; page++)
        {
            string url = Query(relativeUrl, ("per_page", PageSize.ToString(CultureInfo.InvariantCulture)), ("page", page.ToString(CultureInfo.InvariantCulture)));
            using HttpResponseMessage response = await SendCoreAsync(HttpMethod.Get, url, body: null, cancellationToken).ConfigureAwait(false);
            List<T> items = await ReadAsync<List<T>>(response, cancellationToken).ConfigureAwait(false);
            result.AddRange(items);

            // GitLab omits X-Next-Page on the last page (and for very large offset-paginated collections).
            if (items.Count == 0
                || !response.Headers.TryGetValues("X-Next-Page", out IEnumerable<string>? nextPage)
                || string.IsNullOrWhiteSpace(nextPage.FirstOrDefault()))
            {
                break;
            }
        }

        return result;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendCoreAsync(method, relativeUrl, body, cancellationToken).ConfigureAwait(false);
        return await ReadAsync<T>(response, cancellationToken).ConfigureAwait(false);
    }

    private async Task SendAsync(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await SendCoreAsync(method, relativeUrl, body, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendCoreAsync(HttpMethod method, string relativeUrl, object? body, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, relativeUrl);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptions);
        }

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitLabApiException(statusCode: null, $"GitLab did not answer within {_httpClient.Timeout.TotalSeconds:0} seconds ({InstanceUrl}).", ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            string content = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw GitLabApiException.FromResponse(response.StatusCode, content);
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        T? value = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken).ConfigureAwait(false);
        return value ?? throw new GitLabApiException(response.StatusCode, $"Unexpected empty response from GitLab ({response.RequestMessage?.RequestUri}).");
    }

    #endregion
}
