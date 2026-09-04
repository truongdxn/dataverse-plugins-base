<#
.SYNOPSIS
    Builds the four base packages into artifacts/packages.

.DESCRIPTION
    The base ships as installable artifacts so a consumer repo carries none of its source:

      Dataverse.Plugins.Abstractions   source-only: [PluginStep], PluginBase, linked in by props
      Dataverse.Plugins.Testing        assembly: the fake pipeline harness
      Dataverse.Plugins.Tooling        dotnet tool: the dv CLI
      Dataverse.Plugins.Templates      dotnet new templates

    artifacts/packages is registered as a NuGet source in NuGet.config, so packing is all it takes
    to make them restorable locally. Publishing to a real feed is one 'dotnet nuget push' away.

.PARAMETER Version
    Package version. CI passes its build number; defaults to the VersionPrefix in
    Directory.Build.props.

.EXAMPLE
    ./build/pack-packages.ps1 -Version 1.0.0-local.3
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $Version,

    # Wipe artifacts/packages first. Without it, older versions linger and a consumer that asked
    # for "*" can silently resolve one of them instead of what you just built.
    [switch] $Clean
)

. "$PSScriptRoot/common.ps1"

$root = Get-RepoRoot
$output = Join-Path $root 'artifacts\packages'

$projects = @(
    'CRM\Shared\PluginBase\src\Dataverse.Plugins.Abstractions\Dataverse.Plugins.Abstractions.csproj'
    'CRM\Shared\PluginBase\src\Dataverse.Plugins.Testing\Dataverse.Plugins.Testing.csproj'
    'CRM\Shared\PluginBase\src\Dataverse.Plugins.Tooling\Dataverse.Plugins.Tooling.csproj'
    'templates\Dataverse.Plugins.Templates.csproj'
)

if ($Clean -and (Test-Path $output)) {
    Remove-Item $output -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $output | Out-Null

foreach ($project in $projects) {
    $full = Join-Path $root $project
    Write-Host ''
    Write-Host "=== $(Split-Path -Leaf $project) ===" -ForegroundColor Cyan

    $arguments = @('pack', $full, '--configuration', $Configuration, '--output', $output, '--nologo')
    if ($Version) { $arguments += "-p:Version=$Version" }

    Invoke-Native -What "Packing $project" -Command {
        & (Resolve-DotNet) @arguments
    }
}

Write-Host ''
Write-Host 'Packages written to artifacts/packages:' -ForegroundColor Green
Get-ChildItem $output -Filter *.nupkg | ForEach-Object { Write-Host "  $($_.Name)" }
