<#
.SYNOPSIS
    Adds the internal GitLab package registry hosting GitExtensions.GitLab.Raincode to the package
    sources of the Git Extensions Plugin Manager.

.DESCRIPTION
    Edits <UserPlugins>\NuGet.Config, the configuration read by the Plugin Manager.
    The GitLab user name and token are stored in clear text in that file, as NuGet does.
    Use a personal access token with the read_api scope, or a deploy token with the
    read_package_registry scope (its user name is then the deploy token user name).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File Add-PluginSource.ps1
#>
param(
    [string] $UserPluginsPath = (Join-Path $env:LOCALAPPDATA 'GitExtensions\UserPlugins'),
    [string] $SourceName = 'raincode-gitlab',
    [string] $SourceUrl = 'http://owngit.corp.phidani.be/api/v4/projects/558/packages/nuget/index.json',
    [string] $UserName,
    [SecureString] $Token
)

$ErrorActionPreference = 'Stop'

if (-not $UserName) {
    $UserName = Read-Host 'GitLab user name'
}

if (-not $Token) {
    $Token = Read-Host 'GitLab token (scope read_api)' -AsSecureString
}

$plainToken = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($Token))

$configPath = Join-Path $UserPluginsPath 'NuGet.Config'
New-Item -ItemType Directory -Force -Path $UserPluginsPath | Out-Null
if (Test-Path $configPath) {
    [xml] $config = Get-Content -Raw -Path $configPath
}
else {
    [xml] $config = '<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" /></packageSources></configuration>'
}

$root = $config.SelectSingleNode('/configuration')

function Get-Section([string] $name) {
    $section = $root.SelectSingleNode($name)
    if (-not $section) {
        $section = $config.CreateElement($name)
        [void] $root.AppendChild($section)
    }

    return $section
}

function Add-Entry($parent, [string] $key, [string] $value) {
    $entry = $config.CreateElement('add')
    $entry.SetAttribute('key', $key)
    $entry.SetAttribute('value', $value)
    [void] $parent.AppendChild($entry)
    return $entry
}

# The internal GitLab is served over HTTP: recent NuGet clients require allowInsecureConnections.
$sources = Get-Section 'packageSources'
$existingSource = $sources.SelectSingleNode("add[@key='$SourceName']")
if ($existingSource) {
    [void] $sources.RemoveChild($existingSource)
}

$source = Add-Entry $sources $SourceName $SourceUrl
$source.SetAttribute('allowInsecureConnections', 'true')

$credentials = Get-Section 'packageSourceCredentials'
$existingCredentials = $credentials.SelectSingleNode($SourceName)
if ($existingCredentials) {
    [void] $credentials.RemoveChild($existingCredentials)
}

$sourceCredentials = $config.CreateElement($SourceName)
[void] (Add-Entry $sourceCredentials 'Username' $UserName)
[void] (Add-Entry $sourceCredentials 'ClearTextPassword' $plainToken)
[void] $credentials.AppendChild($sourceCredentials)

$config.Save($configPath)
Write-Host "Package source '$SourceName' added to $configPath."
Write-Host 'Open Git Extensions > Plugins > Plugin Manager and install GitExtensions.GitLab.Raincode.'
