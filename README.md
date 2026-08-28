# Dataverse plugin base

A code base for Dataverse plugins where **step registrations live in the repo**, not in the Plugin
Registration Tool.

Adding a step is editing a file and committing it. Deploying is running a script. The same
declarations that register steps into a developer environment also build the solution package that
goes to test and production, so the two cannot drift apart.

```
src/<Assembly>/  [PluginStep] attributes ─► manifest ─┬─► pack ─► .zip ─► import (test/prod)
                                                      └─► sync (dev, direct registration)
```

## Why

Registering steps by hand has costs this removes:

- **Invisible to review.** A step registered in a tool never appears in a pull request. Here a
  changed filtering attribute is a one-line diff.
- **Drifts between environments.** Registering separately per environment means dev and production
  disagree, and nobody finds out until something misfires.
- **Not reproducible.** Packaging by exporting from a dev org bakes in whatever that org happened
  to contain. Here the package is generated from source and needs no environment at all.
- **Typos fail silently.** A mistyped column in a filtering attribute produces a step that deploys
  cleanly and never runs. Here table, column and message names are generated constants, so a typo
  is a build error.

## Prerequisites

| Tool | Why |
|---|---|
| [.NET SDK 8+](https://dotnet.microsoft.com/download) | Builds everything. `net462` needs no Visual Studio — `Microsoft.NETFramework.ReferenceAssemblies` handles it. A user-local install is fine: the tooling finds an SDK even when a runtime-only dotnet comes first on PATH |
| [Power Platform CLI](https://aka.ms/PowerPlatformCLI) (`pac`) | Builds the solution package |
| PowerShell | The `dv` shim and build scripts. Windows PowerShell 5.1 and PowerShell 7 both work |

Visual Studio is optional — only needed for the New Project / Add New Item templates.

## Quick start

Everything goes through one command, `dv`, at the repo root. Nothing to install: it builds itself
and your plugin assemblies on first use.

```powershell
.\dv build
```

That builds the plugin assemblies and validates every step declaration, with no Dataverse
connection.

On a machine where the shim files are not present, the same thing works straight from the source
tree — `dotnet run --project src/Dataverse.Plugins.Tooling -- build`, with everything after `--`
passed to the tool. See [Troubleshooting](docs/getting-started.md#troubleshooting).

Then point `config/environments.json` at your environments and, once per repo:

```powershell
.\dv schema pull -e dev -t contact,account
.\dv messages pull -e dev
```

The first generates typed constants for the tables you name; the second caches the message ids that
packaging needs. Commit `config/schema.json`, the generated `Schema.g.cs`, and
`config/sdkmessages.json`. Everything afterwards works offline.

## Everyday use

| Goal | Command |
|---|---|
| Check everything before pushing | `.\dv build` |
| Register into your dev environment | `.\dv sync -e dev` |
| ...just one assembly | `.\dv sync -e dev -a Contoso.Plugins` |
| Build the package | `.\dv pack --version 1.0.0.42` |
| Import into test | `.\dv import -e test --version 1.0.0.42` |
| Add a table to the constants | `.\dv schema pull -e dev -t lead` |
| See everything the tool can do | `.\dv --help` |

Short flags: `-e` env, `-t` tables, `-a` assembly, `-c` configuration, `-o` out, `-p` package,
`-m` managed, `-v` verbose, `-h` help. `--prune` deliberately has **no** short form, because it
deletes registrations. A mistyped option is rejected with a suggestion rather than ignored.

`build/*.ps1` still exist and are what CI calls, but you never need them locally.

## Declaring a step

One attribute per step, on the plugin class. A class may carry several:

```csharp
using Dataverse.Plugins.Abstractions.Schema;

[PluginStep(Messages.Create, Contact.EntityLogicalName,
    Stage = Stage.PostOperation,
    Order = 10,
    Name = "Contact create: set description",
    PostImage = new[] { Contact.Fields.FirstName, Contact.Fields.LastName })]

[PluginStep(Messages.Update, Contact.EntityLogicalName,
    Stage = Stage.PostOperation,
    Order = 20,
    Name = "Contact update: set description",
    FilteringAttributes = new[] { Contact.Fields.FirstName, Contact.Fields.LastName },
    PreImage            = new[] { Contact.Fields.FirstName, Contact.Fields.LastName })]
public class ContactSync : PluginBase { }
```

Every string above is a compile-checked constant, and each step carries its own image without a
separate attribute — so there is no step name to keep in sync between two places.

Three rules the build enforces:

- Two steps on one class for the same message and table **need explicit `Name`s**, otherwise they
  are indistinguishable.
- `[PluginImage]` (for a custom alias, `ImageType.Both`, or a non-`Target` property) binds with
  `StepName`. A `StepName` matching no step **fails the build** rather than silently dropping.
- A pre-image on `Create`, or a post-image on `Delete`, is rejected — neither exists.

See [docs/getting-started.md](docs/getting-started.md).

## Adding a plugin

Install the templates once:

```powershell
dotnet new install ./templates/dotnet
./templates/install-vs-templates.ps1   # optional, for the Visual Studio dialogs
```

```powershell
dotnet new dv-plugin-assembly -n Contoso.Plugins -o src/Contoso.Plugins
dotnet new dv-plugin -n AccountGuard --namespace Contoso.Plugins --message Delete --entity account --stage PreValidation
```

A new project needs **no configuration change anywhere**. It declares itself with
`<DataversePluginAssembly>true</DataversePluginAssembly>` (set by the shared props it imports) and
the tooling discovers it.

## Layout

```
config/            publisher and solution identity, environments, schema and message snapshots
src/
  Dataverse.Plugins.Abstractions/   attributes + PluginBase + generated Schema.g.cs,
                                    all linked into each plugin assembly as source
  Sample.Plugins/                   an example assembly
  Dataverse.Plugins.Tooling/        the dv CLI
templates/         dotnet new + Visual Studio templates, from one set of sources
dv, dv.cmd, dv.ps1 the CLI entry point - builds itself when stale, then forwards
build/             thin wrappers over dv, kept for CI pipelines
tests/             unit tests plus a golden test that packs and inspects a real zip
artifacts/         git-ignored. manifest, generated solution source, and the .zip
```

## What is and is not committed

No build output is committed. `artifacts/` holds the manifest, the generated packager source tree
and the package, all rebuilt from source on each run.

Three generated-then-committed files are deliberate, because CI has no environment to derive them
from:

| File | Why it is committed |
|---|---|
| `config/schema.json` | Table metadata, so the constants can be regenerated offline |
| `src/.../Schema/Schema.g.cs` | The constants themselves, so a fresh clone compiles |
| `config/sdkmessages.json` | A packed step references its message by GUID only |

## Docs

- [Getting started](docs/getting-started.md) — add a plugin, declare a step, deploy it
- [Architecture](docs/architecture.md) — the manifest contract, deterministic ids, and the
  packager format details, so nobody has to rediscover them
