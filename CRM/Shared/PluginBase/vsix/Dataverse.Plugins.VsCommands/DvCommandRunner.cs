using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Dataverse.Plugins.VsCommands.Core;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Dataverse.Plugins.VsCommands;

/// <summary>
/// Runs one dv command and reports it.
/// <para>
/// The extension never re-implements what dv does; it starts the same shim a developer would type
/// and shows what comes back. That is the whole design: if a menu item behaves differently from
/// the terminal, the difference is a bug here rather than a second implementation drifting.
/// </para>
/// </summary>
internal sealed class DvCommandRunner
{
    private readonly AsyncPackage _package;
    private readonly DvOutputPane _pane;
    private int _running;

    public DvCommandRunner(AsyncPackage package, DvOutputPane pane)
    {
        _package = package;
        _pane = pane;
    }

    /// <summary>
    /// Runs a command, streaming its output into the pane.
    /// <para>
    /// One at a time. Two dv processes in the same repo would build into the same folders and
    /// interleave their output into the same pane, and the result would be blamed on dv.
    /// </para>
    /// </summary>
    public async Task RunAsync(DvRepo repo, IReadOnlyList<string> arguments, string workingDirectory)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            await _pane.WriteLineAsync("A dv command is already running. Wait for it to finish.");
            await _pane.ActivateAsync();
            return;
        }

        try
        {
            DvInvocation invocation;

            try
            {
                invocation = DvInvocation.For(repo, arguments, workingDirectory);
            }
            catch (DvUnavailableException ex)
            {
                await _pane.ActivateAsync();
                await _pane.WriteLineAsync(ex.Message);
                return;
            }

            await _pane.ActivateAsync();
            await _pane.WriteLineAsync("> " + invocation.Display);
            await _pane.WriteLineAsync("  in " + invocation.WorkingDirectory);
            await _pane.WriteLineAsync(string.Empty);

            await SetStatusAsync(invocation.Display + "...");

            var stopwatch = Stopwatch.StartNew();
            var exitCode = await StartAsync(invocation, line => _pane.WriteLineAsync(line));

            stopwatch.Stop();

            var outcome = exitCode == 0
                ? "Finished in " + stopwatch.Elapsed.TotalSeconds.ToString("0.0") + "s."
                : "Failed with exit code " + exitCode + ".";

            await _pane.WriteLineAsync(string.Empty);
            await _pane.WriteLineAsync(outcome);
            await SetStatusAsync(invocation.Display + " - " + outcome);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <summary>
    /// Runs a command for its output rather than for its effect - 'dv environments', to fill the
    /// picker. Nothing reaches the pane, because a dialog that logged its own preparation would
    /// bury the run the developer actually asked for.
    /// </summary>
    public async Task<string> CaptureAsync(DvRepo repo, IReadOnlyList<string> arguments)
    {
        var captured = new StringBuilder();

        try
        {
            var invocation = DvInvocation.For(repo, arguments);

            await StartAsync(invocation, line =>
            {
                captured.AppendLine(line);
                return Task.CompletedTask;
            });
        }
        catch (DvUnavailableException)
        {
            // The caller falls back to asking for the value by hand.
            return string.Empty;
        }

        return captured.ToString();
    }

    private static async Task<int> StartAsync(DvInvocation invocation, Func<string, Task> onLine)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.FileName,
            Arguments = invocation.Arguments,
            WorkingDirectory = invocation.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        var completed = new TaskCompletionSource<int>();

        await Task.Run(() =>
        {
            try
            {
                using (var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true })
                {
                    // stderr is folded into the same stream on purpose. dv writes warnings to
                    // stdout and errors to stderr, and reading them apart would reorder them
                    // against each other - so a failure would no longer sit under the step that
                    // produced it.
                    process.OutputDataReceived += (_, e) => Emit(onLine, e.Data);
                    process.ErrorDataReceived += (_, e) => Emit(onLine, e.Data);

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    process.WaitForExit();

                    completed.TrySetResult(process.ExitCode);
                }
            }
            catch (Exception ex)
            {
                Emit(onLine, ex.Message);
                completed.TrySetResult(-1);
            }
        });

        return await completed.Task;
    }

    private static void Emit(Func<string, Task> onLine, string line)
    {
        if (line == null)
        {
            return;
        }

        // Fire and forget: the pane write is thread-safe, and awaiting it here would block the
        // pipe that produced the line.
        _ = onLine(line);
    }

    private async Task SetStatusAsync(string text)
    {
        await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

        if (await _package.GetServiceAsync(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
        {
            statusBar.SetText(text);
        }
    }
}
