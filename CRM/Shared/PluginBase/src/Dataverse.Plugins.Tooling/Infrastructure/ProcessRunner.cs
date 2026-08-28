using System.Diagnostics;
using System.Text;

namespace Dataverse.Plugins.Tooling.Infrastructure;

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    public string CombinedOutput =>
        string.Join(
            Environment.NewLine,
            new[] { StandardOutput, StandardError }.Where(s => !string.IsNullOrWhiteSpace(s)));
}

/// <summary>Runs an external tool and captures its output. Used for dotnet msbuild and pac.</summary>
public static class ProcessRunner
{
    public static ProcessResult Run(string fileName, IEnumerable<string> arguments, string workingDirectory = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory(),
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Log.Detail($"> {fileName} {string.Join(" ", startInfo.ArgumentList)}");

        using var process = new Process { StartInfo = startInfo };

        var standardOutput = new StringBuilder();
        var standardError = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                standardOutput.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                standardError.AppendLine(e.Data);
            }
        };

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new ToolException($"Could not start '{fileName}': {ex.Message}", ex);
        }

        // Read both streams asynchronously; reading one to completion before the other
        // deadlocks as soon as a tool fills the pipe buffer it is not being drained from.
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, standardOutput.ToString(), standardError.ToString());
    }
}
