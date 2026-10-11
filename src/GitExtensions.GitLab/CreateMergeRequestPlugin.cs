using System.ComponentModel.Composition;
using GitCommands;
using GitExtensions.Extensibility.Git;
using GitExtensions.Extensibility.Plugins;

namespace GitExtensions.GitLab;

/// <summary>
///  Plugins menu entry opening the merge request creation dialog directly.
///  Can also run after each push with a script whose command is "{plugin:Create GitLab merge request}".
/// </summary>
[Export(typeof(IGitPlugin))]
public sealed class CreateMergeRequestPlugin : GitPluginBase
{
    public CreateMergeRequestPlugin()
        : base(hasSettings: false)
    {
        Id = new Guid("5A0B4D52-7C1E-4F3B-9E65-2D8F0C9A1B37");
        Name = "Create GitLab merge request";
        Translate(AppSettings.CurrentTranslation);

        Icon = Images.GitLab;
    }

    public override bool Execute(GitUIEventArgs args)
    {
        GitLabPlugin.Instance?.ShowCreateMergeRequest(args.GitUICommands, args.OwnerForm);
        return false;
    }
}
