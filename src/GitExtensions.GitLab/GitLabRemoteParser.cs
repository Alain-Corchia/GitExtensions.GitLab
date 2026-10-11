using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace GitExtensions.GitLab;

/// <summary>
///  Extracts the GitLab project path ("group/subgroup/project") from a git remote URL.
/// </summary>
internal static partial class GitLabRemoteParser
{
    // scp-like syntax: [user@]host:path
    [GeneratedRegex(@"^(?:[^@/]+@)?(?<host>[^:/\\]{2,}):(?<path>[^\\]+)$", RegexOptions.ExplicitCapture)]
    private static partial Regex ScpLikeUrlRegex { get; }

    /// <summary>
    ///  Parses <paramref name="remoteUrl"/> and returns the path of the project if the remote is hosted
    ///  by the GitLab instance at <paramref name="instanceUrl"/>.
    /// </summary>
    /// <remarks>
    ///  Supported forms: https://[user@]host[:port][/relative_root]/group/project[.git],
    ///  ssh://[user@]host[:port]/group/project[.git], git://host/group/project[.git] and [user@]host:group/project[.git].
    /// </remarks>
    public static bool TryParse(string remoteUrl, Uri instanceUrl, [NotNullWhen(returnValue: true)] out string? projectPath)
    {
        projectPath = null;
        remoteUrl = remoteUrl.Trim();

        string host;
        string path;
        bool isHttp = false;
        if (remoteUrl.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(remoteUrl, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            host = uri.Host;
            path = Uri.UnescapeDataString(uri.AbsolutePath);
            isHttp = uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp;
        }
        else
        {
            Match match = ScpLikeUrlRegex.Match(remoteUrl);
            if (!match.Success)
            {
                return false;
            }

            host = match.Groups["host"].Value;
            path = match.Groups["path"].Value;
        }

        if (!string.Equals(host, instanceUrl.Host, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        path = path.Trim('/');

        // A GitLab installed under a relative URL root (https://host/gitlab) has it in its HTTP clone URLs only.
        string relativeRoot = instanceUrl.AbsolutePath.Trim('/');
        if (isHttp && relativeRoot.Length > 0)
        {
            if (!path.StartsWith(relativeRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = path[(relativeRoot.Length + 1)..];
        }

        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            path = path[..^4];
        }

        path = path.TrimEnd('/');
        if (path.StartsWith('~'))
        {
            return false;
        }

        string[] segments = path.Split('/');
        if (segments.Length < 2 || segments.Any(string.IsNullOrWhiteSpace))
        {
            return false;
        }

        projectPath = path;
        return true;
    }

    /// <summary>
    ///  Normalizes the "GitLab URL" setting: the scheme is optional and defaults to https.
    /// </summary>
    public static bool TryParseInstanceUrl(string? value, [NotNullWhen(returnValue: true)] out Uri? instanceUrl)
    {
        instanceUrl = null;
        value = value?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "https://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            return false;
        }

        instanceUrl = new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
        return true;
    }
}
