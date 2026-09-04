# The Visual Studio extension

Runs dv from Solution Explorer: right-click a plugin assembly to build, test or sync **that**
assembly, right-click the solution for everything else. Output goes to a **Dataverse** pane in the
Output window.

It is a front end and nothing more. Every menu item starts the same `dv` a developer would type, in
the repo the click belongs to, so the IDE and the terminal cannot disagree about what a command
does.

> **Not built yet.** The machine this was written on has no Visual Studio, so the extension has
> never been compiled, loaded or clicked. Its logic is covered by tests that do run
> (`vsix/tests/`); the Visual Studio layer is unproven. Expect to iterate on the first build.

## The menus

**Right-click a project** — everything narrows to that assembly with `-a`, run from the project's
folder:

| Menu item | Runs |
|---|---|
| Build assembly | `dv build -a <Project> -c <ActiveConfig>` |
| Test assembly | `dv test -a <Project> -c <ActiveConfig>` |
| Validate registrations | `dv validate -a <Project>` |
| Write manifest | `dv manifest -a <Project>` |
| Sync to dev environment… | `dv sync -a <Project> -e <env>` |
| New plugin class… | `dv new plugin <Name> --entity --message --stage` |
| New test project… | `dv new tests <Name> --for <Project>` |

**Right-click the solution node** — repo-wide:

| Menu item | Runs |
|---|---|
| Build all assemblies / Run plugin tests / Validate / Write manifest | the same without `-a` |
| Pack solution .zip… | `dv pack --version <x.y.z.b>` |
| Sync to dev environment… | `dv sync -e <env>` |
| List solutions / List environments | `dv solutions`, `dv environments` |
| Pull schema… | `dv schema pull -e <env> [-t <tables>]` |
| Regenerate schema constants | `dv schema codegen` |
| Pull message ids… | `dv messages pull -e <env>` |
| New solution… | `dv new solution <Name> [--prefix] [--unique-name]` |
| New plugin assembly… | `dv new assembly <Name>` |

The configuration comes from Visual Studio's own configuration dropdown, so selecting Release and
clicking Build gets a Release build.

`-s` is never passed. The command runs in the clicked project's folder and dv infers the solution
from it, so the folder and the flag cannot contradict each other.

## Two things worth knowing

**Sync can prune.** The sync dialog has a "delete registrations that are not declared in source"
tickbox. It is off by default and says what it does: on, anything registered in the environment but
missing from the code is removed.

**Nothing reloads itself.** The `new …` commands write projects to disk; Visual Studio does not
notice. Use **Add → Existing Project**, or reload the solution. The pane says so after every
scaffold.

## Building it

Needs **Visual Studio 2022 with the "Visual Studio extension development" workload**. The project
is deliberately outside `PluginBase.sln` and outside CI, so nobody who only writes plugins has to
install that workload.

Open `CRM\Shared\PluginBase\vsix\Dataverse.Plugins.VsCommands\Dataverse.Plugins.VsCommands.csproj`
and press **F5**. That launches a second Visual Studio — the *experimental instance* — with the
extension loaded and your real installation untouched. It is the whole debug loop.

To produce something installable, build Release and take
`bin\Release\Dataverse.Plugins.VsCommands.vsix`. It installs by double-clicking; unsigned, so
Visual Studio shows a trust prompt.

From a Developer Command Prompt instead:

```
msbuild CRM\Shared\PluginBase\vsix\Dataverse.Plugins.VsCommands\Dataverse.Plugins.VsCommands.csproj /p:Configuration=Release /restore
```

## How it finds dv

Walking up from the clicked item for `dv.json` — the same rule `RepoPaths.Discover` uses in the
CLI. No repo above the click means no menu: the "Dataverse" submenu hides itself outside a dv repo
rather than appearing on every project a developer ever opens.

Having found the root, it runs, in order of preference:

1. `powershell -NoProfile -ExecutionPolicy Bypass -File <root>\dv.ps1 …` — the shim, which already
   knows how to find a dotnet with an SDK, whether to build the tool from source or run the pinned
   package, and when the build is stale.
2. `dotnet tool run dv …` — a consumer repo that has a `.config/dotnet-tools.json` but never copied
   the shims.
3. Neither: the pane says which of the two to set up.

Re-deciding any of that inside the extension would be a second implementation to keep in step with
the first, and it would drift.

## Why the environment picker asks dv

The sync and pull dialogs fill their environment list by running `dv environments` and parsing the
result, rather than reading `config/environments.json`. That file is merged with the git-ignored
`config/environments.local.json`, and a developer who has pointed `dev` at their own sandbox must
see their own URL in Visual Studio too. One place owns that merge, and it is the CLI.

The printed shape is therefore a contract, pinned from both sides: `EnvironmentsCommandTests` in
the tooling suite, `EnvironmentListTests` in the extension's.

## Layout

```
CRM/Shared/PluginBase/vsix/
  Dataverse.Plugins.Vsix/          the project and item templates (a separate extension)
  Dataverse.Plugins.VsCommands/    this extension
  core/                            plain C#, no Visual Studio references
  tests/                           xunit over core/, and part of PluginBase.sln
```

`core/` is **source-linked** into both the extension (net472) and the tests (net8.0). It holds
everything that decides which repo a click belongs to, what dv should be started as, and what
arguments it gets — which is to say, everything that can be wrong without Visual Studio being
involved. That is what makes any of this testable on a machine with no VS SDK.

## Two extensions, for now

The templates ship as their own `.vsix` ([packaging.md](packaging.md)). Folding them together is a
few lines of manifest and project file once the commands are proven, and then a new team member
installs one thing.
