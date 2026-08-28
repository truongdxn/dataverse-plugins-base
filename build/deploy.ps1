<#
.SYNOPSIS
    Deploys to a Dataverse environment.

.PARAMETER Environment
    Name from config/environments.json, e.g. dev, test, prod.

.PARAMETER Task
    sync     Register the step configuration directly. Intended for a developer environment.
    import   Import an already-built package. The path used for test and production.
    messages Cache sdkmessage ids into config/sdkmessages.json - run once, then commit the file.
    schema   Refresh table metadata into config/schema.json and regenerate the schema constants.
             Needs -Tables. Commit both the json and the generated .cs.

.PARAMETER Prune
    Only meaningful with -Task sync. DELETES registrations in the environment that are not
    declared in source. Off by default: undeclared steps are reported and left alone.

.EXAMPLE
    ./build/deploy.ps1 -Environment dev  -Task sync
    ./build/deploy.ps1 -Environment test -Task import -Version 1.0.0.42
    ./build/deploy.ps1 -Environment dev  -Task messages
    ./build/deploy.ps1 -Environment dev  -Task schema -Tables contact,account
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Environment,

    [ValidateSet('sync', 'import', 'messages', 'schema')]
    [string] $Task = 'sync',

    # Comma separated table logical names. Required for -Task schema.
    [string] $Tables,

    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [string] $Version,

    [string] $Package,

    # Sync or register only this plugin assembly instead of all of them.
    [string] $Assembly,

    [switch] $Prune,

    [switch] $NoPublish
)

. "$PSScriptRoot/common.ps1"

switch ($Task) {
    'sync' {
        # dv builds the plugin assemblies itself before reading them.
        $arguments = @('sync', '--env', $Environment, '--configuration', $Configuration)
        if ($Assembly) { $arguments += @('--assembly', $Assembly) }
        if ($Prune)    { $arguments += '--prune' }

        Invoke-Dv @arguments
    }

    'import' {
        $arguments = @('import', '--env', $Environment)
        if ($Version)   { $arguments += @('--version', $Version) }
        if ($Package)   { $arguments += @('--package', $Package) }
        if ($NoPublish) { $arguments += '--no-publish' }

        Invoke-Dv @arguments
    }

    'messages' {
        Invoke-Dv messages pull --env $Environment --configuration $Configuration
    }

    'schema' {
        if (-not $Tables) {
            throw "-Task schema needs -Tables, e.g. -Tables contact,account. Pulling the whole org is deliberately not supported."
        }

        # Reads metadata only, and is normally run before the code that will use the generated
        # constants even exists, so nothing is built here.
        Invoke-Dv schema pull --env $Environment --tables $Tables
    }
}

Write-Host ''
Write-Host "Task '$Task' completed for '$Environment'." -ForegroundColor Green
