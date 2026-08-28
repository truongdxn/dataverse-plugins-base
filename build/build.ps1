<#
.SYNOPSIS
    Builds everything and validates every step declaration, across every solution.

.DESCRIPTION
    A thin wrapper over the dv CLI, kept for pipelines. Day to day, use the shim instead:

        .\dv build            one solution, inferred from where you are
        .\dv build -a Foo     one assembly

    This one additionally builds and tests the shared base, so the tooling and the harness are
    proven to compile before anything depends on them - which is what CI wants.

.PARAMETER Solution
    Restrict to one solution. Default: every solution under CRM/Plugins.

.EXAMPLE
    ./build/build.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',

    [string] $Solution
)

. "$PSScriptRoot/common.ps1"

Invoke-BaseBuild -Configuration $Configuration

$solutions = if ($Solution) { @($Solution) } else { Get-DvSolution }

if (-not $solutions) {
    throw 'No solutions found under CRM/Plugins. Create one with: dotnet new dv-solution -n <Name>'
}

foreach ($name in $solutions) {
    Write-Host ''
    Write-Host "=== $name ===" -ForegroundColor Cyan

    Invoke-Dv build --solution $name --configuration $Configuration
    Invoke-Dv test --solution $name --configuration $Configuration
}

Write-Host ''
Write-Host "Build and validation passed for: $($solutions -join ', ')" -ForegroundColor Green
