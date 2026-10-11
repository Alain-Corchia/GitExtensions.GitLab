using System.Net;
using System.Text.Json;

namespace GitExtensions.GitLab.Api;

public sealed class GitLabApiException : Exception
{
    public GitLabApiException(HttpStatusCode? statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>
    ///  <see langword="null"/> when no HTTP response was received (timeout).
    /// </summary>
    public HttpStatusCode? StatusCode { get; }

    internal static GitLabApiException FromResponse(HttpStatusCode statusCode, string content)
    {
        string detail = ExtractMessage(content);
        string message = statusCode switch
        {
            HttpStatusCode.Unauthorized => "GitLab rejected the access token (401 Unauthorized). Check the personal access token in the GitLab plugin settings.",
            HttpStatusCode.Forbidden when detail.Contains("insufficient_scope", StringComparison.OrdinalIgnoreCase)
                => "The GitLab access token does not have the required 'api' scope (403 Forbidden).",
            HttpStatusCode.Forbidden => $"Access denied by GitLab (403 Forbidden): {detail}",
            HttpStatusCode.NotFound => $"Not found on GitLab (404): {detail}",
            _ => $"GitLab error {(int)statusCode} ({statusCode}): {detail}"
        };

        return new GitLabApiException(statusCode, message);
    }

    /// <summary>
    ///  Extracts a readable message from a GitLab error body: {"message": ...} or {"error": ..., "error_description": ...}.
    /// </summary>
    internal static string ExtractMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "no details";
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(content);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("message", out JsonElement message))
                {
                    return Flatten(message);
                }

                if (root.TryGetProperty("error_description", out JsonElement description))
                {
                    return Flatten(description);
                }

                if (root.TryGetProperty("error", out JsonElement error))
                {
                    return Flatten(error);
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON (e.g. an HTML page from a proxy): use the raw content.
        }

        const int maxLength = 500;
        return content.Length > maxLength ? content[..maxLength] : content;

        static string Flatten(JsonElement element) => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Array => string.Join("; ", element.EnumerateArray().Select(Flatten)),
            JsonValueKind.Object => string.Join("; ", element.EnumerateObject().Select(p => $"{p.Name} {Flatten(p.Value)}")),
            _ => element.ToString()
        };
    }
}
