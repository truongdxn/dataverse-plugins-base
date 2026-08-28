<#
.SYNOPSIS
    Installs the Visual Studio project and item templates for Dataverse plugins.

.DESCRIPTION
    Visual Studio templates are just zip files in a well-known folder, so this needs no VSIX to
    build, sign or install - and no admin rights.

    The template content is NOT duplicated for Visual Studio. It is generated from the same files
    under templates/dotnet that 'dotnet new' uses, substituting the placeholder tokens for the
    parameters each system understands. One source of truth, two outputs, so the two cannot drift
    apart.

.PARAMETER VsVersion
    Visual Studio version folder under Documents. Defaults to 2022.

.PARAMETER Uninstall
    Remove the installed templates instead of installing them.

.EXAMPLE
    pwsh templates/install-vs-templates.ps1

    Then restart Visual Studio. The templates appear as:
      File > New > Project ..... "Dataverse plugin assembly"
      Add  > New Item .......... "Dataverse plugin"
#>
[CmdletBinding()]
param(
    [string] $VsVersion = '2022',
    [switch] $Uninstall
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$templatesRoot = $PSScriptRoot
$sourceRoot = Join-Path $templatesRoot 'dotnet'

$documents = [Environment]::GetFolderPath('MyDocuments')
if ([string]::IsNullOrWhiteSpace($documents)) {
    throw 'Could not locate the Documents folder, which is where Visual Studio keeps user templates.'
}

$vsTemplateRoot = Join-Path $documents "Visual Studio $VsVersion\Templates"
$projectTarget = Join-Path $vsTemplateRoot 'ProjectTemplates\Dataverse'
$itemTarget = Join-Path $vsTemplateRoot 'ItemTemplates\CSharp\Dataverse'

if ($Uninstall) {
    foreach ($path in @((Join-Path $projectTarget 'DataversePluginAssembly.zip'),
                        (Join-Path $itemTarget 'DataversePlugin.zip'))) {
        if (Test-Path $path) {
            Remove-Item $path -Force
            Write-Host "Removed $path"
        }
    }
    Write-Host 'Uninstalled. Restart Visual Studio.' -ForegroundColor Green
    return
}

# Tokens the shared template sources use, mapped to the parameters Visual Studio expands.
# 'dotnet new' maps the same tokens itself via .template.config/template.json.
$projectReplacements = [ordered]@{
    'DvPluginAssembly' = '$safeprojectname$'
    'DV_ISOLATION'     = 'Sandbox'
}

$itemReplacements = [ordered]@{
    'DvPluginNamespace' = '$rootnamespace$'
    'DvPluginClass'     = '$safeitemname$'
    'DV_MESSAGE'        = 'Update'
    'DV_ENTITY'         = 'account'
    'DV_STAGE'          = 'PostOperation'
}

function Copy-WithTokens {
    param(
        [Parameter(Mandatory)] [string] $Source,
        [Parameter(Mandatory)] [string] $Destination,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $Replacements,
        [switch] $Raw
    )

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null

    if ($Raw) {
        Copy-Item -LiteralPath $Source -Destination $Destination -Force
        return
    }

    $content = Get-Content -LiteralPath $Source -Raw
    foreach ($token in $Replacements.Keys) {
        $content = $content.Replace($token, $Replacements[$token])
    }
    Set-Content -LiteralPath $Destination -Value $content -Encoding utf8
}

function New-TemplateZip {
    param(
        [Parameter(Mandatory)] [string] $StagingDirectory,
        [Parameter(Mandatory)] [string] $ZipPath
    )

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $ZipPath) | Out-Null
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }

    Compress-Archive -Path (Join-Path $StagingDirectory '*') -DestinationPath $ZipPath -Force
    Write-Host "Installed $ZipPath" -ForegroundColor Green
}

$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("dv-templates-" + [guid]::NewGuid().ToString('N'))

try {
    # ---------- Project template ----------
    $projectStaging = Join-Path $staging 'project'
    $projectSource = Join-Path $sourceRoot 'dv-plugin-assembly'

    Copy-WithTokens -Source (Join-Path $projectSource 'DvPluginAssembly.csproj') `
                    -Destination (Join-Path $projectStaging 'DvPluginAssembly.csproj') `
                    -Replacements $projectReplacements
    Copy-WithTokens -Source (Join-Path $projectSource 'ExamplePlugin.cs') `
                    -Destination (Join-Path $projectStaging 'ExamplePlugin.cs') `
                    -Replacements $projectReplacements
    $projectVsTemplate = @'
<?xml version="1.0" encoding="utf-8"?>
<VSTemplate Version="3.0.0" Type="Project" xmlns="http://schemas.microsoft.com/developer/vstemplate/2005">
  <TemplateData>
    <Name>Dataverse plugin assembly</Name>
    <Description>A plugin assembly that registers itself with the dv tooling. Create it under src\ so the relative import of Abstractions.Sources.props resolves.</Description>
    <ProjectType>CSharp</ProjectType>
    <LanguageTag>C#</LanguageTag>
    <PlatformTag>Windows</PlatformTag>
    <ProjectTypeTag>Dataverse</ProjectTypeTag>
    <SortOrder>1000</SortOrder>
    <CreateNewFolder>true</CreateNewFolder>
    <DefaultName>Plugins</DefaultName>
    <ProvideDefaultName>true</ProvideDefaultName>
    <LocationField>Enabled</LocationField>
    <EnableLocationBrowseButton>true</EnableLocationBrowseButton>
  </TemplateData>
  <TemplateContent>
    <Project TargetFileName="$safeprojectname$.csproj" File="DvPluginAssembly.csproj" ReplaceParameters="true">
      <ProjectItem ReplaceParameters="true" TargetFileName="ExamplePlugin.cs">ExamplePlugin.cs</ProjectItem>
    </Project>
  </TemplateContent>
</VSTemplate>
'@

    Set-Content -LiteralPath (Join-Path $projectStaging 'MyTemplate.vstemplate') -Value $projectVsTemplate -Encoding utf8
    New-TemplateZip -StagingDirectory $projectStaging -ZipPath (Join-Path $projectTarget 'DataversePluginAssembly.zip')

    # ---------- Item template ----------
    $itemStaging = Join-Path $staging 'item'
    $itemSource = Join-Path $sourceRoot 'dv-plugin'

    Copy-WithTokens -Source (Join-Path $itemSource 'DvPluginClass.cs') `
                    -Destination (Join-Path $itemStaging 'DvPluginClass.cs') `
                    -Replacements $itemReplacements

    $itemVsTemplate = @'
<?xml version="1.0" encoding="utf-8"?>
<VSTemplate Version="3.0.0" Type="Item" xmlns="http://schemas.microsoft.com/developer/vstemplate/2005">
  <TemplateData>
    <Name>Dataverse plugin</Name>
    <Description>A plugin class with its [PluginStep] registration already filled in. Edit the message, table and stage after adding it.</Description>
    <ProjectType>CSharp</ProjectType>
    <SortOrder>1000</SortOrder>
    <DefaultName>Plugin.cs</DefaultName>
  </TemplateData>
  <TemplateContent>
    <ProjectItem ReplaceParameters="true" TargetFileName="$fileinputname$.cs">DvPluginClass.cs</ProjectItem>
  </TemplateContent>
</VSTemplate>
'@

    Set-Content -LiteralPath (Join-Path $itemStaging 'MyTemplate.vstemplate') -Value $itemVsTemplate -Encoding utf8
    New-TemplateZip -StagingDirectory $itemStaging -ZipPath (Join-Path $itemTarget 'DataversePlugin.zip')

    Write-Host ''
    Write-Host 'Restart Visual Studio, then:' -ForegroundColor Cyan
    Write-Host '  File > New > Project  ->  "Dataverse plugin assembly"'
    Write-Host '  Add  > New Item       ->  "Dataverse plugin"'
}
finally {
    if (Test-Path $staging) {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}
