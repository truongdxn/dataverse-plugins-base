using System.Text.Json.Serialization;
using Dataverse.Plugins.Tooling.Configuration;

namespace Dataverse.Plugins.Tooling.Model;

/// <summary>
/// Committed snapshot of the Dataverse metadata the plugins actually reference: the tables named
/// with <c>--tables</c>, their columns, and the SDK messages valid for them.
/// <para>
/// It exists so that <c>Schema.g.cs</c> can be regenerated with no environment, the same way
/// <c>sdkmessages.json</c> lets packing run offline. Pulls merge into it per table, so refreshing
/// one table leaves the rest alone.
/// </para>
/// </summary>
public sealed class SchemaSnapshot
{
    /// <summary>Preserved on load so rewriting the file does not strip its documentation.</summary>
    [JsonPropertyName("$comment")]
    public object Comment { get; set; }

    public DateTimeOffset? GeneratedUtc { get; set; }

    public List<SchemaTable> Tables { get; set; } = new();

    /// <summary>SDK message names valid for the pulled tables. Source of the Messages constants.</summary>
    public List<string> Messages { get; set; } = new();

    public static SchemaSnapshot Load(RepoPaths paths) =>
        File.Exists(paths.SchemaFile)
            ? Normalise(JsonConfig.Read<SchemaSnapshot>(paths.SchemaFile))
            : new SchemaSnapshot();

    public void Save(RepoPaths paths)
    {
        Tables = Tables
            .OrderBy(table => table.LogicalName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var table in Tables)
        {
            table.Columns = table.Columns
                .OrderBy(column => column.LogicalName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        Messages = Messages
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(message => message, StringComparer.OrdinalIgnoreCase)
            .ToList();

        GeneratedUtc = DateTimeOffset.UtcNow;
        JsonConfig.Write(paths.SchemaFile, this);
    }

    /// <summary>Replaces one table's entry, leaving every other table untouched.</summary>
    public void Merge(SchemaTable table)
    {
        Tables.RemoveAll(existing =>
            string.Equals(existing.LogicalName, table.LogicalName, StringComparison.OrdinalIgnoreCase));

        Tables.Add(table);
    }

    public void MergeMessages(IEnumerable<string> messages)
    {
        foreach (var message in messages.Where(m => !string.IsNullOrWhiteSpace(m)))
        {
            if (!Messages.Contains(message, StringComparer.OrdinalIgnoreCase))
            {
                Messages.Add(message);
            }
        }
    }

    private static SchemaSnapshot Normalise(SchemaSnapshot snapshot)
    {
        snapshot.Tables ??= new List<SchemaTable>();
        snapshot.Messages ??= new List<string>();

        foreach (var table in snapshot.Tables)
        {
            table.Columns ??= new List<SchemaColumn>();
        }

        return snapshot;
    }
}

public sealed class SchemaTable
{
    /// <summary>Lower-case logical name, e.g. "contact". This is the value the constant carries.</summary>
    public string LogicalName { get; set; }

    /// <summary>Cased name, e.g. "Contact". This becomes the C# identifier.</summary>
    public string SchemaName { get; set; }

    public List<SchemaColumn> Columns { get; set; } = new();
}

public sealed class SchemaColumn
{
    public string LogicalName { get; set; }

    public string SchemaName { get; set; }
}
