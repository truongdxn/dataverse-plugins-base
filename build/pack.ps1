<#
.SYNOPSIS
    Builds the solution package(s) from the assemblies' step configuration.

.DESCRIPTION
    A thin wrapper over the dv CLI, kept for pipelines. Day to day, use the shim instead:

        .\dv pack --version 1.0.0.42

    dv builds the plugin assemblies itself, so no separate build step is needed. Packing needs no
    Dataverse connection, which is what lets a pipeline produce the package with no credentials.

    Each solution packs to its own .zip, by default under CRM/Solutions/<Solution>/. Importing
    those is another module's job; this repo stops at building them.

.PARAMETER Solution
    Restrict to one solution. Default: every solution under CRM/Plugins.

.EXAMPLE
    ./build/pack.ps1 -Configuration Release -Version 1.0.0.42
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    # Version stamped into the package. CI passes its build number here.
    [string] $Version,

    [switch] $Managed,

    [string] $Solution,

    # Restrict the package to one plugin assembly. A partial package never removes assemblies
    # already deployed - solution import is additive - but it is partial.
    [string] $Assembly,

    # Overrides packageOutput in solution.json and dv.json. A folder, or a .zip path.
    [string] $Out
)

. "$PSScriptRoot/common.ps1"

$solutions = if ($Solution) { @($Solution) } else { Get-DvSolution }

if (-not $solutions) {
    throw 'No solutions found under CRM/Plugins. Create one with: dotnet new dv-solution -n <Name>'
}

foreach ($name in $solutions) {
    Write-Host ''
    Write-Host "=== $name ===" -ForegroundColor Cyan

    $arguments = @('pack', '--solution', $name, '--configuration', $Configuration)

    if ($Version)  { $arguments += @('--version', $Version) }
    if ($Managed)  { $arguments += '--managed' }
    if ($Assembly) { $arguments += @('--assembly', $Assembly) }
    if ($Out)      { $arguments += @('--out', $Out) }

    Invoke-Dv @arguments
}

Write-Host ''
Write-Host "Packages written for: $($solutions -join ', ')" -ForegroundColor Green
