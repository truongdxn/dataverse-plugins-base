using System;
using System.Collections.Generic;
using System.Text;

namespace Dataverse.Plugins.VsCommands.Core;

/// <summary>What to start, with which arguments, and where. Everything a process launch needs.</summary>
public sealed class DvInvocation
{
    private DvInvocation(string fileName, string arguments, string workingDirectory, string display)
    {
        FileName = fileName;
        Arguments = arguments;
        WorkingDirectory = workingDirectory;
        Display = display;
    }

    public string FileName { get; }

    public string Arguments { get; }

    public string WorkingDirectory { get; }

    /// <summary>What to echo into the Output pane, so the run can be repeated in a terminal.</summary>
    public string Display { get; }

    /// <summary>
    /// Builds the launch for one dv command.
    /// <para>
    /// The shim is preferred over invoking the tool directly, and that is the whole point: dv.ps1
    /// already finds a dotnet that carries an SDK, decides between building the tool from source
    /// and running the pinned package, and rebuilds it when its sources have changed. Re-deciding
    /// any of that here would be a second implementation to keep in step with the first, and it
    /// would drift.
    /// </para>
    /// </summary>
    /// <exception cref="DvUnavailableException">
    /// When the repo offers neither the shim nor a tool manifest.
    /// </exception>
    public static DvInvocation For(DvRepo repo, IReadOnlyList<string> arguments, string workingDirectory = null)
    {
        if (repo == null)
        {
            throw new ArgumentNullException(nameof(repo));
        }

        var directory = string.IsNullOrWhiteSpace(workingDirectory) ? repo.Root : workingDirectory;
        var dvArguments = Join(arguments);

        if (repo.HasShim)
        {
            // -NoProfile because a developer's profile can print banners, change the working
            // directory or fail outright, none of which should reach the Output pane as if dv
            // had said it. -ExecutionPolicy Bypass because the shim is not signed and a machine
            // policy of AllSigned would otherwise stop it.
            var arguments1 = "-NoProfile -ExecutionPolicy Bypass -File " + Quote(repo.ShimFile) +
                             (dvArguments.Length == 0 ? string.Empty : " " + dvArguments);

            return new DvInvocation("powershell.exe", arguments1, directory, "dv " + dvArguments);
        }

        if (repo.HasToolManifest)
        {
            // A consumer repo that did not copy the shims. 'tool run' resolves the version pinned
            // in the manifest; if nothing is restored yet dotnet says so plainly enough.
            var arguments2 = "tool run dv" + (dvArguments.Length == 0 ? string.Empty : " " + dvArguments);

            return new DvInvocation("dotnet", arguments2, directory, "dotnet dv " + dvArguments);
        }

        throw new DvUnavailableException(
            "dv is neither built from source nor pinned as a tool in " + repo.Root + ". Expected " +
            DvRepo.ShimFileName + " beside " + DvRepo.MarkerFileName + ", or " +
            DvRepo.ToolManifestPath + ". In a consumer repo run: dotnet new tool-manifest; " +
            "dotnet tool install Dataverse.Plugins.Tooling.");
    }

    /// <summary>
    /// Joins arguments into one command line, quoting only what needs it.
    /// <para>
    /// Solution names, assembly names and folder paths all reach here from Solution Explorer, and
    /// a project under "C:\My Projects\" would otherwise arrive at dv as two arguments and fail
    /// with something that mentions neither the space nor the path.
    /// </para>
    /// </summary>
    public static string Join(IReadOnlyList<string> arguments)
    {
        if (arguments == null || arguments.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var argument in arguments)
        {
            if (argument == null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(NeedsQuoting(argument) ? Quote(argument) : argument);
        }

        return builder.ToString();
    }

    private static bool NeedsQuoting(string value)
    {
        if (value.Length == 0)
        {
            return true;
        }

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || character == '"')
            {
                return true;
            }
        }

        return false;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}

/// <summary>The repo has no way to run dv. Carries the instruction rather than just the fact.</summary>
public sealed class DvUnavailableException : Exception
{
    public DvUnavailableException(string message)
        : base(message)
    {
    }
}
