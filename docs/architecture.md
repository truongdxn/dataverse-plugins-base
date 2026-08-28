# Architecture

## The manifest is the contract

```
src/<Assembly>/  [PluginStep] attributes ─► dv manifest ─► manifest ─┬─► dv pack ─► .zip ─► dv import
                                                                     └─► dv sync
```

Steps are declared once, in attributes on the plugin class, and **everything downstream reads only
the manifest**. That is what stops the direct-registration path and the packaged-import path from
disagreeing about what a step is: one definition, consumed twice.

`dv manifest` writes it to `artifacts/manifest.json` — useful for seeing exactly what would be
registered before anything is.

## Typed schema constants

Table, column and message names are the three magic strings in a registration, and they fail very
differently. A wrong message or table fails at deploy, on a lookup. **A wrong column fails
silently**: the step deploys cleanly and simply never fires, or the image arrives without the data.

So the names are generated constants rather than literals:

```csharp
[PluginStep(Messages.Update, Contact.EntityLogicalName,
    FilteringAttributes = new[] { Contact.Fields.FirstName })]
```

`dv schema pull --env dev --tables contact,account` reads metadata into the committed
`config/schema.json`; `dv schema codegen` turns that into
`src/Dataverse.Plugins.Abstractions/Schema/Schema.g.cs`, which `Abstractions.Sources.props` links
into every plugin assembly. The values are `const`, so they inline and cost the assemblies nothing.

Three details worth knowing:

- **Identifiers come from metadata `SchemaName`, never `LogicalName`.** Logical names are lower case
  with no word boundaries (`firstname`), so deriving an identifier from one gives `Firstname`. The
  schema name already carries the casing.
- **`--tables` is required and pulls merge per table.** There is no org-wide mode: it would generate
  an enormous file that is mostly noise, and refreshing one table must not disturb the others.
- **The starter `config/schema.json` is hand-written** and lists only stock columns present in every
  org, so a first real pull cannot remove a constant the sample depends on.

The generated file is committed so a fresh clone compiles without anyone connecting to Dataverse.

## Deterministic ids

Component GUIDs are derived, not random: RFC 4122 version 5 (SHA-1) over a fixed namespace plus the
solution name and the component's logical key — see
[`DeterministicGuid`](../src/Dataverse.Plugins.Tooling/Infrastructure/DeterministicGuid.cs).

This is what makes the two deployment paths compose. A step created by `dv sync` in dev and the same
step arriving via solution import in production carry the *same* `sdkmessageprocessingstepid`, so:

- re-running either is idempotent,
- importing a package over a synced environment updates rather than duplicates,
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

`dv manifest` globs `src/**/*.csproj` and asks **MSBuild** for each candidate's evaluated
properties (`dotnet msbuild -getProperty:`). A project is a plugin assembly when
`DataversePluginAssembly` is `true`, which `Abstractions.Sources.props` sets on import.

Asking MSBuild rather than reading the XML matters: the property is usually set by an import, not
written in the project file, and MSBuild's `TargetPath` removes any need to guess where the build
put the DLL.

The upshot is that adding a project is New Project → build → deploy, with no central list to
remember to update.

## Shared code is linked, not referenced

The Dataverse sandbox loads exactly one assembly per registered plugin type and resolves no
dependencies. A plugin referencing a separate `Abstractions.dll` fails at runtime with an
assembly-load error.

So `Abstractions.Sources.props` compiles the attributes, `PluginBase` and the generated
`Schema.g.cs` **into** each plugin assembly as linked source. Each assembly therefore has its own copy of the attribute types, which
is fine: the scanner matches attributes by full type name, not by assembly identity.

`Dataverse.Plugins.Abstractions.csproj` still exists so that shared code is compiled and analysed
in one place.

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

`config/sdkmessages.json` caches the map. It is generated once by `dv messages pull` and committed,
because CI has no environment to resolve ids against. `dv pack` fails with the missing names listed
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

**Not verified here**, because both need a live Dataverse environment:

- Importing a package, and steps actually firing. Treat the first import into a scratch environment
  as the remaining checkpoint.
- The metadata and message queries in `dv schema pull`. The generator that consumes their output is
  tested; the queries themselves are not. The starter `config/schema.json` was written by hand so
  everything else could be verified without them.
