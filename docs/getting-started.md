# Getting started

## One-time setup

Install the prerequisites listed in the [README](../README.md), then:

```powershell
dotnet new install ./templates/dotnet
./templates/install-vs-templates.ps1   # optional, for the Visual Studio dialogs; restart VS after
```

### The `dv` command

Everything goes through `dv` at the repo root. There is nothing to install and no build step to
remember: it compiles itself when its sources change, and builds your plugin assemblies before any
command that reads them.

```powershell
.\dv --help          # every command and option
.\dv build           # build the plugin assemblies and validate the declarations
```

Options have short forms — `-e` env, `-t` tables, `-a` assembly, `-c` configuration, `-o` out,
`-p` package, `-m` managed, `-v` verbose, `-h` help — so:

```powershell
.\dv sync -e dev -a Contoso.Plugins
```

Two deliberate gaps: `--prune` has no short form because it deletes registrations, and `--version`
has none because `-v` is verbose. A mistyped option is rejected with a suggestion rather than
silently ignored.

Use `--no-build` when a build has already run and a second is waste, and `DV_NO_BUILD=1` to stop the
shim rebuilding itself in a tight loop.

`build/*.ps1` remain as thin wrappers for CI pipelines; you do not need them locally.

### Point at your environments

Edit `config/environments.json`. It holds URLs only — secrets are named, not stored:

```json
{
  "environments": {
    "dev":  { "url": "https://contoso-dev.crm.dynamics.com", "auth": "interactive" },
    "test": { "url": "https://contoso-test.crm.dynamics.com", "auth": "clientsecret",
              "clientIdVar": "DV_TEST_CLIENT_ID", "clientSecretVar": "DV_TEST_CLIENT_SECRET" }
  }
}
```

To point `dev` at your own sandbox without touching the shared file, create
`config/environments.local.json` with the same shape. It is git-ignored and merged on top.

### Rename the publisher and solution

Edit `config/solution.json` — the only place those names appear.

### Generate the schema constants

```powershell
.\dv schema pull -e dev -t contact,account
```

This reads metadata for the tables you name into `config/schema.json` and regenerates
`src/Dataverse.Plugins.Abstractions/Schema/Schema.g.cs`, which is linked into every plugin assembly.
Commit both.

`-Tables` is required and pulls merge per table, so adding a table later leaves the rest alone:

```powershell
.\dv schema pull -e dev -t lead
```

There is no "pull the whole org" mode — it would generate an enormous file that is mostly noise.

To regenerate the `.cs` from the committed snapshot without an environment (after a merge, say):

```powershell
.\dv schema codegen
```

### Cache the message ids

```powershell
.\dv messages pull -e dev
```

Commit `config/sdkmessages.json`. A packed step references its SDK message by GUID only, so doing
this once means CI can build packages with no environment at all.

## Adding a plugin assembly

```powershell
dotnet new dv-plugin-assembly -n Contoso.Plugins -o src/Contoso.Plugins
dotnet sln DataverseBase.sln add src/Contoso.Plugins/Contoso.Plugins.csproj
```

Or in Visual Studio: **File → New → Project → "Dataverse plugin assembly"**. Create it under `src/`
so the relative import of `Abstractions.Sources.props` resolves.

Nothing else — no central registry to update.

## Adding a plugin

```powershell
cd src/Contoso.Plugins
dotnet new dv-plugin -n AccountDeleteGuard --namespace Contoso.Plugins `
  --message Delete --entity account --stage PreValidation
```

Or in Visual Studio: **Add → New Item → "Dataverse plugin"**.

```csharp
protected override void Execute(ILocalPluginContext context)
{
    var target = context.GetTarget();               // Entity on Create/Update
    var reference = context.GetTargetReference();   // EntityReference on Delete

    if (SomethingIsWrong())
    {
        // The one exception type whose message reaches the user.
        throw new InvalidPluginExecutionException("Explain what they should do instead.");
    }
}
```

`PluginBase` already handles building the context, guarding recursion depth, and turning unexpected
exceptions into a reported failure with the stack trace in the trace log.

## Declaring steps

Use the generated constants rather than literals — that is what turns a typo into a build error:

```csharp
using Dataverse.Plugins.Abstractions.Schema;

[PluginStep(Messages.Update, Contact.EntityLogicalName,
    Stage = Stage.PostOperation,
    Order = 10,
    FilteringAttributes = new[] { Contact.Fields.FirstName, Contact.Fields.LastName })]
public class ContactPostUpdate : PluginBase { }
```

### Fields

| Field | Notes |
|---|---|
| message | First argument. `Messages.Update`, or a custom API / action name |
| primaryEntity | Second argument. `Contact.EntityLogicalName`. Omit for a global registration |
| `Stage` | `PreValidation`, `PreOperation`, `PostOperation` |
| `Mode` | `Synchronous`, `Asynchronous`. Async must be `PostOperation` |
| `Order` | Rank within the stage |
| `FilteringAttributes` | `string[]`. `Update` only — set it, or the step fires on every column |
| `Name` | Required when a class declares two steps for the same message and table |
| `PreImage` / `PostImage` | `string[]` of columns. Registers the image on this step directly |
| `State` | `Enabled`, `Disabled`. Only `sync` can apply this — see below |
| `UnsecureConfiguration` | Readable by any user. Never secrets |
| `SecureConfigurationKey` | Name of the secret, resolved at deploy time |

## Several steps, each with its own image

Put the images on the steps. There is then no name to keep in sync:

```csharp
[PluginStep(Messages.Create, Contact.EntityLogicalName,
    Order = 10, Name = "Contact create: set description",
    PostImage = new[] { Contact.Fields.FirstName, Contact.Fields.LastName })]

[PluginStep(Messages.Update, Contact.EntityLogicalName,
    Order = 20, Name = "Contact update: set description",
    FilteringAttributes = new[] { Contact.Fields.FirstName, Contact.Fields.LastName },
    PreImage            = new[] { Contact.Fields.FirstName, Contact.Fields.LastName })]
public class ContactSync : PluginBase { }
```

Both images may be called `PreImage` / `PostImage` on different steps: the image id derives from the
step plus the name, so they never collide, and both read as `context.GetPreImage()` in code.

`[PluginImage]` remains for what the shorthand cannot express — a custom name or alias,
`ImageType.Both`, or a message property other than `Target`. It binds with `StepName`:

```csharp
[PluginImage(ImageType.Both, "Snapshot", StepName = "Contact update: set description",
    Attributes = new[] { Contact.Fields.FirstName })]
```

Four rules govern that binding:

1. `StepName` must match the step's `Name` exactly (case-insensitive).
2. Give every step an explicit `Name` when using per-step images, rather than relying on the
   generated `"Namespace.Type: Update of contact"` default.
3. An image with no `StepName` attaches to **every** step on the class.
4. A `StepName` matching no step **fails the build**, naming the steps that do exist.

## Deploying

### To your dev environment

```powershell
.\dv sync -e dev
.\dv sync -e dev -a Contoso.Plugins   # just one assembly
```

Registers directly: uploads assemblies, registers types, creates or updates steps and images.
Idempotent — running it twice changes nothing the second time.

Steps in the environment that are not declared in source are **reported and left alone**. To remove
them deliberately, add `--prune` — spelled out in full on purpose, since it deletes.

### To test and production

```powershell
.\dv pack --version 1.0.0.42
.\dv import -e test --version 1.0.0.42
```

`pack` needs no connection, which is why CI can build the package — see
[.github/workflows/deploy.yml](../.github/workflows/deploy.yml).

> **One limitation worth knowing.** The solution format has no field for a step's enabled/disabled
> state, so a step marked `State = StepState.Disabled` imports **enabled**. `pack` warns when this
> applies. Use `sync` if the disabled state has to hold.

`dv pack -a X` produces a package containing only X. Solution import is additive, so it will not
remove assemblies already deployed — but it is a partial package, and the normal path is packing
everything.

## Working across machines

A machine only needs the **source tree**. The `dv` shim rebuilds the tool, and `dv` rebuilds the
plugin assemblies, so nothing compiled has to travel with it. After copying the repo somewhere new,
run `.\dv build` once rather than trusting any binaries that came along for the ride.

Two things to know if the repo moves between machines by file sync rather than version control:

- **Newly added files arrive late.** A file created minutes ago may simply not be on the other
  machine yet. That is the usual cause of `.\dv` not being found.
- **`.gitignore` does not stop a sync tool.** `bin/`, `obj/`, `artifacts/` and `.vs/` are build
  output; git ignores them, OneDrive and friends do not. Syncing them is slow and can produce
  conflicted copies while a build is running. Exclude those four folders from sync.

## Troubleshooting

**"`.\dv` is not recognized as the name of a cmdlet, function, script file, or operable program."**

That error means the file was not found — an execution policy block looks different. In order of
likelihood:

1. **The file is not there.** Check with `dir dv*`. If the repo travels by file sync, confirm the
   sync finished; the shims are recent additions.
2. **Use `.\dv.cmd` instead.** Bare `.\dv` depends on PowerShell resolving the `.ps1`, and on the
   execution policy permitting local scripts. `.\dv.cmd` needs neither, so it is the portable form.
3. **Skip the shim entirely.** This always works from a bare source tree, with nothing installed:

   ```powershell
   dotnet run --project src/Dataverse.Plugins.Tooling -- build
   dotnet run --project src/Dataverse.Plugins.Tooling -- sync -e dev
   ```

   Everything after `--` reaches the tool verbatim. It also works from any folder inside the repo,
   because the tool walks up to find `config/solution.json`.

**"No .NET SDKs were found" even though an SDK is installed.** More than one dotnet installation
exists and the one first on PATH is runtime-only — commonly `C:\Program Files\dotnet` shadowing a
user-local SDK. Both `dv` and the build scripts work around this by searching for an installation
whose `sdk` folder is populated, so it should not surface; if it does, set `DOTNET_ROOT` to the
installation that has the SDK. `dv --verbose` prints which dotnet it chose.

**"No plugin assembly projects found."** The project must import `Abstractions.Sources.props` and
must have been built for the configuration you asked for. Run a build first.

**"No plugin assembly called 'X'. Found: ..."** `-Assembly` takes the assembly name, not the project
path. The message lists the valid names.

**A constant is missing after `schema pull`.** The pull replaced that table with what the
environment actually has. If a column was removed or renamed, the compile error is telling you the
truth about the target org.

**"No sdkmessageid cached for message 'X'."** Run the messages task and commit the file. If `X` is a
custom API, it must be deployed to that environment before its message exists.

**"Table 'x' does not support this message."** No `sdkmessagefilter` for that combination — usually
a wrong table for the message.

**A step is not firing.** Check `FilteringAttributes` — a step filtered to columns the caller did
not send will not run. `dv manifest` writes `artifacts/manifest.json`, showing exactly what would be
registered.
