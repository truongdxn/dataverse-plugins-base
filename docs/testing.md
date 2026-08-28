# Testing plugins

A plugin test runs the **real plugin** against a fake pipeline. No connection, no environment, no
registration, no `pac`. They run in milliseconds, so they belong in the edit-compile loop.

Worked examples:
[`Sample.Plugins.Tests`](../CRM/Plugins/Sample/Sample.Plugins.Tests).

## Setting up a test project

```bash
cd CRM/Plugins/<Solution>
dotnet new dv-plugin-tests -n <Assembly>.Tests --testsFor <Assembly>
```

The whole project file is:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <DataverseTestsFor>CRMCore.Plugins</DataverseTestsFor>
  </PropertyGroup>

  <Import Project="$(PluginTestingProps)" />

  <ItemGroup>
    <ProjectReference Include="..\CRMCore.Plugins\CRMCore.Plugins.csproj" />
  </ItemGroup>
</Project>
```

`DataverseTestsFor` is what `dv test -a <assembly>` matches on. It is declared rather than guessed
from the project name, because a name-matching convention fails silently the first time somebody
names a project differently — and "0 tests, all passed" is the worst possible answer. The build
refuses a test project that omits it.

```bash
.\dv test                      # every test project in the solution
.\dv test -a CRMCore.Plugins   # just this assembly's
```

## The shape of a test

```csharp
[Fact]
public void TheDescriptionIsBuiltFromTheTargetMergedOverThePreImage()
{
    var host = PluginTestHost
        .For(Messages.Update, Contact.EntityLogicalName, Stages.PostOperation)
        .WithTarget(new Entity(Contact.EntityLogicalName, id) { [Contact.Fields.LastName] = "Tran" })
        .WithPreImage("PreImage", existing)
        .WithRows(existing)
        .Execute(new ContactPostUpdate());

    var saved = host.FakeService.Retrieve(Contact.EntityLogicalName, id, new ColumnSet(true));

    Assert.Equal("Nhat Truong Tran", saved.GetAttributeValue<string>(Contact.Fields.Description));
}
```

The generated `Schema` constants work in tests too — the test project references the plugin
assembly, which compiled them in — so a test cannot drift from the columns the plugin uses.

### Arranging

| | |
|---|---|
| `PluginTestHost.For(message, table, stage)` | Start. `Stages.PreValidation` / `PreOperation` / `PostOperation` |
| `.WithTarget(entity)` | The Target input parameter. Also aligns `PrimaryEntityId`, which the platform does and a hand-built context usually forgets |
| `.WithTargetReference(reference)` | For Delete, whose Target is a reference |
| `.WithPreImage(name, entity)` / `.WithPostImage(...)` | Registered images |
| `.WithRows(entity, ...)` | Seeds rows the plugin can retrieve, without them counting as something it created |
| `.WithInputParameter` / `.WithSharedVariable` | Anything else on the context |
| `.WithDepth(n)` | Pipeline depth, to exercise a recursion guard |
| `.WithUser(guid)` | The executing user |
| `.WithService(myService)` | Substitutes the organization service entirely — see below |
| `.Execute(plugin)` | Runs it. Exceptions propagate untouched |

`host.Context` is the raw `FakePluginExecutionContext`, every property settable, for anything the
builders do not cover.

### Asserting

| | |
|---|---|
| `host.Target` | The target as the plugin left it — the point of a pre-operation step |
| `host.FakeService` | `.Rows`, `.Retrieve(...)`, `.Requests` |
| `host.Tracing` | `.Contains(fragment)`, `.Lines`, `.Text` |
| `host.OutputParameter<T>(name)` | What it returned |
| `host.ImpersonatedUsers` | Which user each service was created for. `null` means SYSTEM — useful to prove a plugin reached for elevated privileges deliberately |
| `Assert.Throws<InvalidPluginExecutionException>` | The message a user would actually see |

## What the fakes model, and what they do not

`FakeOrganizationService` is deliberately small.

**Real:** `Create`, `Retrieve`, `Update`, `Delete`. Rows are stored as copies, so a plugin mutating
an entity after passing it to `Create` does not retroactively change what was saved. `Update`
merges only the columns it carries, as the real message does. Retrieving a row that was never
seeded throws, because the real service faults rather than returning null.

**Partial:** `RetrieveMultiple` handles a `QueryExpression` with `Equal`, `NotEqual`, `Null`,
`NotNull` and `In` conditions, nested filters, and column projection. `EntityReference`,
`OptionSetValue` and `Money` compare by their underlying value, so writing a raw `Guid` in a
condition matches a row holding an `EntityReference`.

**Not modelled:** FetchXml, `Associate`/`Disassociate`, and any `OrganizationRequest` you have not
registered. Each throws a message saying what to do instead.

For a specific request, register a handler:

```csharp
host.FakeService.On<WhoAmIRequest>(_ => new WhoAmIResponse());
```

Past that, substitute the service entirely:

```csharp
host.WithService(mySubstitute);   // Moq, NSubstitute, FakeXrmEasy — anything
```

That is the intended escape hatch. Growing `FakeOrganizationService` into a second Dataverse is
not the goal; being enough for the common case, and honest about the rest, is.

## Why the harness never references the abstractions

`[PluginStep]`, `PluginBase` and `ILocalPluginContext` are compiled **into each plugin assembly as
source**, because the Dataverse sandbox resolves no dependent assemblies. So every plugin assembly
holds its own copy of those types, and the copies are different CLR types even though the source is
identical.

A harness referencing its own copy of `ILocalPluginContext` could not hand it to somebody else's
plugin — the types would not match.

So the harness talks only in `Microsoft.Xrm.Sdk` types, which really are shared: it is an
`IServiceProvider`, and it calls `IPlugin.Execute(IServiceProvider)`. That is the same seam the
platform uses. `PluginBase` builds its own `LocalPluginContext` from it, exactly as in production.

One visible consequence: `Stages.PostOperation` in tests rather than `Stage.PostOperation`. The
values are Dataverse's own and cannot change, and a tooling test pins the enum to the same numbers.

## What the tooling's own tests cover

Separate suite, `CRM/Shared/PluginBase/tests`, run with:

```bash
dotnet test CRM/Shared/PluginBase/PluginBase.sln
```

CLI parsing · attribute-to-manifest translation · declaration validation · schema codegen ·
deterministic id stability (with one pinned value) · solution resolution and package output paths ·
and an end-to-end packaging test that runs the real `pac solution pack` and inspects the resulting
zip.

That last one exists because the packager's two worst failure modes are silent: given a malformed
source tree it prints a warning, drops the components, and still exits zero — producing a `.zip`
that imports cleanly and registers nothing. Only inspecting the output catches it. It needs `pac`
installed, and **fails** rather than skips without it.
