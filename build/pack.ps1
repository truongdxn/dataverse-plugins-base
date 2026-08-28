<#
.SYNOPSIS
    Builds the solution package from the assemblies' step configuration.

.DESCRIPTION
    A thin wrapper over the dv CLI, kept for pipelines. Day to day, use the shim instead:

        .\dv pack --version 1.0.0.42

    dv builds the plugin assemblies itself, so no separate build step is needed. It needs no
    Dataverse connection, which is what lets the deploy pipeline produce the package. Nothing it
    writes is committed - artifacts/ is git-ignored and regenerated every run.

.EXAMPLE
    ./build/pack.ps1 -Configuration Release -Version 1.0.0.42 -Managed
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    # Version stamped into the package. CI passes its build number here.
    [string] $Version,

    [switch] $Managed,

    # Restrict the package to one plugin assembly. Solution import is additive, so a partial
    # package never removes assemblies already deployed - but it is partial.
    [string] $Assembly
)

. "$PSScriptRoot/common.ps1"

$arguments = @('pack', '--configuration', $Configuration)

if ($Version)  { $arguments += @('--version', $Version) }
if ($Managed)  { $arguments += '--managed' }
if ($Assembly) { $arguments += @('--assembly', $Assembly) }

Invoke-Dv @arguments

Write-Host ''
Write-Host 'Package written to artifacts/.' -ForegroundColor Green
