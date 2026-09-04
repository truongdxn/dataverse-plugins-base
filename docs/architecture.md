# Architecture

## Scope

This repo covers the **development phase** and nothing else. It declares steps, builds them, tests
them, registers them into a developer sandbox, and produces a `.zip`.

**Importing that `.zip` into any environment is out of scope** and belongs to another module. The
consequences are load-bearing rather than cosmetic: there is no `import` command, no test or
production environment, no client-secret authentication, and no place in this repo for a
credential. Sign-in is interactive only, because everything that connects is something a developer
runs at a keyboard.

## Many solutions, one base

```
CRM/Shared/PluginBase/          the base — attributes, harness, tooling
CRM/Plugins/<Solution>/         one folder per PowerApps solution, with its own config
CRM/Solutions/<Solution>/       one .zip per solution
```

A project belongs to the solution whose folder it sits in: the nearest `solution.json` above it.
MSBuild resolves that with `GetPathOfFileAbove` and `dv` walks the same directories, so the two
cannot disagree — and moving a project between solutions is a move, with no file to edit
afterwards.

A solution folder holds only `solution.json` and code. Everything describing the **org** -
`config/schema.json`, `config/sdkmessages.json`, the generated `Schema.g.cs` - is repo-wide,
because every solution here targets the same org and a copy per solution would only be the same
file several times over. `config/environments.json` is repo-wide for a related reason: a sandbox
belongs to a *developer*, not to a product.

### What the shared snapshot costs

The generated constants are linked into **every** plugin assembly, so it is worth knowing what that
weighs. Measured on `Sample.Plugins.dll`: 45.5 KB total, of which ~38 KB is 602 schema constants -
about 64 bytes each, being a metadata row, the identifier, and the UTF-16 value.

- **Runtime: nothing.** `const string` inlines at the call site (`ldstr "name"`), so the generated
  classes are never referenced, the CLR never loads them, and `const` emits no static constructor.
- **Size: linear.** ~470 KB at 50 tables, ~1.9 MB at 200 - per assembly, and base64-encoded into
  each `.zip`. Dataverse's 16 MB per-assembly limit is far off; build time and package size are the
  real costs.

What bounds it is that `--tables` is required and there is no org-wide pull, so the snapshot is the
union of tables actually used rather than the org's table list. `dv schema codegen` warns past
5,000 constants so that growth cannot creep up unnoticed.

### Solutions Visual Studio cannot create

A solution folder is config, not a project, and the New Project dialog only produces projects -
creating one would need a VSIX. So `dv new solution` writes `solution.json` directly, and creating
and *adopting* are the same command, because the common case is a folder the IDE already made.

It refuses to overwrite an existing `solution.json`. That file carries `uniqueName`, which every
component id derives from; rewriting it would silently re-identify everything and turn the next
sync into a duplicate-everything run. With no template able to write that file, refusing is the
whole safety story.

`SolutionSet` tracks **candidates** - folders holding projects but no `solution.json` - so the
half-finished state is reported by `dv solutions`, by `dv build`, and by the MSBuild error in
`Abstractions.Sources.props`, all naming the same fix. Before that, such a folder was not reported
as broken; it was invisible.

Which solution a command acts on is resolved highest-precedence-first — explicit `-s`, then the
working directory, then `defaultSolution` in `dv.json`, then the only solution if there is one.
Ambiguity fails and lists the names rather than picking one and being wrong.

## The manifest is the contract

```
CRM/Plugins/<Solution>/<Assembly>/  [PluginStep] ─► manifest ─┬─► dv pack ─► .zip ─► another module imports it
                                                              └─► dv sync ─► dev sandbox
```

Steps are declared once, in attributes on the plugin class, and **everything downstream reads only
the manifest**. That is what stops the direct-registration path and the packaged path from
disagreeing about what a step is: one definition, consumed twice.

`dv manifest` writes it to `artifacts/<Solution>/manifest.json` — useful for seeing exactly what
would be registered before anything is.

## Typed schema constants

Table, column and message names are the three magic strings in a registration, and they fail very
differently. A wrong message or table fails at deploy, on a lookup. **A wrong column fails
silently**: the step deploys cleanly and simply never fires, or the image arrives without the data.

So the names are generated constants rather than literals:

```csharp
[PluginStep(Messages.Update, Contact.EntityLogicalName,
    FilteringAttributes = new[] { Contact.Fields.FirstName })]
```

`dv schema pull -e dev -t contact,account` reads metadata into the committed `config/schema.json`;
`dv schema codegen` turns it into `config/Generated/Schema.g.cs`, which
`Abstractions.Sources.props` links into every plugin assembly in every solution. Neither takes a
`--solution`: the snapshot describes the org.

Both live in `config/`, on the consumer's side of the line, because the abstractions can arrive as
a NuGet package - and a package's own folder is a read-only cache that generated output cannot be
written into. `Abstractions.Sources.props` finds the folder by walking up for `dv.json`, the same
trick it uses for `solution.json`, so the rule holds whether the base is source or a package.

Three details worth knowing:

- **Identifiers come from metadata `SchemaName`, never `LogicalName`.** Logical names are lower case
  with no word boundaries (`firstname`), so deriving an identifier from one gives `Firstname`. The
  schema name already carries the casing.
- **`--tables` is required and pulls merge per table.** There is no org-wide mode: it would generate
  an enormous file that is mostly noise, and refreshing one table must not disturb the others.

The generated file is committed so a fresh clone compiles without anyone connecting to Dataverse.

## Deterministic ids

Component GUIDs are derived, not random: RFC 4122 version 5 (SHA-1) over a fixed namespace plus the
solution name and the component's logical key — see
[`DeterministicGuid`](../CRM/Shared/PluginBase/src/Dataverse.Plugins.Tooling/Infrastructure/DeterministicGuid.cs).

Seeding with `solution.uniqueName` is also what keeps solutions apart: two solutions in this repo
cannot produce a colliding component id, however similarly their assemblies are named. The flip
side is that renaming `solution.uniqueName` re-identifies everything, so the next sync duplicates
rather than updates. It is chosen once.

This is what makes the two paths compose. A step created by `dv sync` in dev and the same step
arriving via solution import elsewhere carry the *same* `sdkmessageprocessingstepid`, so:

- re-running either is idempotent,
- a package imported over a synced environment updates rather than duplicates,
- the package is byte-stable between builds when nothing changed.

The namespace constant must never change. `DeterministicGuidTests` pins a known value, because a
change there would silently re-identify every component and turn the next deploy into a duplicate.

SHA-1 is used because RFC 4122 specifies it for version 5 — these are identifiers, not secrets.

## Steps and images

A class may declare several `[PluginStep]`. Two for the same message and table without explicit
`Name`s are rejected: they would be indistinguishable and would collapse onto one derived id.

Images attach in two ways:

- **`PreImage` / `PostImage` on the step attribute.** The image belongs to that step by
  construction, so there is no name to keep in sync and nothing that can fail to bind. This is the
  common case.
- **A separate `[PluginImage]`**, for a custom name or alias, `ImageType.Both`, or a non-`Target`
  message property. It binds by `StepName`, and an unbound `StepName` **fails the build** —
  `ManifestBuilder` records the mismatch on the `ManifestType` and the validator reports it, rather
  than the validator re-deriving the binding where the two could disagree.

Image ids derive from the step id plus the image name, so the same image name on two steps produces
two distinct components.

## Assembly discovery

`dv` globs `CRM/Plugins/<Solution>/**/*.csproj` and asks **MSBuild** for each candidate's evaluated
properties (`dotnet msbuild -getProperty:`). A project is a plugin assembly when
`DataversePluginAssembly` is `true`, which `Abstractions.Sources.props` sets on import; it is a
test project when `DataversePluginTests` is `true`, which `Testing.Sources.props` sets. One
mechanism, two kinds of project.

Asking MSBuild rather than reading the XML matters: the property is usually set by an import, not
written in the project file, and MSBuild's `TargetPath` removes any need to guess where the build
put the DLL.

The upshot is that adding a project is New Project → build → deploy, with no central list to
remember to update. A project that imports the props but sits outside every solution folder is
named in a warning rather than silently skipped, because putting it there is exactly what an IDE's
New Project dialog does by default.

## Shared code is linked, not referenced

The Dataverse sandbox loads exactly one assembly per registered plugin type and resolves no
dependencies. A plugin referencing a separate `Abstractions.dll` fails at runtime with an
assembly-load error.

So `Abstractions.Sources.props` compiles the attributes, `PluginBase` and the solution's generated
`Schema.g.cs` **into** each plugin assembly as linked source. Each assembly therefore has its own
copy of the attribute types, which is fine: the scanner matches attributes by full type name, not
by assembly identity.

`Dataverse.Plugins.Abstractions.csproj` still exists so that shared code is compiled and analysed
in one place.

**This is what dictates the test harness design.** Because every plugin assembly holds its own copy
of `ILocalPluginContext`, those copies are different CLR types. A shared harness referencing its
own copy could not hand it to somebody else's plugin. So `Dataverse.Plugins.Testing` references
`Microsoft.Xrm.Sdk` and nothing else: it is an `IServiceProvider` and calls
`IPlugin.Execute(IServiceProvider)` — the same seam the platform uses, and the one place types
are genuinely shared. See [testing.md](testing.md).

## Reading net462 assemblies from a net8.0 tool

The tool runs on `net8.0`; plugin assemblies target `net462`. `AssemblyScanner` opens them with
`MetadataLoadContext` — metadata only, never executed — and reads `CustomAttributeData` rather than
instantiating attributes, which is what makes reading across the framework boundary work.

The resolver is given the plugin's own folder first and the tool's runtime folder second, so the
plugin's `net462` copies of shared libraries win, while the runtime folder supplies `mscorlib`.

## Packaging

`dv pack` generates a SolutionPackager source tree and shells out to `pac solution pack`. The
packager owns the details that are invisible until an import fails — `[Content_Types].xml`, where
the assembly bytes physically live, how sharded components fold back into `customizations.xml` — so
a mistake in our generated source surfaces at build time instead of in production.

That source tree is written to the **system temp folder**, not to `artifacts/`. It is regenerated
wholesale every run and nobody keeps it, and putting hundreds of transient files inside the repo
turned out to be actively harmful: this repo commonly lives in a OneDrive-synced folder, and
OneDrive takes ownership of directories it syncs — marking them ReadOnly, converting them to
reparse points, and adding a `Deny Everyone: DeleteSubdirectoriesAndFiles` ACE. The next pack then
cannot clear its own scratch tree, permanently, and retrying never helps. The path is keyed by repo
location so two clones do not collide, and `dv pack -v` prints it when something needs inspecting.

`artifacts/<Solution>/manifest.json` stays in the repo: one file, written not deleted, and worth
having to hand.

### Format details, established by experiment

These were determined by driving `pac solution pack` (v2.8.1) and inspecting the resulting zip, not
inferred from documentation. They are recorded here so nobody has to rediscover them.

**Packed zip layout**

```
customizations.xml
solution.xml
[Content_Types].xml
PluginAssemblies/<AssemblyName>-<PluginAssemblyId>/<AssemblyName>.dll
```

**Source layout we generate**

```
Other/Solution.xml
Other/Customizations.xml
Other/Relationships.xml
PluginAssemblies/<Name>-<guid>/<Name>.dll
PluginAssemblies/<Name>-<guid>/<Name>.dll.data.xml
SdkMessageProcessingSteps/<step name>.xml
```

**Steps are a sharded component.** `<SdkMessageProcessingSteps />` must be *childless* in
`Customizations.xml`, with one file per step. This is the trap: inline the steps instead and the
packager prints

> Component: SdkMessageProcessingSteps is a supported component type but has unexpected children in
> Customizations.xml; this component's specific processing will be skipped.

then drops every step and **still exits zero**. The result is a package that imports cleanly and
registers nothing. `SolutionPacker` therefore treats that line, and "root components are not
defined in customizations", as failures regardless of exit code, and
`SolutionPackagingTests` packs a real zip and asserts its contents.

**Other details**

- `Solution.xml` needs a `<RootComponent type="91">` per assembly and `type="92"` per step. Images
  are owned by their step and must not be listed.
- Step file names may not contain `:`, which the conventional step name always has. Also, the
  packager mis-parses a `name-guid.xml` file name, so a colliding name falls back to the bare id
  rather than appending one.
- Element order inside each file follows the published schema sequence. The packager is lenient
  about order; the platform validates on import.

### Message ids

A packed step references its SDK message **by GUID only** — the customizations schema has no
message-name element. Those ids are seeded per organization and stable, which is exactly why a
solution containing plugin steps is portable between environments at all.

`config/sdkmessages.json` caches the map, repo-wide like the schema. It is generated once by
`dv messages pull` and committed, because CI has no environment to resolve ids against. `dv pack` fails with the missing names listed
rather than guessing.

### Known limitation

The step schema has no state field, so a `Disabled` step cannot be expressed in a package and will
import **enabled**. `dv pack` warns when the manifest contains one. `dv sync` sets state correctly.

## Reconciliation

`dv sync` creates and updates. Registrations present in the environment but absent from the
manifest are **reported, never deleted**; `--prune` opts into removing them.

`--assembly` restricts a run to one plugin assembly. Filtering happens after discovery, so orphan
reporting stays scoped to the plugin types actually being deployed and a filtered sync never
mistakes another assembly's steps for orphans.

Existing records are adopted rather than duplicated: `StepRegistrar` matches on the deterministic id
first, then falls back to name within the same plugin type, so a step originally registered by hand
or by the Plugin Registration Tool is taken over and updated. Assemblies are matched by name and
keep whatever id the environment already has, since assembly names are unique and insisting on our
id would just collide.

## What is verified, and what is not

Verified locally, offline: the build, attribute scanning, validation, schema code generation, and
the whole packaging path including a real `pac solution pack` whose output is inspected by tests.

Verified on the real tree: a packed `Sample` zip containing every step, image, assembly and plugin
type with all four root components present; and two solutions packed side by side producing zero
overlapping component ids.

`dv schema pull` has been run successfully against a live org, so the metadata query path works.
`config/schema.json` and its generated constants came from that run.

**Not verified here**, because each needs a live environment and an interactive sign-in:

- `dv sync`, and steps actually firing. Treat the first sync into a scratch sandbox as the
  remaining checkpoint.
- `dv messages pull`. `config/sdkmessages.json` is still empty, so `dv pack` fails with the
  missing messages named until somebody runs it. The packaging path itself was proven with a
  temporary cache and by the golden test.
- Importing a package. Out of scope for this repo entirely — another module owns it.
