using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Dataverse.Plugins.VsCommands;

/// <summary>
/// The "Dataverse" pane in the Output window - the only place command output goes.
/// <para>
/// One pane for every command rather than one per command, because the questions a developer asks
/// are "what did the last run say" and "did it pass", and both are answered by scrolling one
/// place. The pane is created once and reused; recreating it per run loses the scrollback.
/// </para>
/// </summary>
internal sealed class DvOutputPane
{
    private readonly AsyncPackage _package;
    private IVsOutputWindowPane _pane;

    public DvOutputPane(AsyncPackage package)
    {
        _package = package;
    }

    public async Task WriteLineAsync(string text) => await WriteAsync(text + Environment.NewLine);

    public async Task WriteAsync(string text)
    {
        var pane = await GetPaneAsync();

        // Thread-safe by contract, which matters: process output arrives on a thread pool thread
        // and blocking it to reach the UI thread would stall the pipe and deadlock a chatty build.
        pane?.OutputStringThreadSafe(text);
    }

    /// <summary>Brings the pane forward and empties it, so a run starts from a clean page.</summary>
    public async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var pane = await GetPaneAsync(cancellationToken);

        if (pane == null)
        {
            return;
        }

        pane.Clear();
        pane.Activate();
    }

    private async Task<IVsOutputWindowPane> GetPaneAsync(CancellationToken cancellationToken = default)
    {
        if (_pane != null)
        {
            return _pane;
        }

        await _package.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

        var window = await _package.GetServiceAsync(typeof(SVsOutputWindow)) as IVsOutputWindow;

        if (window == null)
        {
            return null;
        }

        var paneGuid = PackageGuids.OutputPane;

        // visible: 1 so it survives a restart of the Output window; clearWithSolution: 0 so the
        // last run's output is still there after closing a solution, which is when people go
        // looking for it.
        window.CreatePane(ref paneGuid, "Dataverse", 1, 0);
        window.GetPane(ref paneGuid, out _pane);

        return _pane;
    }
}
