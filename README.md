# Dataverse plugin base

A development-phase toolkit for Dataverse plugins. One shared base serves many PowerApps
solutions; each solution builds its own `.zip`.

Steps are declared as attributes on the plugin class:

```csharp
[PluginStep(Messages.Update, Contact.EntityLogicalName,
    Stage = Stage.PostOperation,
    FilteringAttributes = new[] { Contact.Fields.FirstName },
    PreImage = new[] { Contact.Fields.FirstName })]
public class ContactPostUpdate : PluginBase { ... }
```

That declaration is the only source of truth. It feeds a manifest, and both outputs read only the
manifest — so registering into a sandbox and packing a solution cannot disagree about what a step
is.

## What this does, and where it stops

| | |
|---|---|
| **Does** | Declare steps in code · build · unit-test plugins · register into a dev sandbox · pack a solution `.zip` |
| **Does not** | Import that `.zip` anywhere. No test or production environments, no credentials, no deploy pipeline. |

Importing is another module's job. This repo hands over a `.zip` and stops, which is why nothing
here needs a client secret.

## Why

Registering steps by hand in the Plugin Registration Tool means the registration lives only in an
environment. Nobody can review it, it does not travel with the code that depends on it, and two
environments drift apart quietly.

Putting the declaration next to the code it describes fixes all three. The typed schema constants
close the last gap: a mistyped column name becomes a build error rather than a step that deploys
cleanly and never fires.

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download) (the tooling; it also builds the net462
  plugin assemblies via `Microsoft.NETFramework.ReferenceAssemblies`)
- [Power Platform CLI](https://learn.microsoft.com/power-platform/developer/cli/introduction) —
  `dotnet tool install --global Microsoft.PowerApps.CLI.Tool`. Needed only for `dv pack`.
- Windows for packing and for running plugin tests (net462).

## Quick start

```bash
.\dv new solution CRMCore           # create a solution (or adopt a folder the IDE made)
.\dv solutions                       # what is in this repo
.\dv build -a Sample.Plugins         # build and validate one assembly
.\dv test  -a Sample.Plugins         # run its tests
.\dv sync  -e dev -a Sample.Plugins  # register it into your sandbox
.\dv pack  -s Sample --version 1.0.0.1
```

Nothing has to be installed to get `dv`: it builds itself into `artifacts/tool` on first use and
rebuilds when its sources change. On a machine without the shims, or if `.\dv` is not recognised:

```bash
dotnet run --project CRM/Shared/PluginBase/src/Dataverse.Plugins.Tooling -- solutions
```

Full walkthrough, from creating a solution to syncing it: **[docs/getting-started.md](docs/getting-started.md)**.

## Working on one assembly

Several people share this repo, so every command narrows:

- `-s, --solution <name>` — which PowerApps solution. Usually unnecessary: the working directory
  decides. `cd CRM/Plugins/CRMCore && .\..\..\..\dv build` acts on CRMCore.
- `-a, --assembly <name>` — one plugin assembly, for build, test, sync, manifest and pack.

Nothing you do to your assembly touches anyone else's.

## Layout

```
dv.json                          repo settings; its presence marks the repo root
config/
  environments.json              developer sandboxes (dev only)
  schema.json                    table metadata snapshot — one for the repo
  sdkmessages.json               message id cache — one for the repo

config/Generated/Schema.g.cs     typed constants, generated and committed

CRM/Shared/PluginBase/           the shared base, used by every solution
  src/Dataverse.Plugins.Abstractions/   [PluginStep], PluginBase — linked as source
  src/Dataverse.Plugins.Testing/        fake pipeline harness for unit tests
  src/Dataverse.Plugins.Tooling/        the dv CLI
  tests/                                the tooling's own tests
  vsix/                                 the Visual Studio templates extension

CRM/Plugins/<Solution>/          one folder per PowerApps solution — SOURCE
  solution.json                    publisher and solution identity — the only file it needs
  <Assembly>/                      a plugin assembly project
  <Assembly>.Tests/                its tests

CRM/Solutions/<Solution>/        the packed .zip — OUTPUT, git-ignored
```

The split: **anything describing the org is repo-wide; only identity and code are per solution.**
Every solution here targets the same org, so a schema snapshot per solution would only be the same
file several times over.

A project belongs to the solution whose folder it sits in — the nearest `solution.json` above it.
Both MSBuild and `dv` apply that same rule, so they cannot disagree, and moving a project between
solutions is a move with no file to edit afterwards.

Visual Studio can create the *projects* but not `solution.json` — a solution folder is config, not
a project. So `dv new solution <Name>` both creates a solution and adopts a folder the IDE already
made, and a folder without it is reported by `dv solutions` under "Not configured yet" rather than
silently ignored.

## Using the base in another repo

Nothing above `CRM/Plugins/` has to be copied. The base ships as four packages — the abstractions
as **source** (the sandbox resolves no dependencies, so the types must compile into your own
assembly), the test harness as an assembly, `dv` as a dotnet tool, and the templates:

```bash
.\build\pack-packages.ps1 -Clean -Version 1.0.0-local.1
```

A consumer repo then needs `dv.json`, `config/`, `CRM/Plugins/`, a `NuGet.config` pointing at the
packages, and one reference per plugin project:

```xml
<PackageReference Include="Dataverse.Plugins.Abstractions" Version="1.0.0" />
```

No import, no props path, no `CRM/Shared/` folder. Full instructions, and how to point at a real
feed later: **[docs/packaging.md](docs/packaging.md)**.

## What is and is not committed

**Committed:** step declarations (in code), `solution.json`, `config/schema.json`,
`config/sdkmessages.json`, `config/Generated/Schema.g.cs`.

**Not committed:** `bin/`, `obj/`, `artifacts/`, `CRM/Solutions/` (the packed `.zip`),
`config/environments.local.json`.

No package is stored in the repo. `dv pack` rebuilds it from the declarations whenever it is
wanted, so a `.zip` can never go stale against the code.

## Docs

- [Getting started](docs/getting-started.md) — the end-to-end walkthrough
- [Testing](docs/testing.md) — writing plugin unit tests
- [Packaging](docs/packaging.md) — shipping the base as packages, and using it elsewhere
- [Architecture](docs/architecture.md) — how it works and why, including what is unverified
