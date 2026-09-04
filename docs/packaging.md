# Packaging the base

The base ships as four installable artifacts, so a repo that only writes plugins carries none of
its source.

| Package | Kind | What it gives you |
|---|---|---|
| `Dataverse.Plugins.Abstractions` | **source-only** | `[PluginStep]`, `PluginBase`, and the generated schema constants, compiled into *your* assembly |
| `Dataverse.Plugins.Testing` | assembly | `PluginTestHost` and the fakes |
| `Dataverse.Plugins.Tooling` | dotnet tool | the `dv` CLI |
| `Dataverse.Plugins.Templates` | dotnet new templates | the three project/item templates |

## Building them

```bash
.\build\pack-packages.ps1 -Clean -Version 1.0.0-local.1
```

Writes four `.nupkg` into `artifacts/packages/`, which `NuGet.config` already lists as a package
source — so they are restorable immediately, with no feed and no credentials.

CI does the same and publishes them as build artifacts. **Nothing is pushed anywhere yet.**

## Using the base in another repo

A consumer repo needs no `CRM/Shared/` folder at all. It needs:

```
dv.json                                repo marker + defaults
NuGet.config                           pointing at wherever the packages are
.config/dotnet-tools.json              pins the dv version
config/environments.json               dev sandboxes
config/schema.json                     metadata snapshot
config/sdkmessages.json                message id cache
config/Generated/Schema.g.cs           generated, committed
CRM/Plugins/<Solution>/                solution.json + projects
```

Point `NuGet.config` at the packages — for now, the folder they were built into:

```xml
<add key="dv-local" value="\\some\share\dv-packages" />
```

Then:

```bash
dotnet new tool-manifest
dotnet tool install Dataverse.Plugins.Tooling
dotnet dv new solution CRMCore --prefix contoso
dotnet dv schema codegen
dotnet dv build
```

Consumers run `dotnet dv`; the `.\dv` shims are a convenience of this repo. Copy them if you like —
they detect a consumer repo (no tool source, a tool manifest present) and forward to the restored
tool.

A plugin project then declares one reference and nothing else:

```xml
<PackageReference Include="Dataverse.Plugins.Abstractions" Version="1.0.0" />
```

No `<Import>`: the package's `buildTransitive` props is applied automatically, and brings the
markers, the `solution.json` walk-up, the schema glob and the SDK dependencies with it.

## Two things that will bite

**A package cannot supply `TargetFramework`.** The props arrives through restore, and restore needs
a TFM before it will run. A test project that leaves its TFM to the props cannot restore, and then
looks like it simply is not a test project. The templates declare `net462` explicitly; keep it.

**`dv` restores before it evaluates.** Project properties come from MSBuild, and the props that
sets them only reaches the project once `obj/*.nuget.g.props` exists. Discovery therefore restores
each candidate first. In a repo importing the props by path there is nothing to restore and the
step is a no-op.

## Why the abstractions ship as source

The Dataverse sandbox loads exactly one assembly per registered plugin type and resolves no
dependencies. A plugin *referencing* `Dataverse.Plugins.Abstractions.dll` would compile and then
fail at runtime with an assembly-load error.

So the package ships `contentFiles` and a props that turns them into `<Compile>` items, and
deliberately **contains no `lib/`** — there is nothing to accidentally reference. You can confirm
it on any built consumer: the plugin DLL contains `PluginBase` and the schema constants, and the
output folder holds no `Dataverse.Plugins.Abstractions.dll`.

The test harness has no such constraint — test projects are never sandboxed — so it is an ordinary
assembly package.

## Moving to a real feed later

Everything above is feed-agnostic. Publishing is one source change plus one CI step.

| Feed | Push | What consumers need |
|---|---|---|
| **Folder / UNC share** | copy the `.nupkg` | a path in `NuGet.config`. No credentials |
| **Azure Artifacts** | `dotnet nuget push` with the pipeline token | the Azure Artifacts Credential Provider, or a PAT in `%USERPROFILE%\.nuget\NuGet\NuGet.Config` — **never** in the repo |
| **GitHub Packages** | `dotnet nuget push` with `GITHUB_TOKEN` | a PAT with `read:packages` — **required even for public packages**, which catches most teams out |
| **nuget.org** | `dotnet nuget push` with an API key | nothing. Only appropriate if this is genuinely open source |

Whichever you choose, replace the `local` source in `NuGet.config`, add the push step, and pin
versions: the templates default `Version` to `*`, which is fine against a local folder and a poor
idea once several people build at once.

## The VSIX

`CRM/Shared/PluginBase/vsix/Dataverse.Plugins.Vsix` packages the Visual Studio templates as a
`.vsix`. It is deliberately **not** in `PluginBase.sln` and not built by CI: building a VSIX needs
the *Visual Studio extension development* workload, and requiring that of everyone who touches this
repo — to produce something only GUI users install — is a bad trade.

Build it from a Developer Command Prompt:

```
msbuild CRM\Shared\PluginBase\vsix\Dataverse.Plugins.Vsix\Dataverse.Plugins.Vsix.csproj /p:Configuration=Release /restore
```

The output `.vsix` installs by double-clicking. Unsigned, so Visual Studio shows a trust prompt.

Its template content is generated at build time by `templates/install-vs-templates.ps1 -StageOnly`,
the same script that installs templates by hand — so the extension and the script cannot disagree
about what a template contains.

> **Unverified.** This was written on a machine with no Visual Studio, so the VSIX has never been
> built or installed. Expect to iterate on the first build. `install-vs-templates.ps1` works today
> and is the fallback.
