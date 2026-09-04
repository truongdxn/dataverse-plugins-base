# Getting started

The whole path, in the order you hit it: from an empty repo to steps firing in your dev sandbox.

Every creation step is given **both ways** — command line and Visual Studio. They produce the same
thing; pick whichever you are already in.

Read it once end to end the first time. After that, section 8 is the loop you live in.

> ### "Solution" means two different things
>
> A **Visual Studio solution** is a `.sln` file grouping projects for the IDE.
> A **PowerApps solution** is the unit that gets packaged and imported into Dataverse.
>
> This repo is about the second. `dv solutions` lists PowerApps solutions — folders under
> `CRM/Plugins/` — and a `.sln` has nothing to do with it. Creating a solution in Visual Studio's
> New Project dialog does **not** create a PowerApps solution.

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

**To use the Visual Studio path**, register the templates once:

```bash
pwsh templates/install-vs-templates.ps1
```

Restart Visual Studio afterwards. You do not need this for the command-line path — `dv new`
installs what it needs on first use.

There is also an extension that runs the dv commands themselves from Solution Explorer — build,
test and sync one assembly by right-clicking it. It has to be built once from Visual Studio:
**[vs-extension.md](vs-extension.md)**. Everything below works with or without it.

---

## 1. Create a solution

One PowerApps solution = one folder under `CRM/Plugins/` = one `.zip`.

**Command line**

```bash
.\dv new solution CRMCore --prefix contoso
```

**Visual Studio** — there is no pure-GUI form, and it is worth knowing why: a solution folder is
config, not a project, and the New Project dialog only makes projects. Producing one would need a
VSIX. So:

1. File → New → Project → **Dataverse plugin assembly**
2. Set the location to `CRM\Plugins\CRMCore\` and the name to `CRMCore.Plugins`
3. Run once, from anywhere in the repo:

   ```bash
   .\dv new solution CRMCore --prefix contoso
   ```

   Run it from inside the folder and you can drop the name — `dv` infers it.

Either way you end up with:

```
CRM/Plugins/CRMCore/
  solution.json        publisher and solution identity  ← the only file a solution needs
```

If you skip step 3, `dv solutions` tells you:

```
Not configured yet
  CRMCore  ->  run: dv new solution CRMCore
```

and `dv build` refuses with the same instruction. Nothing fails silently.

> **`solution.uniqueName` is permanent.** It seeds every component id `dv` generates. Change it
> later and everything re-identifies itself, so the next sync *creates duplicates* instead of
> updating. `dv new solution` refuses to overwrite an existing `solution.json` for exactly this
> reason — to change names or the version, edit the file.

Confirm:

```bash
.\dv solutions
```

---

## 2. Point at your own sandbox

`config/environments.json` is shared and holds one `dev` entry. Do not edit it to point at your own
org — create `config/environments.local.json`, which is git-ignored and merged over it:

```json
{
  "environments": {
    "dev": { "url": "https://yourorg.crm5.dynamics.com/" }
  }
}
```

Sign-in is interactive and the token is cached outside the repo, so you sign in once rather than
once per command.

There are no `test` or `prod` entries and nowhere to put a secret. That is deliberate: this repo
builds a `.zip` and stops. Importing it is another module's job.

---

## 3. Add an assembly and a plugin

**Command line**

```bash
cd CRM/Plugins/CRMCore
..\..\..\dv new assembly CRMCore.Plugins
cd CRMCore.Plugins
..\..\..\..\dv new plugin AccountPreCreate --entity account --message Create --stage PreValidation
```

**Visual Studio**

| | |
|---|---|
| Assembly | File → New → Project → **Dataverse plugin assembly**, located in `CRM\Plugins\CRMCore\` |
| Plugin class | Right-click the project → Add → New Item → **Dataverse plugin** |

The project needs no entry in any config file. Importing `Abstractions.Sources.props` is what makes
`dv` discover it, and *where the project sits* is what binds it to CRMCore — the nearest
`solution.json` above it. Create one outside a solution folder and the build says so rather than
producing an assembly that belongs to nothing.

---

## 4. Generate the typed constants

This is what makes a mistyped column a build error instead of a step that deploys and never fires.

```bash
.\dv schema pull -e dev -t account,contact
```

**One snapshot for the whole repo** — no `-s`. It describes the *org*, and every solution here
targets the same org, so a copy per solution would only be the same file several times over. It
writes `config/schema.json` (the snapshot) and `config/Generated/Schema.g.cs` (the constants).
**Commit both**, so a fresh clone compiles and CI never needs a connection.

Both sit in `config/` rather than beside the base, because the base can arrive as a NuGet package —
and generated output cannot live inside a package cache.

You now have, in every solution:

```csharp
Account.EntityLogicalName        // "account"
Account.Fields.Name              // "name"
Messages.Update                  // "Update"
```

Pass `-t` the tables you actually use. A pull *merges*, so adding a table later leaves the others
alone, and regenerating offline is `.\dv schema codegen`.

> **Why `--tables` is required.** The generated file is linked into *every* plugin assembly. The
> constants cost nothing at runtime — `const` inlines at the call site, so the classes are never
> even loaded — but they stay in each assembly's metadata at roughly 64 bytes each, and ride into
> every `.zip` base64-encoded. Three tables is ~38 KB; a whole org would be megabytes, repeated per
> assembly. Pull what you use. `dv` warns past 5,000 constants.

---

## 5. Cache the message ids

A packed solution references SDK messages by GUID — the customizations schema has no place for a
message *name* — so the ids must be known offline.

```bash
.\dv messages pull -e dev
```

Repo-wide too, and for the same reason. Commit `config/sdkmessages.json`. After this, `dv pack`
needs no environment at all, which is what lets CI build the package with no credentials. Skip it
and `dv pack` fails with the missing messages named, rather than guessing.

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

See [`ContactPostUpdate.cs`](../CRM/Plugins/Sample/Sample.Plugins/ContactPostUpdate.cs) for the
whole surface in one worked class.

---

## 7. Test it

**Command line**

```bash
cd CRM/Plugins/CRMCore
..\..\..\dv new tests CRMCore.Plugins.Tests --for CRMCore.Plugins
```

**Visual Studio** — File → New → Project → **Dataverse plugin tests**, located in
`CRM\Plugins\CRMCore\`. Name it `CRMCore.Plugins.Tests`; if the name does not match the assembly,
fix `<DataverseTestsFor>` in the `.csproj`.

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

`-a` keeps you to your own assembly, so nothing you run touches a colleague's work. `-s` is usually
unnecessary — run from inside `CRM/Plugins/CRMCore/` and it is inferred.

`dv sync` creates or updates the assembly, plugin types, steps and images. Because ids are derived
from the declaration rather than allocated by the server, syncing twice updates rather than
duplicates.

Its summary ends with anything found in the environment that is *not* declared in source:

```
Not declared locally (left untouched)
  Some step registered by hand
```

Those are left alone. Usually it means someone registered a step in the Plugin Registration Tool,
and the fix is to declare it in code. `--prune` deletes them instead — spelled out in full, with no
short form, and never suggested by the tool, because it is the one command here that destroys
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

Writes `CRM/Solutions/CRMCore/CRMCore_1_0_0_1.zip`, built from the same declarations `dv sync` uses.
It needs no Dataverse connection.

**This is where the repo's responsibility ends.** The `.zip` goes to whatever module handles
imports. There is no `dv import`, no test or prod environment, and no credential in this repo to
reach one with.

Change where the `.zip` lands with `--out`, or permanently via `packageOutput` in `solution.json`,
or repo-wide via `packageOutputDirectory` in `dv.json`. `--out` wins, then `solution.json`, then
`dv.json`.

> **One thing the package cannot carry:** a *disabled* step. The solution format has no element for
> it, so a disabled step imports as enabled. `dv pack` warns when this applies. Use `dv sync` where
> the disabled state matters.

---

## Where everything lives

| | |
|---|---|
| `config/environments.json` | Dev sandboxes |
| `config/schema.json` | Table metadata — **repo-wide** |
| `config/sdkmessages.json` | Message ids — **repo-wide** |
| `config/Generated/Schema.g.cs` | Typed constants, generated and committed |
| `CRM/Plugins/<Solution>/solution.json` | Publisher and solution identity — **per solution** |
| `CRM/Plugins/<Solution>/<Assembly>/` | The code |
| `CRM/Solutions/<Solution>/` | The packed `.zip` — output, git-ignored |

The split is: **anything describing the org is repo-wide; only identity and code are per solution.**

---

## Troubleshooting

**`dv solutions` does not show the solution I just created**

You made a Visual Studio solution or just a project. A PowerApps solution needs `solution.json`,
which the IDE cannot create. `dv solutions` lists the folder under "Not configured yet" with the
command to fix it: `dv new solution <Name>`.

**`.\dv` is not recognized**

1. Check the file is there: `dir dv*`. Three shims live at the repo root — `dv`, `dv.cmd`, `dv.ps1`.
2. Prefer `.\dv.cmd`. Bare `.\dv` relies on PowerShell resolving the `.ps1`, which also depends on
   the execution policy allowing local scripts. `.\dv.cmd` needs neither.
3. The fallback that always works, needing no shim at all:

   ```bash
   dotnet run --project CRM/Shared/PluginBase/src/Dataverse.Plugins.Tooling -- solutions
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

**`dotnet new` says the template match was ambiguous**

The templates are installed twice — once as the folder, once per subfolder. Uninstall the
subfolders and keep `templates/dotnet`:

```bash
dotnet new uninstall
```

lists what is installed and how to remove each one.

**A step deploys but never fires**

Almost always `FilteringAttributes` on an Update step: the column you expected to trigger it is not
in the list. Check the registration in the sandbox against the attribute.

**Several solutions and no `-s`**

`dv` refuses to guess and lists them. Either pass `-s`, `cd` into the solution folder, or set
`defaultSolution` in `dv.json`.

## Working without the base in your repo

Everything above assumes you are in this repo. A team that only writes plugins does not need the
base source at all — it installs `dv` as a dotnet tool and references the abstractions as a
package, and the walkthrough is otherwise identical (`dotnet dv build` instead of `.\dv build`).
See **[packaging.md](packaging.md)**.

## Working across machines

The tool and the shims are rebuilt from source, so a machine only needs the source tree — use git
rather than file sync. `bin/`, `obj/`, `artifacts/` and `CRM/Solutions/` are build output;
`.gitignore` excludes them from git but a file-sync tool will happily copy them, which is slow and
can produce conflicted copies mid-build.

After cloning, run `.\dv build` once: it rebuilds the tool and the plugin assemblies locally rather
than trusting anything that arrived prebuilt.
