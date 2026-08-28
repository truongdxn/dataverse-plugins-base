using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling.Configuration;

/// <summary>
/// One developer sandbox.
/// <para>
/// Interactive sign-in only, and deliberately so. This repo covers the development phase: it
/// registers steps into a sandbox a developer is signed into, and builds a .zip for somebody else
/// to import. Nothing here runs unattended, so nothing here needs a client secret - and not
/// accepting one is what keeps a credential from ever being wanted in a config file.
/// </para>
/// </summary>
public sealed class DataverseEnvironment
{
    public string Url { get; set; }

    public string Description { get; set; }

    /// <summary>Builds the ServiceClient connection string.</summary>
    public string BuildConnectionString(string environmentName)
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            throw new ToolException($"Environment '{environmentName}' has no url.");
        }

        // Persist the token cache, otherwise every single command opens a browser sign-in.
        // Keyed per environment so several environments can stay signed in at once.
        return $"AuthType=OAuth;Url={Url};RedirectUri=http://localhost;LoginPrompt=Auto;" +
               $"TokenCacheStorePath={TokenCachePath(environmentName)}";
    }

    /// <summary>
    /// Where the interactive sign-in is cached. Under LocalApplicationData rather than the repo,
    /// so a token never lands somewhere it could be committed.
    /// </summary>
    private static string TokenCachePath(string environmentName)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DataversePluginBase");

        Directory.CreateDirectory(directory);

        var safeName = string.Concat(
            environmentName.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));

        return Path.Combine(directory, $"tokencache-{safeName}.dat");
    }
}

/// <summary>
/// All configured environments, with local overrides merged in. Repo-wide rather than per
/// solution: a sandbox belongs to a developer, not to a product.
/// </summary>
public sealed class EnvironmentConfig
{
    public Dictionary<string, DataverseEnvironment> Environments { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static EnvironmentConfig Load(RepoPaths paths)
    {
        var config = JsonConfig.Read<EnvironmentConfig>(paths.EnvironmentsConfigFile);

        // environments.local.json is git-ignored and lets a developer point 'dev' at their own
        // sandbox without dirtying the shared file.
        if (File.Exists(paths.LocalEnvironmentsConfigFile))
        {
            var local = JsonConfig.Read<EnvironmentConfig>(paths.LocalEnvironmentsConfigFile);

            foreach (var (name, environment) in local.Environments)
            {
                config.Environments[name] = environment;
            }

            Log.Detail($"Merged overrides from {Path.GetFileName(paths.LocalEnvironmentsConfigFile)}.");
        }

        return config;
    }

    public DataverseEnvironment Get(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ToolException("No environment specified. Pass --env <name>.");
        }

        if (!Environments.TryGetValue(name, out var environment))
        {
            var known = Environments.Count == 0
                ? "(none configured)"
                : string.Join(", ", Environments.Keys.OrderBy(k => k));

            throw new ToolException($"Unknown environment '{name}'. Configured environments: {known}.");
        }

        return environment;
    }
}
