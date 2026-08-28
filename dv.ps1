<#
.SYNOPSIS
    Runs the dv CLI from the repo, building it first if it is out of date.

.DESCRIPTION
    The entry point for everything: .\dv build, .\dv sync -e dev, .\dv pack --version 1.0.0.42

    Nothing has to be installed. The tool is built into artifacts/tool on first use and rebuilt
    only when its sources change, so it always matches the code in your working tree.

    Set DV_NO_BUILD=1 to skip the staleness check in a tight loop. It is an environment variable
    rather than a switch because this script cannot tell its own arguments from dv's.

.EXAMPLE
    .\dv --help
    .\dv schema pull -e dev -t contact,account
#>
# No param() block on purpose. With one, PowerShell's binder claims arguments meant for dv:
# '-a Sample.Plugins' binds to a parameter named -Arguments as a partial match, and the script
# never sees it. Without a param block every token lands in $args untouched.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$toolProject = Join-Path $repoRoot 'src\Dataverse.Plugins.Tooling\Dataverse.Plugins.Tooling.csproj'
$toolDirectory = Join-Path $repoRoot 'artifacts\tool'
$toolDll = Join-Path $toolDirectory 'dv.dll'

function Resolve-DotNet {
    <#
        Finds a dotnet that actually carries an SDK. A machine can have several installations -
        commonly a runtime-only one under Program Files that comes first on PATH, alongside a
        user-local one holding the SDK - and picking the wrong one fails with "No .NET SDKs were
        found" even though an SDK is installed.
    #>
    $roots = @()
    if ($env:DOTNET_ROOT) { $roots += $env:DOTNET_ROOT }
    if ($env:LOCALAPPDATA) { $roots += (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet') }
    if ($env:ProgramFiles) { $roots += (Join-Path $env:ProgramFiles 'dotnet') }

    $onPath = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($onPath) { $roots += (Split-Path -Parent $onPath.Source) }

    foreach ($root in $roots) {
        if (-not $root) { continue }

        $sdk = Join-Path $root 'sdk'
        $exe = Join-Path $root 'dotnet.exe'
        if (-not (Test-Path $exe)) { $exe = Join-Path $root 'dotnet' }

        if ((Test-Path $sdk) -and (Get-ChildItem $sdk -Directory -ErrorAction SilentlyContinue) -and (Test-Path $exe)) {
            return $exe
        }
    }

    throw ("No .NET SDK found. Install one from https://dotnet.microsoft.com/download, " +
           "or set DOTNET_ROOT to an installation that has an 'sdk' folder.")
}

function Test-ToolIsStale {
    if (-not (Test-Path $toolDll)) { return $true }

    $builtAt = (Get-Item $toolDll).LastWriteTimeUtc
    $sourceDirectory = Join-Path $repoRoot 'src\Dataverse.Plugins.Tooling'

    # Any source or project file newer than the built dll means a rebuild is due. Cheaper than
    # running an incremental build on every single command.
    $newer = Get-ChildItem $sourceDirectory -Recurse -File -Include *.cs, *.csproj -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.LastWriteTimeUtc -gt $builtAt } |
        Select-Object -First 1

    return $null -ne $newer
}

$dotnet = Resolve-DotNet

if ((-not $env:DV_NO_BUILD) -and (Test-ToolIsStale)) {
    Write-Host 'Building dv...' -ForegroundColor DarkGray

    # Native stderr must not become a PowerShell error here; the exit code is the real signal.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $dotnet build $toolProject --configuration Release --output $toolDirectory --nologo -v quiet
    }
    finally {
        $ErrorActionPreference = $previous
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Building dv failed with exit code $LASTEXITCODE."
        exit $LASTEXITCODE
    }
}

$previous = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & $dotnet $toolDll @args
}
finally {
    $ErrorActionPreference = $previous
}

# Propagate the tool's exit code so CI and && chains behave.
exit $LASTEXITCODE
