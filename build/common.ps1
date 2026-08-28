<#
    Shared helpers for the build scripts. Dot-source it:  . "$PSScriptRoot/common.ps1"

    The scripts are deliberately thin. All the logic lives in the dv CLI so that a local run and
    a CI run execute exactly the same code, rather than a script that has quietly drifted from
    what the pipeline does.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = Split-Path -Parent $PSScriptRoot

# Initialised here because Set-StrictMode makes reading a never-assigned variable an error.
$script:DotNet = $null

function Resolve-DotNet {
    <#
        Finds a dotnet that actually carries an SDK.

        A machine can have several installations - commonly a runtime-only one under Program Files
        that comes first on PATH, alongside a user-local one holding the SDK. Taking whatever PATH
        resolves to then fails with "No .NET SDKs were found" even though an SDK is installed. So
        each candidate is checked for a populated sdk folder.
    #>
    if ($script:DotNet) { return $script:DotNet }

    $roots = @()
    if ($env:DOTNET_ROOT) { $roots += $env:DOTNET_ROOT }
    $roots += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet')
    if ($env:ProgramFiles) { $roots += (Join-Path $env:ProgramFiles 'dotnet') }

    # Whatever PATH resolves to, considered last.
    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $roots += (Split-Path -Parent $onPath.Source) }

    foreach ($root in $roots) {
        if (-not $root) { continue }

        $sdk = Join-Path $root 'sdk'
        $exe = Join-Path $root 'dotnet.exe'
        if (-not (Test-Path $exe)) { $exe = Join-Path $root 'dotnet' }

        if ((Test-Path $sdk) -and (Get-ChildItem $sdk -Directory -ErrorAction SilentlyContinue) -and (Test-Path $exe)) {
            $script:DotNet = $exe
            return $script:DotNet
        }
    }

    throw ("No .NET SDK found. Install one from https://dotnet.microsoft.com/download, " +
           "or set DOTNET_ROOT to an installation that has an 'sdk' folder.")
}
$script:ToolProject = Join-Path $script:RepoRoot 'src\Dataverse.Plugins.Tooling\Dataverse.Plugins.Tooling.csproj'
$script:ToolOutput = Join-Path $script:RepoRoot 'artifacts\tool'
$script:ToolBuilt = $false

function Get-RepoRoot {
    $script:RepoRoot
}

function Invoke-Native {
    <#
        Runs an external command and decides success from its exit code alone.

        Windows PowerShell 5.1 turns anything a native command writes to stderr into an
        ErrorRecord, which under $ErrorActionPreference = 'Stop' fails the build over what may
        only have been a warning. Exit code is the only trustworthy signal, so stderr is
        demoted for the duration of the call.
    #>
    param(
        [Parameter(Mandatory)] [scriptblock] $Command,
        [Parameter(Mandatory)] [string] $What
    )

    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Command
    }
    finally {
        $ErrorActionPreference = $previous
    }

    if ($LASTEXITCODE -ne 0) {
        throw "$What failed with exit code $LASTEXITCODE."
    }
}

function Initialize-DvTool {
    <#
        Builds the dv CLI once per script run. Building it separately from the plugin projects
        matters: dv reads the plugin assemblies, so it has to exist before they are inspected.
    #>
    if ($script:ToolBuilt) { return }

    Write-Host 'Building dv tooling...' -ForegroundColor Cyan
    Invoke-Native -What 'Building the dv tooling' -Command {
        & (Resolve-DotNet) build $script:ToolProject --configuration Release --output $script:ToolOutput --nologo -v quiet
    }

    $script:ToolBuilt = $true
}

function Invoke-Dv {
    <#
        Runs the dv CLI, throwing on a non-zero exit so a failing step stops the pipeline
        instead of being reported as success.
    #>
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]] $Arguments)

    Initialize-DvTool

    $dll = Join-Path $script:ToolOutput 'dv.dll'
    if (-not (Test-Path $dll)) {
        throw "dv was not produced at $dll."
    }

    Push-Location $script:RepoRoot
    try {
        Invoke-Native -What "dv $($Arguments -join ' ')" -Command {
            & (Resolve-DotNet) $dll @Arguments
        }
    }
    finally {
        Pop-Location
    }
}

function Invoke-PluginBuild {
    <#
        Builds every project except the tooling. dv reads the compiled assemblies, so this must
        run before manifest, pack or sync.
    #>
    param([string] $Configuration = 'Debug')

    Write-Host "Building plugin assemblies ($Configuration)..." -ForegroundColor Cyan

    $solution = Join-Path $script:RepoRoot 'DataverseBase.sln'
    Invoke-Native -What 'Building the solution' -Command {
        & (Resolve-DotNet) build $solution --configuration $Configuration --nologo -v minimal
    }
}
