# GitLab plugin for Git Extensions

Create and manage GitLab merge requests from [Git Extensions](https://github.com/gitextensions/gitextensions).

Works with gitlab.com and self-managed GitLab instances (REST API v4), with projects in nested groups and
with forks. Requires **Git Extensions 7.x** (.NET 10).

## Features

- **Merge requests dialog** (*Plugins > GitLab*):
  - lists the merge requests of a project, filtered by state (open, merged, closed, all) and scope
    (all, created by me, assigned to me, review requested from me);
  - shows the description, discussion, pipeline and approval status, changed files with their diff,
    and commits;
  - comment, approve / revoke approval, merge (merge commit or squash, delete source branch),
    close / reopen, open in the browser;
  - checks out the source branch, including from a fork (fetched from `refs/merge-requests/<iid>/head`).
- **Create merge request dialog** (*Plugins > Create GitLab merge request*, or *New merge request...*
  in the dialog above): source and target project and branch (forks target their parent project by
  default), title, description (prefilled from `.gitlab/merge_request_templates/Default.md`), draft,
  assignee, reviewer, labels, squash and delete-source-branch options, preview of the commits and changes.
- **Git Extensions repository host integration**: the *Repository hosts* menu (view pull requests,
  create pull request, fork / clone, add upstream remote) and the *View in GitLab* blame link work
  for GitLab repositories.

## Installation

- With the [Plugin Manager](https://github.com/gitextensions/gitextensions.pluginmanager) (*Plugins > Plugin Manager*):
  install the [`GitExtensions.GitLab.MergeRequests`](https://www.nuget.org/packages/GitExtensions.GitLab.MergeRequests) package from nuget.org.
- Manually: copy `GitExtensions.GitLab.MergeRequests.dll` into
  `%LOCALAPPDATA%\GitExtensions\UserPlugins\GitExtensions.GitLab.MergeRequests\` (or `UserPlugins\GitExtensions.GitLab.MergeRequests\`
  next to `GitExtensions.exe` for a portable installation), then restart Git Extensions.

## Configuration

*Plugins > Plugin Settings > GitLab*:

| Setting | Description |
|---|---|
| GitLab URL | Address of the GitLab instance, e.g. `https://gitlab.com` (default) or `https://gitlab.example.com/gitlab`. Only the remotes on this host are handled. |
| Personal access token | A [personal access token](https://docs.gitlab.com/user/profile/personal_access_tokens/) with the `api` scope (a project or group access token works too). The link below the field opens the token creation page. |

The settings can be defined globally, or per repository (local or distributed level) to work with
several GitLab instances.

To be offered a merge request after each push, add a script (*Settings > Scripts*) running on event
*After Push* with the command `{plugin:Create GitLab merge request}`.

> The previous versions used the OAuth password grant; GitLab no longer allows it for third-party
> applications. A personal access token is now required.

## Building

Requirements: .NET SDK 10.

```bash
dotnet build GitExtensions.GitLab.sln
dotnet test GitExtensions.GitLab.sln
```

The plugin is compiled against the Git Extensions installed in `C:\Program Files\GitExtensions`. When
it is not installed, the `GitExtensions.Extensibility` package downloads the portable Git Extensions
version set in `Directory.Build.props` (`GitExtensionsReferenceVersion`) into `gitextensions.shared`.
Another location can be given with `-p:GitExtensionsPath=<folder>`.

To deploy the build into the user plugins folder (Git Extensions must be restarted):

```bash
dotnet build src/GitExtensions.GitLab -p:DeployPlugin=true
```

To create the NuGet package for the Plugin Manager:

```bash
dotnet pack src/GitExtensions.GitLab -c Release -o artifacts
```

## Publishing

The GitHub workflow builds, tests and packs every push. Pushing a tag `v<version>` also publishes the
package to nuget.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing):
no API key is stored, the nuget.org policy of the owner `Alain.Corchia` trusts the workflow `build_plugin.yml`
of this repository.
The tag must match `VersionPrefix` in `src/GitExtensions.GitLab/GitExtensions.GitLab.csproj`:

```bash
git tag v2.0.0
git push origin v2.0.0
```
