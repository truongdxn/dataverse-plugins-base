using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling.Configuration;

/// <summary>Deserialised config/solution.json - publisher and solution identity only.</summary>
public sealed class SolutionConfig
{
    public PublisherSection Publisher { get; set; } = new();

    public SolutionSection Solution { get; set; } = new();

    public static SolutionConfig Load(RepoPaths paths)
    {
        var config = JsonConfig.Read<SolutionConfig>(paths.SolutionConfigFile);
        config.Validate(paths.SolutionConfigFile);
        return config;
    }

    private void Validate(string path)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(Publisher.UniqueName))
        {
            problems.Add("publisher.uniqueName is required.");
        }

        if (string.IsNullOrWhiteSpace(Publisher.Prefix))
        {
            problems.Add("publisher.prefix is required.");
        }

        if (string.IsNullOrWhiteSpace(Solution.UniqueName))
        {
            problems.Add("solution.uniqueName is required.");
        }

        // Dataverse rejects a solution unique name containing spaces or punctuation at import
        // time, long after the build - so catch it here instead.
        if (!string.IsNullOrWhiteSpace(Solution.UniqueName) &&
            !Solution.UniqueName.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            problems.Add(
                $"solution.uniqueName '{Solution.UniqueName}' may contain only letters, digits and underscores.");
        }

        if (!Version.TryParse(Solution.Version, out _))
        {
            problems.Add($"solution.version '{Solution.Version}' is not a valid version (expected e.g. 1.0.0.0).");
        }

        if (problems.Count > 0)
        {
            throw new ToolException($"{path} is invalid:{Environment.NewLine}  - " +
                                    string.Join(Environment.NewLine + "  - ", problems));
        }
    }

    public sealed class PublisherSection
    {
        public string UniqueName { get; set; }

        public string FriendlyName { get; set; }

        public string Description { get; set; }

        public string Prefix { get; set; }

        /// <summary>Option value prefix, the numeric block new option set values are allocated from.</summary>
        public int OptionValuePrefix { get; set; } = 10000;
    }

    public sealed class SolutionSection
    {
        public string UniqueName { get; set; }

        public string FriendlyName { get; set; }

        public string Description { get; set; }

        public string Version { get; set; } = "1.0.0.0";
    }
}
