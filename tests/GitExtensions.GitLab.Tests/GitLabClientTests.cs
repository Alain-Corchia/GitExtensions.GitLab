using System.Net;
using System.Text;
using System.Text.Json;
using GitExtensions.GitLab.Api;
using GitExtensions.GitLab.Api.Models;

namespace GitExtensions.GitLab.Tests;

public class GitLabClientTests
{
    private readonly FakeHandler _handler = new();
    private readonly GitLabClient _client;

    public GitLabClientTests()
    {
        _client = new GitLabClient(new Uri("https://gitlab.example.com/gitlab/"), " token ", _handler);
    }

    [Fact]
    public async Task Requests_target_api_v4_with_a_bearer_token_and_an_encoded_project_path()
    {
        _handler.Respond("""{"id": 42, "path_with_namespace": "group/sub/project", "default_branch": "main", "namespace": {"full_path": "group/sub", "kind": "group"}}""");

        Project project = await _client.GetProjectAsync("group/sub/project", TestContext.Current.CancellationToken);

        HttpRequestMessage request = Assert.Single(_handler.Requests);
        Assert.Equal("https://gitlab.example.com/gitlab/api/v4/projects/group%2Fsub%2Fproject", request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer token", request.Headers.Authorization!.ToString());
        Assert.Equal(42, project.Id);
        Assert.Equal("main", project.DefaultBranch);
        Assert.Equal("group/sub", project.Namespace!.FullPath);
    }

    [Fact]
    public async Task Paged_requests_follow_X_Next_Page()
    {
        _handler.Respond("""[{"name": "a", "commit": {"id": "1"}}, {"name": "b", "commit": {"id": "2"}}]""", nextPage: "2");
        _handler.Respond("""[{"name": "c", "commit": {"id": "3"}}]""", nextPage: "");

        IReadOnlyList<Branch> branches = await _client.GetBranchesAsync(7, TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b", "c"], branches.Select(branch => branch.Name));
        Assert.Equal(2, _handler.Requests.Count);
        Assert.EndsWith("projects/7/repository/branches?per_page=100&page=1", _handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.EndsWith("projects/7/repository/branches?per_page=100&page=2", _handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Merge_requests_are_deserialized_from_snake_case()
    {
        _handler.Respond("""
            {
              "id": 1001, "iid": 12, "project_id": 7, "title": "Add feature", "state": "opened",
              "created_at": "2025-01-02T03:04:05.123Z", "updated_at": "2025-01-03T00:00:00Z",
              "source_branch": "feature", "target_branch": "main", "source_project_id": 8, "target_project_id": 7,
              "author": {"id": 3, "username": "jdoe", "name": "John Doe"},
              "reviewers": [{"id": 4, "username": "rev"}], "labels": ["backend"],
              "draft": true, "has_conflicts": false, "detailed_merge_status": "draft_status",
              "sha": "0123456789abcdef0123456789abcdef01234567", "squash": true, "force_remove_source_branch": true,
              "diff_refs": {"base_sha": "aaaa", "head_sha": "bbbb", "start_sha": "cccc"},
              "head_pipeline": {"id": 5, "status": "success"},
              "user": {"can_merge": true},
              "web_url": "https://gitlab.example.com/gitlab/group/project/-/merge_requests/12"
            }
            """);

        MergeRequest mergeRequest = await _client.GetMergeRequestAsync(7, 12, TestContext.Current.CancellationToken);

        Assert.Equal(12, mergeRequest.Iid);
        Assert.Equal(8, mergeRequest.SourceProjectId);
        Assert.Equal("jdoe", mergeRequest.Author.Username);
        Assert.Equal("John Doe (@jdoe)", mergeRequest.Author.DisplayName);
        Assert.Equal("rev", Assert.Single(mergeRequest.Reviewers).Username);
        Assert.Equal(["backend"], mergeRequest.Labels);
        Assert.True(mergeRequest.Draft);
        Assert.True(mergeRequest.IsOpened);
        Assert.Equal("aaaa", mergeRequest.DiffRefs!.BaseSha);
        Assert.Equal("success", mergeRequest.HeadPipeline!.Status);
        Assert.True(mergeRequest.Permissions!.CanMerge);
        Assert.Equal(new DateTimeOffset(2025, 1, 2, 3, 4, 5, 123, TimeSpan.Zero), mergeRequest.CreatedAt);
    }

    [Fact]
    public async Task Merge_request_list_uses_state_scope_and_reviewer_filters()
    {
        _handler.Respond("""{"id": 3, "username": "me"}""");
        _handler.Respond("[]");

        await _client.GetMergeRequestsAsync(7, MergeRequestState.Merged, MergeRequestScope.ReviewRequestedFromMe, TestContext.Current.CancellationToken);

        Assert.EndsWith("/api/v4/user", _handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.EndsWith("projects/7/merge_requests?state=merged&scope=all&reviewer_username=me&order_by=updated_at&per_page=100&page=1", _handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Create_merge_request_posts_a_snake_case_body_to_the_source_project()
    {
        _handler.Respond("""{"iid": 5, "web_url": "https://gitlab.example.com/gitlab/g/p/-/merge_requests/5"}""", statusCode: HttpStatusCode.Created);

        MergeRequest created = await _client.CreateMergeRequestAsync(8, new CreateMergeRequestRequest
        {
            SourceBranch = "feature",
            TargetBranch = "main",
            Title = "Draft: Add feature",
            TargetProjectId = 7,
            AssigneeIds = [3],
            RemoveSourceBranch = true
        }, TestContext.Current.CancellationToken);

        HttpRequestMessage request = Assert.Single(_handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.EndsWith("projects/8/merge_requests", request.RequestUri!.AbsoluteUri);
        using JsonDocument body = JsonDocument.Parse(_handler.Bodies[0]!);
        Assert.Equal("feature", body.RootElement.GetProperty("source_branch").GetString());
        Assert.Equal(7, body.RootElement.GetProperty("target_project_id").GetInt64());
        Assert.Equal(3, body.RootElement.GetProperty("assignee_ids")[0].GetInt64());
        Assert.True(body.RootElement.GetProperty("remove_source_branch").GetBoolean());
        Assert.False(body.RootElement.TryGetProperty("reviewer_ids", out _));
        Assert.False(body.RootElement.TryGetProperty("labels", out _));
        Assert.Equal(5, created.Iid);
    }

    [Fact]
    public async Task Compare_passes_the_project_of_the_from_ref()
    {
        _handler.Respond("""{"commits": [{"id": "abc", "title": "Commit"}], "diffs": [{"old_path": "a", "new_path": "a", "diff": "@@"}], "compare_timeout": false}""");

        CompareResult result = await _client.CompareAsync(8, from: "main", to: "feature/x", fromProjectId: 7, TestContext.Current.CancellationToken);

        Assert.EndsWith("projects/8/repository/compare?from=main&to=feature%2Fx&from_project_id=7", _handler.Requests[0].RequestUri!.AbsoluteUri);
        Assert.Equal("Commit", Assert.Single(result.Commits).Title);
        Assert.Equal("@@", Assert.Single(result.Diffs).Diff);
    }

    [Fact]
    public async Task Diffs_fall_back_to_the_changes_endpoint_on_old_GitLab_versions()
    {
        _handler.Respond("""{"message": "404 Not found"}""", statusCode: HttpStatusCode.NotFound);
        _handler.Respond("""{"changes": [{"old_path": "a", "new_path": "b", "renamed_file": true, "diff": ""}]}""");

        IReadOnlyList<FileDiff> diffs = await _client.GetMergeRequestDiffsAsync(7, 12, TestContext.Current.CancellationToken);

        Assert.True(Assert.Single(diffs).RenamedFile);
        Assert.EndsWith("projects/7/merge_requests/12/changes", _handler.Requests[1].RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Errors_carry_the_status_code_and_the_GitLab_message()
    {
        _handler.Respond("""{"message": ["Another open merge request already exists for this source branch: !3"]}""", statusCode: HttpStatusCode.Conflict);

        GitLabApiException exception = await Assert.ThrowsAsync<GitLabApiException>(
            () => _client.CloseMergeRequestAsync(7, 12, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("Another open merge request already exists", exception.Message);
    }

    [Fact]
    public async Task Unauthorized_errors_point_to_the_token_setting()
    {
        _handler.Respond("""{"message": "401 Unauthorized"}""", statusCode: HttpStatusCode.Unauthorized);

        GitLabApiException exception = await Assert.ThrowsAsync<GitLabApiException>(
            () => _client.GetCurrentUserAsync(TestContext.Current.CancellationToken));

        Assert.Contains("personal access token", exception.Message);
    }

    [Theory]
    [InlineData("""{"message": {"title": ["can't be blank"], "target_branch": ["is invalid"]}}""", "title can't be blank; target_branch is invalid")]
    [InlineData("""{"error": "insufficient_scope", "error_description": "The request requires higher privileges."}""", "The request requires higher privileges.")]
    [InlineData("""{"error": "invalid_token"}""", "invalid_token")]
    [InlineData("<html>Bad gateway</html>", "<html>Bad gateway</html>")]
    [InlineData("", "no details")]
    public void ExtractMessage_reads_the_GitLab_error_formats(string content, string expected)
    {
        Assert.Equal(expected, GitLabApiException.ExtractMessage(content));
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string?> Bodies { get; } = [];

        public void Respond(string json, HttpStatusCode statusCode = HttpStatusCode.OK, string? nextPage = null)
        {
            HttpResponseMessage response = new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
            if (nextPage is not null)
            {
                response.Headers.Add("X-Next-Page", nextPage);
            }

            _responses.Enqueue(response);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken));
            HttpResponseMessage response = _responses.Dequeue();
            response.RequestMessage = request;
            return response;
        }
    }
}
