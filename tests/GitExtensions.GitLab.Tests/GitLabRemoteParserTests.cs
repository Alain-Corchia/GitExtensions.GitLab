namespace GitExtensions.GitLab.Tests;

public class GitLabRemoteParserTests
{
    private static readonly Uri GitLabCom = new("https://gitlab.com/");

    [Theory]
    [InlineData("https://gitlab.com/group/project.git", "group/project")]
    [InlineData("https://gitlab.com/group/project", "group/project")]
    [InlineData("https://gitlab.com/group/project/", "group/project")]
    [InlineData("https://user:secret@gitlab.com/group/sub/project.git", "group/sub/project")]
    [InlineData("http://GITLAB.com/Group/Project.git", "Group/Project")]
    [InlineData("git@gitlab.com:group/project.git", "group/project")]
    [InlineData("git@gitlab.com:group/sub1/sub2/project", "group/sub1/sub2/project")]
    [InlineData("gitlab.com:group/project.git", "group/project")]
    [InlineData("ssh://git@gitlab.com/group/project.git", "group/project")]
    [InlineData("ssh://git@gitlab.com:2222/group/sub/project.git", "group/sub/project")]
    [InlineData("git://gitlab.com/group/project.git", "group/project")]
    [InlineData("  https://gitlab.com/group/my.project.git  ", "group/my.project")]
    public void TryParse_accepts_remotes_of_the_instance(string remoteUrl, string expectedProjectPath)
    {
        Assert.True(GitLabRemoteParser.TryParse(remoteUrl, GitLabCom, out string? projectPath));
        Assert.Equal(expectedProjectPath, projectPath);
    }

    [Theory]
    [InlineData("https://github.com/group/project.git")]
    [InlineData("git@github.com:group/project.git")]
    [InlineData("https://gitlab.example.com/group/project.git")]
    [InlineData("https://gitlab.com/project.git")]
    [InlineData("https://gitlab.com/")]
    [InlineData("C:\\repositories\\project")]
    [InlineData("/srv/git/project.git")]
    [InlineData("../project")]
    [InlineData("")]
    public void TryParse_rejects_other_remotes(string remoteUrl)
    {
        Assert.False(GitLabRemoteParser.TryParse(remoteUrl, GitLabCom, out string? projectPath));
        Assert.Null(projectPath);
    }

    [Theory]
    [InlineData("https://example.com/gitlab/group/project.git", "group/project")]
    [InlineData("git@example.com:group/project.git", "group/project")]
    [InlineData("ssh://git@example.com/group/project.git", "group/project")]
    public void TryParse_handles_a_relative_url_root(string remoteUrl, string expectedProjectPath)
    {
        Uri instanceUrl = new("https://example.com/gitlab/");

        Assert.True(GitLabRemoteParser.TryParse(remoteUrl, instanceUrl, out string? projectPath));
        Assert.Equal(expectedProjectPath, projectPath);
    }

    [Fact]
    public void TryParse_rejects_http_remotes_outside_the_relative_url_root()
    {
        Assert.False(GitLabRemoteParser.TryParse("https://example.com/other/group/project.git", new Uri("https://example.com/gitlab/"), out _));
    }

    [Theory]
    [InlineData("https://gitlab.com", "https://gitlab.com/")]
    [InlineData("gitlab.example.com", "https://gitlab.example.com/")]
    [InlineData(" https://gitlab.example.com:8443/gitlab/ ", "https://gitlab.example.com:8443/gitlab/")]
    [InlineData("http://localhost:8080", "http://localhost:8080/")]
    public void TryParseInstanceUrl_normalizes_the_setting(string value, string expected)
    {
        Assert.True(GitLabRemoteParser.TryParseInstanceUrl(value, out Uri? instanceUrl));
        Assert.Equal(expected, instanceUrl.AbsoluteUri);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ftp://gitlab.com")]
    [InlineData("https://")]
    public void TryParseInstanceUrl_rejects_invalid_values(string? value)
    {
        Assert.False(GitLabRemoteParser.TryParseInstanceUrl(value, out _));
    }
}
