<#
.SYNOPSIS
    Builds everything and validates every step declaration.

.DESCRIPTION
    A thin wrapper over the dv CLI, kept for pipelines. Day to day, use the shim instead:

        .\dv build

    This one additionally builds the whole solution, so the tests and the tooling are proven to
    compile too - which is what CI wants before it runs dotnet test.

.EXAMPLE
    ./build/build.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

. "$PSScriptRoot/common.ps1"

Invoke-PluginBuild -Configuration $Configuration

# --no-build because the solution build above already produced the assemblies.
Invoke-Dv validate --configuration $Configuration --no-build

Write-Host ''
Write-Host 'Build and validation passed.' -ForegroundColor Green
