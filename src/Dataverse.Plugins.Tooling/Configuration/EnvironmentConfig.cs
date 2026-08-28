using System.Text.Json;
using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling.Configuration;

/// <summary>
/// One target environment. Secrets are never stored here - the config names environment
/// variables, and the values are read from the process environment at connect time.
/// </summary>
public sealed class DataverseEnvironment
{
    public string Url { get; set; }

    /// <summary>"interactive" or "clientsecret".</summary>
    public string Auth { get; set; } = "interactive";

    public string Description { get; set; }

    public string TenantIdVar { get; set; }

    public string ClientIdVar { get; set; }

    public string ClientSecretVar { get; set; }

    public bool UsesClientSecret =>
        string.Equals(Auth, "clientsecret", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Builds the ServiceClient connection string, reading any secret from the environment.
    /// Throws with the variable's name when it is not set, so the fix is obvious in CI logs.
    /// </summary>
    public string BuildConnectionString(string environmentName)
    {
        if (string.IsNullOrWhiteSpace(Url))
        {
            throw new ToolException($"Environment '{environmentName}' has no url.");
        }

        if (!UsesClientSecret)
        {
            // Persist the token cache, otherwise every single command opens a browser sign-in.
            // Keyed per environment so several environments can stay signed in at once.
            return $"AuthType=OAuth;Url={Url};RedirectUri=http://localhost;LoginPrompt=Auto;" +
                   $"TokenCacheStorePath={TokenCachePath(environmentName)}";
        }

        var clientId = RequireVariable(environmentName, nameof(ClientIdVar), ClientIdVar);
        var clientSecret = RequireVariable(environmentName, nameof(ClientSecretVar), ClientSecretVar);

        return $"AuthType=ClientSecret;Url={Url};ClientId={clientId};ClientSecret={clientSecret}";
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

    private static string RequireVariable(string environmentName, string setting, string variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            throw new ToolException(
                $"Environment '{environmentName}' uses clientsecret auth but does not name a {setting}.");
        }

        var value = Environment.GetEnvironmentVariable(variableName);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ToolException(
                $"Environment variable '{variableName}' is not set. It supplies the {setting} for " +
                $"environment '{environmentName}'.");
        }

        return value;
    }
}

/// <summary>All configured environments, with local overrides merged in.</summary>
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
