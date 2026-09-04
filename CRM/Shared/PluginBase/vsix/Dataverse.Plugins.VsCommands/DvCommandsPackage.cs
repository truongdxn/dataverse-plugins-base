using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace Dataverse.Plugins.VsCommands;

/// <summary>
/// The extension itself.
/// <para>
/// It adds no build system, no project system and no state of its own. Every menu item shells out
/// to the dv CLI in the repo that was clicked, which means the IDE and the terminal cannot
/// disagree: there is one implementation, and this is a front end to it.
/// </para>
/// </summary>
[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Dataverse plugin commands", "Runs the dv CLI from Solution Explorer.", "1.0")]
[ProvideMenuResource("Menus.ctmenu", 1)]
[Guid(PackageGuids.PackageString)]
// Loaded once a solution is open, in the background. The menus need a selection to act on, so
// there is nothing to do before that, and loading earlier would only slow Visual Studio's start.
[ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class DvCommandsPackage : AsyncPackage
{
    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await base.InitializeAsync(cancellationToken, progress);

        // Menu registration and DTE both belong to the UI thread.
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        var dte = await GetServiceAsync(typeof(SDTE)) as DTE2;

        if (commandService == null)
        {
            return;
        }

        var pane = new DvOutputPane(this);

        DvMenu.Register(this, commandService, new DvCommandRunner(this, pane), pane, dte);
    }
}
