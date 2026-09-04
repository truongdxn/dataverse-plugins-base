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
      File > New > Project ..... "Dataverse plugin tests"
      Add  > New Item .......... "Dataverse plugin"

    Creating a whole SOLUTION folder is not offered here, and cannot be: it is a config file
    rather than a project, and the New Project dialog only produces projects. Create the project
    through the GUI, then run this once so dv can see the folder:

      dv new solution <Name>

    It also adopts a folder you already created, which is exactly this situation.
#>
[CmdletBinding()]
param(
    [string] $VsVersion = '2022',
    [switch] $Uninstall,

    # Stage the three templates into this folder and stop - do not zip, do not install.
    # The VSIX build calls this, so the extension and this script cannot disagree about what a
    # template contains: there is one definition, under templates/dotnet, and one piece of code
    # that turns it into Visual Studio's shape.
    [string] $StageOnly
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
                        (Join-Path $projectTarget 'DataversePluginTests.zip'),
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
#
# DV_PKG_VERSION has no Visual Studio equivalent to expand into - the New Project dialog has no
# parameter to ask for it - so it takes the same default 'dotnet new' uses. It only matters outside
# this repo: in here $(AbstractionsSourcesProps) is set, the sources are imported by path, and the
# PackageReference is conditioned out unread. Pin it once there is a real feed.
$projectReplacements = [ordered]@{
    'DvPluginAssembly' = '$safeprojectname$'
    'DV_ISOLATION'     = 'Sandbox'
    'DV_PKG_VERSION'   = '*'
}

$testReplacements = [ordered]@{
    'DvPluginTests'  = '$safeprojectname$'
    'DV_TESTS_FOR'   = '$safeprojectname$'
    'DV_PKG_VERSION' = '*'
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

$staging = if ($StageOnly) {
    New-Item -ItemType Directory -Force -Path $StageOnly | Out-Null
    (Resolve-Path $StageOnly).Path
}
else {
    Join-Path ([System.IO.Path]::GetTempPath()) ("dv-templates-" + [guid]::NewGuid().ToString('N'))
}

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
    <Description>A plugin assembly that registers itself with the dv tooling. Create it inside a solution folder (CRM\Plugins\&lt;Solution&gt;\), which is what decides the solution it belongs to.</Description>
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
    if (-not $StageOnly) {
        New-TemplateZip -StagingDirectory $projectStaging -ZipPath (Join-Path $projectTarget 'DataversePluginAssembly.zip')
    }

    # ---------- Test project template ----------
    $testStaging = Join-Path $staging 'tests'
    $testSource = Join-Path $sourceRoot 'dv-plugin-tests'

    Copy-WithTokens -Source (Join-Path $testSource 'DvPluginTests.csproj') `
                    -Destination (Join-Path $testStaging 'DvPluginTests.csproj') `
                    -Replacements $testReplacements
    Copy-WithTokens -Source (Join-Path $testSource 'ExamplePluginTests.cs') `
                    -Destination (Join-Path $testStaging 'ExamplePluginTests.cs') `
                    -Replacements $testReplacements

    $testVsTemplate = @'
<?xml version="1.0" encoding="utf-8"?>
<VSTemplate Version="3.0.0" Type="Project" xmlns="http://schemas.microsoft.com/developer/vstemplate/2005">
  <TemplateData>
    <Name>Dataverse plugin tests</Name>
    <Description>Tests for one plugin assembly, wired to the fake pipeline harness. Name it &lt;Assembly&gt;.Tests and it picks up the right DataverseTestsFor; correct it in the .csproj otherwise.</Description>
    <ProjectType>CSharp</ProjectType>
    <LanguageTag>C#</LanguageTag>
    <PlatformTag>Windows</PlatformTag>
    <ProjectTypeTag>Dataverse</ProjectTypeTag>
    <SortOrder>1010</SortOrder>
    <CreateNewFolder>true</CreateNewFolder>
    <DefaultName>Plugins.Tests</DefaultName>
    <ProvideDefaultName>true</ProvideDefaultName>
    <LocationField>Enabled</LocationField>
    <EnableLocationBrowseButton>true</EnableLocationBrowseButton>
  </TemplateData>
  <TemplateContent>
    <Project TargetFileName="$safeprojectname$.csproj" File="DvPluginTests.csproj" ReplaceParameters="true">
      <ProjectItem ReplaceParameters="true" TargetFileName="ExamplePluginTests.cs">ExamplePluginTests.cs</ProjectItem>
    </Project>
  </TemplateContent>
</VSTemplate>
'@

    Set-Content -LiteralPath (Join-Path $testStaging 'MyTemplate.vstemplate') -Value $testVsTemplate -Encoding utf8
    if (-not $StageOnly) {
        New-TemplateZip -StagingDirectory $testStaging -ZipPath (Join-Path $projectTarget 'DataversePluginTests.zip')
    }

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
    if (-not $StageOnly) {
        New-TemplateZip -StagingDirectory $itemStaging -ZipPath (Join-Path $itemTarget 'DataversePlugin.zip')
    }

    if ($StageOnly) {
        Write-Host ''
        Write-Host "Staged templates into $staging" -ForegroundColor Green
        return
    }

    Write-Host ''
    Write-Host 'Restart Visual Studio, then:' -ForegroundColor Cyan
    Write-Host '  File > New > Project  ->  "Dataverse plugin assembly"'
    Write-Host '  File > New > Project  ->  "Dataverse plugin tests"'
    Write-Host '  Add  > New Item       ->  "Dataverse plugin"'
    Write-Host ''
    Write-Host 'Create the project inside a solution folder: CRM\Plugins\<Solution>' -ForegroundColor Cyan
}
finally {
    # Staged output is the caller's to keep.
    if (-not $StageOnly -and (Test-Path $staging)) {
        Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}
