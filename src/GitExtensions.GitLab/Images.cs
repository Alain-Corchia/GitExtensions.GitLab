namespace GitExtensions.GitLab;

internal static class Images
{
    private static readonly Lazy<Icon> _gitLabIcon = new(() =>
    {
        using Stream stream = typeof(Images).Assembly.GetManifestResourceStream("GitExtensions.GitLab.IconGitlab.ico")
            ?? throw new InvalidOperationException("Missing embedded resource IconGitlab.ico");
        return new Icon(stream);
    });

    private static readonly Lazy<Bitmap> _gitLab = new(() => new Icon(GitLabIcon, 32, 32).ToBitmap());

    public static Icon GitLabIcon => _gitLabIcon.Value;

    public static Bitmap GitLab => _gitLab.Value;
}
