using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling.Packaging;

/// <summary>
/// Turns the generated source tree into a solution zip by shelling out to
/// <c>pac solution pack</c>, rather than writing the zip directly.
/// <para>
/// The packager owns details that are invisible until import fails - [Content_Types].xml, where
/// the assembly bytes physically live, how sharded components are folded back into
/// customizations.xml. Letting it do that means a mistake in our generated source surfaces here,
/// at build time, instead of in a production import.
/// </para>
/// </summary>
public static class SolutionPacker
{
    public static void Pack(string sourceDirectory, string zipPath, bool managed)
    {
        var pac = LocatePac();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(zipPath)));

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        var result = ProcessRunner.Run(
            pac,
            new[]
            {
                "solution",
                "pack",
                "--zipfile", zipPath,
                "--folder", sourceDirectory,
                "--packagetype", managed ? "Managed" : "Unmanaged",
            });

        var output = result.CombinedOutput;

        if (!result.Succeeded)
        {
            throw new ToolException($"pac solution pack failed:{Environment.NewLine}{output}");
        }

        // The packager reports these two as informational text and still exits zero, having
        // silently dropped components from the zip. Treating them as failures is the whole
        // reason packing goes through a verification step rather than trusting the exit code.
        foreach (var line in output.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            if (line.Contains("unexpected children", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("not defined in customizations", StringComparison.OrdinalIgnoreCase))
            {
                throw new ToolException(
                    "pac solution pack reported that components were skipped, so the zip is " +
                    $"incomplete:{Environment.NewLine}{output}");
            }

            Log.Detail(line);
        }

        if (!File.Exists(zipPath))
        {
            throw new ToolException($"pac reported success but no package was produced at '{zipPath}'.");
        }
    }

    /// <summary>
    /// Finds pac on PATH, then in the two places its installers put it. Looking in the well-known
    /// locations avoids failing on a machine where the CLI is installed but the shell has not
    /// been restarted since.
    /// </summary>
    public static string LocatePac()
    {
        var candidates = new List<string> { "pac" };

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "Microsoft", "PowerAppsCLI", "pac.cmd"));
        }

        if (!string.IsNullOrEmpty(userProfile))
        {
            candidates.Add(Path.Combine(userProfile, ".dotnet", "tools", "pac.exe"));
            candidates.Add(Path.Combine(userProfile, ".dotnet", "tools", "pac"));
        }

        foreach (var candidate in candidates)
        {
            if (candidate == "pac")
            {
                // No path to test - try running it and see whether the OS can start it.
                try
                {
                    var probe = ProcessRunner.Run(candidate, new[] { "help" });

                    if (probe.Succeeded)
                    {
                        return candidate;
                    }
                }
                catch (ToolException)
                {
                    continue;
                }
            }
            else if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new ToolException(
            "Could not find the Power Platform CLI (pac), which is required to build a solution " +
            "package. Install it with 'dotnet tool install --global Microsoft.PowerApps.CLI.Tool' " +
            "or from https://aka.ms/PowerPlatformCLI.");
    }
}
