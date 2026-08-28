namespace Dataverse.Plugins.Tooling.Infrastructure;

/// <summary>
/// Locates a <c>dotnet</c> executable that actually carries an SDK.
/// <para>
/// Relying on PATH is not enough, and neither is using whichever installation happens to be running
/// this process. A machine can easily have several: a runtime-only install under Program Files
/// (which the apphost will happily start us from) alongside a user-local one that has the SDK. The
/// tool shells out to <c>dotnet msbuild</c>, so a runtime-only host fails with "No .NET SDKs were
/// found" even though an SDK is installed a directory away.
/// </para>
/// <para>
/// So every candidate root is checked for a populated <c>sdk</c> folder before being accepted.
/// </para>
/// </summary>
public static class DotNetHost
{
    private static readonly Lazy<string> Resolved = new(Locate);

    public static string Path => Resolved.Value;

    private static string Locate()
    {
        foreach (var root in CandidateRoots())
        {
            if (!HasSdk(root))
            {
                continue;
            }

            var executable = ExecutableIn(root);

            if (executable is not null)
            {
                Log.Detail($"Using dotnet with an SDK at: {executable}");
                return executable;
            }
        }

        // Nothing verifiable found. Let PATH decide and let the caller report the failure, which
        // includes the resolved path so the cause is visible.
        Log.Detail("No dotnet installation with an SDK found; falling back to 'dotnet' from PATH.");
        return "dotnet";
    }

    private static IEnumerable<string> CandidateRoots()
    {
        // Set by the SDK when it launches a tool, and points at the exact host in use.
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");

        if (!string.IsNullOrWhiteSpace(hostPath))
        {
            var directory = System.IO.Path.GetDirectoryName(hostPath);

            if (!string.IsNullOrEmpty(directory))
            {
                yield return directory;
            }
        }

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");

        if (!string.IsNullOrWhiteSpace(dotnetRoot))
        {
            yield return dotnetRoot;
        }

        // The framework assemblies live at <root>/shared/Microsoft.NETCore.App/<version>/, so three
        // levels up is the installation running this process.
        var runtimeDirectory = System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location);

        if (!string.IsNullOrEmpty(runtimeDirectory))
        {
            yield return System.IO.Path.GetFullPath(
                System.IO.Path.Combine(runtimeDirectory, "..", "..", ".."));
        }

        // The two default install locations, user-local first: a user-local SDK is the one that
        // exists when the machine-wide installer could not be run without administrator rights.
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (!string.IsNullOrEmpty(localAppData))
        {
            yield return System.IO.Path.Combine(localAppData, "Microsoft", "dotnet");
        }

        foreach (var variable in new[] { "ProgramFiles", "ProgramW6432" })
        {
            var programFiles = Environment.GetEnvironmentVariable(variable);

            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                yield return System.IO.Path.Combine(programFiles, "dotnet");
            }
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            yield return "/usr/share/dotnet";
            yield return "/usr/local/share/dotnet";
        }
    }

    /// <summary>A root counts only when its sdk folder holds at least one version.</summary>
    private static bool HasSdk(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return false;
        }

        try
        {
            var sdkDirectory = System.IO.Path.Combine(root, "sdk");
            return Directory.Exists(sdkDirectory) && Directory.EnumerateDirectories(sdkDirectory).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string ExecutableIn(string root)
    {
        foreach (var fileName in new[] { "dotnet.exe", "dotnet" })
        {
            var candidate = System.IO.Path.Combine(root, fileName);

            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
