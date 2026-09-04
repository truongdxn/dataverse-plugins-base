using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Dataverse.Plugins.VsCommands.Core;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace Dataverse.Plugins.VsCommands;

/// <summary>
/// Every menu item, and what it runs.
/// <para>
/// Handlers do three things and no more: read the selection, ask for anything dv needs that a
/// click cannot supply, and hand the arguments to the runner. The arguments themselves are built
/// in <see cref="DvArguments"/>, which is testable without Visual Studio - so the part most likely
/// to be wrong is the part that is covered.
/// </para>
/// </summary>
internal sealed class DvMenu
{
    private readonly AsyncPackage _package;
    private readonly DvCommandRunner _runner;
    private readonly DvOutputPane _pane;
    private readonly DTE2 _dte;

    private DvMenu(AsyncPackage package, DvCommandRunner runner, DvOutputPane pane, DTE2 dte)
    {
        _package = package;
        _runner = runner;
        _pane = pane;
        _dte = dte;
    }

    public static void Register(AsyncPackage package, OleMenuCommandService service, DvCommandRunner runner, DvOutputPane pane, DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var menu = new DvMenu(package, runner, pane, dte);

        // Project node.
        menu.Add(service, PackageIds.BuildAssembly, (s, r) => menu.RunAsync(r, DvArguments.Build(s.ProjectName, s.Configuration), s));
        menu.Add(service, PackageIds.TestAssembly, (s, r) => menu.RunAsync(r, DvArguments.Test(s.ProjectName, s.Configuration), s));
        menu.Add(service, PackageIds.ValidateAssembly, (s, r) => menu.RunAsync(r, DvArguments.Validate(s.ProjectName), s));
        menu.Add(service, PackageIds.ManifestAssembly, (s, r) => menu.RunAsync(r, DvArguments.Manifest(s.ProjectName, s.Configuration), s));
        menu.Add(service, PackageIds.SyncAssembly, (s, r) => menu.SyncAsync(r, s, s.ProjectName));
        menu.Add(service, PackageIds.NewPlugin, (s, r) => menu.NewPluginAsync(r, s));
        menu.Add(service, PackageIds.NewTests, (s, r) => menu.NewTestsAsync(r, s));

        // Solution node.
        menu.Add(service, PackageIds.BuildSolution, (s, r) => menu.RunAsync(r, DvArguments.Build(null, s.Configuration), s));
        menu.Add(service, PackageIds.TestSolution, (s, r) => menu.RunAsync(r, DvArguments.Test(null, s.Configuration), s));
        menu.Add(service, PackageIds.ValidateSolution, (s, r) => menu.RunAsync(r, DvArguments.Validate(null), s));
        menu.Add(service, PackageIds.ManifestSolution, (s, r) => menu.RunAsync(r, DvArguments.Manifest(null, s.Configuration), s));
        menu.Add(service, PackageIds.PackSolution, (s, r) => menu.PackAsync(r, s));
        menu.Add(service, PackageIds.SyncSolution, (s, r) => menu.SyncAsync(r, s, null));
        menu.Add(service, PackageIds.ListSolutions, (s, r) => menu.RunAsync(r, DvArguments.Solutions(), s));
        menu.Add(service, PackageIds.ListEnvironments, (s, r) => menu.RunAsync(r, DvArguments.Environments(), s));
        menu.Add(service, PackageIds.SchemaPull, (s, r) => menu.SchemaPullAsync(r, s));
        menu.Add(service, PackageIds.SchemaCodegen, (s, r) => menu.RunAsync(r, DvArguments.SchemaCodegen(), s));
        menu.Add(service, PackageIds.MessagesPull, (s, r) => menu.MessagesPullAsync(r, s));
        menu.Add(service, PackageIds.NewSolution, (s, r) => menu.NewSolutionAsync(r, s));
        menu.Add(service, PackageIds.NewAssembly, (s, r) => menu.NewAssemblyAsync(r, s));
    }

    private void Add(OleMenuCommandService service, int id, Func<SolutionSelection, DvRepo, Task> handler)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var command = new OleMenuCommand(
            (sender, e) => Invoke(handler),
            new CommandID(PackageGuids.CommandSet, id));

        // Without this the "Dataverse" menu would appear on every project in every solution a
        // developer opens, Dataverse or not.
        command.BeforeQueryStatus += (sender, e) =>
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (sender is OleMenuCommand menuCommand)
            {
                menuCommand.Visible = FindRepo(out _) != null;
            }
        };

        service.AddCommand(command);
    }

    private void Invoke(Func<SolutionSelection, DvRepo, Task> handler)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        var repo = FindRepo(out var selection);

        if (repo == null)
        {
            // Should be unreachable while BeforeQueryStatus hides the menu, but a menu that has
            // gone stale between the query and the click must not throw into Visual Studio.
            return;
        }

        // FileAndForget is how a package starts work from a click: the exception lands in the
        // activity log instead of a dialog nobody can copy from.
        _package.JoinableTaskFactory
            .RunAsync(async () => await handler(selection, repo))
            .FileAndForget("dataverse/vscommands/invoke");
    }

    private DvRepo FindRepo(out SolutionSelection selection)
    {
        ThreadHelper.ThrowIfNotOnUIThread();

        selection = SolutionSelection.From(_dte);

        return DvRepo.Find(selection.Directory);
    }

    private Task RunAsync(DvRepo repo, IReadOnlyList<string> arguments, SolutionSelection selection) =>
        _runner.RunAsync(repo, arguments, selection.Directory);

    /// <summary>
    /// Shows a dialog, from the UI thread whatever thread the handler happens to be on. Every
    /// prompt goes through here rather than calling <see cref="Prompt"/> directly, because a WPF
    /// window created off the UI thread does not fail politely.
    /// </summary>
    private async Task<bool> AskAsync(string title, List<PromptField> fields)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

        return Prompt.TryAsk(title, fields);
    }

    private async Task SyncAsync(DvRepo repo, SolutionSelection selection, string assembly)
    {
        var environments = await AskEnvironmentAsync(repo, "Sync" + (assembly == null ? string.Empty : " " + assembly));

        if (environments == null)
        {
            return;
        }

        await RunAsync(repo, DvArguments.Sync(assembly, environments.Environment, environments.Prune, selection.Configuration), selection);
    }

    private async Task PackAsync(DvRepo repo, SolutionSelection selection)
    {
        var version = new PromptField("Solution version (blank keeps the one in solution.json)");

        if (!await AskAsync("Pack solution", new List<PromptField> { version }))
        {
            return;
        }

        await RunAsync(repo, DvArguments.Pack(version.Value, selection.Configuration), selection);
    }

    private async Task SchemaPullAsync(DvRepo repo, SolutionSelection selection)
    {
        var environment = await EnvironmentFieldAsync(repo);
        var tables = new PromptField(
            "Tables (comma separated, blank for all)",
            hint: "Pulling every table takes a while and generates a large constants file.");

        if (!await AskAsync("Pull schema", new List<PromptField> { environment, tables }))
        {
            return;
        }

        await RunAsync(repo, DvArguments.SchemaPull(environment.Value, tables.Value), selection);
    }

    private async Task MessagesPullAsync(DvRepo repo, SolutionSelection selection)
    {
        var environment = await EnvironmentFieldAsync(repo);

        if (!await AskAsync("Pull message ids", new List<PromptField> { environment }))
        {
            return;
        }

        await RunAsync(repo, DvArguments.MessagesPull(environment.Value), selection);
    }

    private async Task NewSolutionAsync(DvRepo repo, SolutionSelection selection)
    {
        var name = new PromptField("Solution name");
        var prefix = new PromptField("Publisher prefix (blank uses the name)");
        var uniqueName = new PromptField(
            "Unique name (blank uses the name)",
            hint: "Chosen once. Every component id derives from it, so changing it later re-creates everything.");

        if (!await AskAsync("New solution", new List<PromptField> { name, prefix, uniqueName }) ||
            string.IsNullOrWhiteSpace(name.Value))
        {
            return;
        }

        await RunAsync(repo, DvArguments.NewSolution(name.Value, prefix.Value, uniqueName.Value), selection);
        await ReloadHintAsync();
    }

    private async Task NewAssemblyAsync(DvRepo repo, SolutionSelection selection)
    {
        var name = new PromptField("Assembly name", hint: "Created inside the solution folder the selection is in.");

        if (!await AskAsync("New plugin assembly", new List<PromptField> { name }) ||
            string.IsNullOrWhiteSpace(name.Value))
        {
            return;
        }

        await RunAsync(repo, DvArguments.NewAssembly(name.Value), selection);
        await ReloadHintAsync();
    }

    private async Task NewTestsAsync(DvRepo repo, SolutionSelection selection)
    {
        var name = new PromptField("Test project name", (selection.ProjectName ?? "Plugins") + ".Tests");

        if (!await AskAsync("New test project", new List<PromptField> { name }) ||
            string.IsNullOrWhiteSpace(name.Value))
        {
            return;
        }

        await RunAsync(repo, DvArguments.NewTests(name.Value, selection.ProjectName), selection);
        await ReloadHintAsync();
    }

    private async Task NewPluginAsync(DvRepo repo, SolutionSelection selection)
    {
        var name = new PromptField("Class name");
        var entity = new PromptField("Table logical name", "account");
        var message = new PromptField("Message", "Update", PromptKind.Choice,
            new List<string> { "Create", "Update", "Delete", "Retrieve", "RetrieveMultiple", "Associate", "Disassociate" });
        var stage = new PromptField("Stage", "PostOperation", PromptKind.Choice,
            new List<string> { "PreValidation", "PreOperation", "PostOperation" });

        if (!await AskAsync("New plugin class", new List<PromptField> { name, entity, message, stage }) ||
            string.IsNullOrWhiteSpace(name.Value))
        {
            return;
        }

        await RunAsync(repo, DvArguments.NewPlugin(name.Value, entity.Value, message.Value, stage.Value), selection);
        await ReloadHintAsync();
    }

    /// <summary>
    /// Asks which sandbox, and whether to prune.
    /// <para>
    /// Prune is on the same dialog rather than behind a second confirmation because it belongs to
    /// the decision being made - but it starts unticked and says what it does, because it deletes
    /// registrations that exist in the environment and are not declared in source.
    /// </para>
    /// </summary>
    private async Task<SyncAnswer> AskEnvironmentAsync(DvRepo repo, string title)
    {
        var environment = await EnvironmentFieldAsync(repo);
        var prune = new PromptField(
            "Delete registrations that are not declared in source (--prune)",
            kind: PromptKind.Check,
            hint: "Off by default. On, anything registered in the environment but missing from the code is removed.");

        if (!await AskAsync(title, new List<PromptField> { environment, prune }) ||
            string.IsNullOrWhiteSpace(environment.Value))
        {
            return null;
        }

        return new SyncAnswer { Environment = environment.Value, Prune = prune.Checked };
    }

    /// <summary>
    /// Fills the environment list by asking dv, not by reading environments.json - so the
    /// git-ignored local overlay applies here exactly as it does in a terminal.
    /// </summary>
    private async Task<PromptField> EnvironmentFieldAsync(DvRepo repo)
    {
        var listing = await _runner.CaptureAsync(repo, DvArguments.Environments());
        var environments = EnvironmentList.Parse(listing);
        var names = new List<string>();

        foreach (var environment in environments)
        {
            names.Add(environment.Name);
        }

        var preferred = EnvironmentList.Preferred(environments);

        return new PromptField("Environment", preferred?.Name ?? "dev", PromptKind.Choice, names);
    }

    private async Task ReloadHintAsync() =>
        await _pane.WriteLineAsync(
            "New files are on disk. Visual Studio does not pick projects up on its own - use " +
            "Add > Existing Project, or reload the solution.");

    private sealed class SyncAnswer
    {
        public string Environment { get; set; }

        public bool Prune { get; set; }
    }
}
