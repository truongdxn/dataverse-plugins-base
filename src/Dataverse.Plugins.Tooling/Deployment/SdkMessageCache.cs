using System.Text.Json.Serialization;
using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Infrastructure;

namespace Dataverse.Plugins.Tooling.Deployment;

/// <summary>
/// Maps SDK message names to their sdkmessageid.
/// <para>
/// A packed solution references a message by GUID only - the customizations schema has no
/// message-name element - so packing needs this map and cannot look it up from an environment.
/// The ids are seeded per organization and stable, which is precisely why a solution containing
/// plugin steps is portable between environments at all.
/// </para>
/// <para>
/// Populate once with <c>dv messages pull --env dev</c> and commit the result; packing is then
/// fully offline, which is what lets CI build the zip with no Dataverse connection.
/// </para>
/// </summary>
public sealed class SdkMessageCache
{
    /// <summary>Preserved on load so rewriting the file does not strip its documentation.</summary>
    [JsonPropertyName("$comment")]
    public object Comment { get; set; }

    public Dictionary<string, Guid> Messages { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static SdkMessageCache Load(RepoPaths paths)
    {
        if (!File.Exists(paths.SdkMessageCacheFile))
        {
            return new SdkMessageCache();
        }

        var cache = JsonConfig.Read<SdkMessageCache>(paths.SdkMessageCacheFile);
        cache.Messages ??= new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        // The file is written camel-cased but message names are proper-cased ("Update"); make
        // lookups insensitive regardless of how the file was edited by hand.
        cache.Messages = new Dictionary<string, Guid>(cache.Messages, StringComparer.OrdinalIgnoreCase);
        return cache;
    }

    public void Save(RepoPaths paths)
    {
        Messages = Messages
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        JsonConfig.Write(paths.SdkMessageCacheFile, this);
        Log.Detail($"Wrote {Messages.Count} message id(s) to {paths.SdkMessageCacheFile}.");
    }

    public Guid Require(string messageName, string usedBy)
    {
        if (Messages.TryGetValue(messageName, out var id) && id != Guid.Empty)
        {
            return id;
        }

        throw new ToolException(
            $"No sdkmessageid cached for message '{messageName}' (needed by {usedBy}). " +
            "Run 'dv messages pull --env <name>' once against any environment and commit " +
            "config/sdkmessages.json.");
    }

    /// <summary>Message names referenced by the manifest that are not in the cache.</summary>
    public IReadOnlyList<string> FindMissing(IEnumerable<string> messageNames) =>
        messageNames
            .Where(name => !Messages.ContainsKey(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
