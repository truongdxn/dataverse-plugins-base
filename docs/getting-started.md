# Getting started

The whole path, in the order you hit it: from an empty repo to steps firing in your dev sandbox.

Read it once end to end the first time. After that, section 8 is the loop you live in.

---

## 0. One-time setup

Install the [.NET 8 SDK](https://dotnet.microsoft.com/download), then the Power Platform CLI:

```bash
dotnet tool install --global Microsoft.PowerApps.CLI.Tool
```

`pac` is needed only by `dv pack`. Everything else works without it.

Check the tool builds:

```bash
.\dv --help
```

The first run compiles `dv` into `artifacts/tool`; later runs reuse it and rebuild only when its
sources change. Nothing is installed globally.

> **Windows only, for now.** Plugin assemblies and their tests target net462, and `pac` wants
> Windows. The tooling itself is net8.0 and platform-neutral.

Optionally register the templates with Visual Studio, so New Project offers them:

```bash
pwsh templates/install-vs-templates.ps1
```

Or use them from the command line, which is what the rest of this page does:

```bash
dotnet new install ./templates/dotnet/dv-solution
dotnet new install ./templates/dotnet/dv-plugin-assembly
dotnet new install ./templates/dotnet/dv-plugin-tests
dotnet new install ./templates/dotnet/dv-plugin
```

---

## 1. Create a solution

One PowerApps solution = one folder under `CRM/Plugins/` = one `.zip`.

```bash
dotnet new dv-solution -n CRMCore -o CRM/Plugins/CRMCore --publisherPrefix contoso
```

That gives you:

```
CRM/Plugins/CRMCore/
  solution.json        publisher and solution identity  ← edit this
  schema.json          empty; section 4 fills it
  sdkmessages.json     empty; section 5 fills it
  Generated/           where the typed constants land
```

Open `solution.json` and set the friendly names and descriptions.

> **`solution.uniqueName` is permanent.** It seeds every component id `dv` generates. Change it
> later and everything re-identifies itself, so the next sync *creates duplicates* instead of
> updating what is there. Choose it once.

Confirm `dv` sees it:

```bash
.\dv solutions
```

---

## 2. Point at your own sandbox

`config/environments.json` is shared and holds one `dev` entry. Do not edit it to point at your
own org — create `config/environments.local.json`, which is git-ignored and merged over it:

```json
{
  "environments": {
    "dev": { "url": "https://yourorg.crm5.dynamics.com/" }
  }
}
```

Sign-in is interactive and the token is cached outside the repo, so you sign in once rather than
once per command.

There are no `test` or `prod` entries and there is no place to put a secret. That is deliberate:
this repo builds a `.zip` and stops. Importing it is another module's job.

---

## 3. Add an assembly and a plugin

```bash
cd CRM/Plugins/CRMCore
dotnet new dv-plugin-assembly -n CRMCore.Plugins
cd CRMCore.Plugins
dotnet new dv-plugin -n AccountPreCreate --namespace CRMCore.Plugins --entity account --message Create --stage PreValidation
```

The project needs no entry in any config file. Importing `Abstractions.Sources.props` is what
makes `dv` discover it, and *where the project sits* is what binds it to CRMCore — the nearest
`solution.json` above it. Create one outside a solution folder and the build tells you so rather
than producing an assembly that silently belongs to nothing.

---

## 4. Generate the typed constants

This is what makes a mistyped column a build error instead of a step that deploys and never fires.

```bash
.\dv schema pull -s CRMCore -e dev -t account,contact
```

It writes `CRM/Plugins/CRMCore/schema.json` (the snapshot) and `Generated/Schema.g.cs` (the
constants). **Commit both**, so a fresh clone compiles and CI never needs a connection.

You now have:

```csharp
Account.EntityLogicalName        // "account"
Account.Fields.Name              // "name"
Messages.Update                  // "Update"
```

Pass `-t` the tables you actually use. A pull *merges*, so adding a table later leaves the others
alone, and regenerating offline is:

```bash
.\dv schema codegen -s CRMCore
```

---

## 5. Cache the message ids

A packed solution references SDK messages by GUID — the customizations schema has no place for a
message *name* — so the ids have to be known offline.

```bash
.\dv messages pull -s CRMCore -e dev
```

Commit `sdkmessages.json`. After this, `dv pack` needs no environment at all, which is what lets
CI build the package with no credentials. Skip it and `dv pack` fails with the missing messages
named, rather than guessing.

---

## 6. Declare the step

Write the registration on the class, using only generated constants:

```csharp
[PluginStep(
    Messages.Update,
    Contact.EntityLogicalName,
    Stage = Stage.PostOperation,
    Order = 20,
    Name = "Contact update: set description",
    FilteringAttributes = new[] { Contact.Fields.FirstName, Contact.Fields.LastName },
    PreImage = new[] { Contact.Fields.FirstName, Contact.Fields.LastName })]
public class ContactPostUpdate : PluginBase
{
    protected override void Execute(ILocalPluginContext context) { ... }
}
```

One class can carry several `[PluginStep]` attributes, each with its own images. Two steps for the
same message and table must have different `Name`s — otherwise they are indistinguishable, and the
build says so.

| | |
|---|---|
| `Stage` | `PreValidation` (10), `PreOperation` (20), `PostOperation` (40) |
| `Mode` | `Synchronous`, `Asynchronous` (post-operation only) |
| `FilteringAttributes` | Columns that trigger an Update step. Without them it fires on every change — including its own writes |
| `PreImage` / `PostImage` | Column arrays. Empty array = all columns. Omitted = no image |
| `Order` | Execution order within the stage |

`dv` rejects the combinations Dataverse cannot honour — a pre-image on Create, a post-image on
Delete, an async step outside post-operation — at build time rather than at registration time.

See [`CRM/Plugins/Sample/Sample.Plugins/ContactPostUpdate.cs`](../CRM/Plugins/Sample/Sample.Plugins/ContactPostUpdate.cs)
for the whole surface in one worked class.

---

## 7. Test it

```bash
cd CRM/Plugins/CRMCore
dotnet new dv-plugin-tests -n CRMCore.Plugins.Tests --testsFor CRMCore.Plugins
```

A test runs the real plugin against a fake pipeline — no connection, no environment, no
registration:

```csharp
var host = PluginTestHost
    .For(Messages.Update, Contact.EntityLogicalName, Stages.PostOperation)
    .WithTarget(target)
    .WithPreImage("PreImage", pre)
    .Execute(new ContactPostUpdate());

Assert.True(host.Tracing.Contains("..."));
```

```bash
.\dv test -a CRMCore.Plugins
```

Details and limits in **[testing.md](testing.md)**.

---

## 8. The everyday loop

```bash
.\dv build -a CRMCore.Plugins    # compile + validate every declaration
.\dv test  -a CRMCore.Plugins    # run its tests
.\dv sync  -e dev -a CRMCore.Plugins
```

`-a` keeps you to your own assembly, so nothing you run touches a colleague's work. `-s` is
usually unnecessary — run from inside `CRM/Plugins/CRMCore/` and it is inferred.

`dv sync` creates or updates the assembly, plugin types, steps and images. Because ids are derived
from the declaration rather than allocated by the server, syncing twice updates rather than
duplicates.

Its summary ends with anything it found in the environment that is *not* declared in source:

```
Not declared locally (left untouched)
  Some step registered by hand
```

Those are left alone. Usually it means someone registered a step in the Plugin Registration Tool,
and the fix is to declare it in code. `--prune` deletes them instead — spelled out in full, with
no short form, and never suggested by the tool, because it is the one command here that destroys
something.

Before pushing, run what CI runs:

```bash
.\build\build.ps1 -Configuration Release
```

---

## 9. Hand over the package

```bash
.\dv pack -s CRMCore --version 1.0.0.1
```

Writes `CRM/Solutions/CRMCore/CRMCore_1_0_0_1.zip`, built from the same declarations `dv sync`
uses. It needs no Dataverse connection.

**This is where the repo's responsibility ends.** The `.zip` goes to whatever module handles
imports. There is no `dv import`, no test or prod environment, and no credential in this repo to
reach one with.

Change where the `.zip` lands with `--out`, or permanently via `packageOutput` in `solution.json`,
or repo-wide via `packageOutputDirectory` in `dv.json`. `--out` wins, then `solution.json`, then
`dv.json`.

> **One thing the package cannot carry:** a *disabled* step. The solution format has no element
> for it, so a disabled step imports as enabled. `dv pack` warns when this applies. Use `dv sync`
> where the disabled state matters.

---

## Troubleshooting

**`.\dv` is not recognized**

1. Check the file is there: `dir dv*`. Three shims live at the repo root — `dv`, `dv.cmd`,
   `dv.ps1`.
2. Prefer `.\dv.cmd`. Bare `.\dv` relies on PowerShell resolving the `.ps1`, which also depends on
   the execution policy allowing local scripts. `.\dv.cmd` needs neither.
3. The fallback that always works, needing no shim at all:

   ```bash
   dotnet run --project CRM/Shared/PluginBase/src/Dataverse.Plugins.Tooling -- build -a CRMCore.Plugins
   ```

   Everything after `--` goes to the tool verbatim. It works from the repo root or any folder
   inside it.

**`No .NET SDKs were found`, with an SDK installed**

A runtime-only install under `Program Files` is shadowing it on `PATH`. Both `dv` and the build
scripts already look for an installation with a populated `sdk/` folder; if it still picks wrong,
set `DOTNET_ROOT` to the right one.

**`pac` not found**

Only `dv pack` needs it: `dotnet tool install --global Microsoft.PowerApps.CLI.Tool`.

**"looks like a plugin assembly but is not inside any solution folder"**

An IDE put the project outside `CRM/Plugins/<Solution>/`. It is not being deployed. Move it in.

**A step deploys but never fires**

Almost always `FilteringAttributes` on an Update step: the column you expected to trigger it is
not in the list. Check the registration in the sandbox against the attribute.

**Several solutions and no `-s`**

`dv` refuses to guess and lists them. Either pass `-s`, `cd` into the solution folder, or set
`defaultSolution` in `dv.json`.

## Working across machines

The tool and the shims are rebuilt from source, so a machine only needs the source tree — use git
rather than file sync. `bin/`, `obj/`, `artifacts/` and `CRM/Solutions/` are build output;
`.gitignore` excludes them from git but a file-sync tool will happily copy them, which is slow and
can produce conflicted copies mid-build.

After cloning, run `.\dv build` once: it rebuilds the tool and the plugin assemblies locally
rather than trusting anything that arrived prebuilt.
