using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Model;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;

namespace Dataverse.Plugins.Tooling.Deployment;

/// <summary>
/// Reads table metadata and the SDK messages valid for those tables into the committed snapshot,
/// so the schema constants can be regenerated afterwards with no environment.
/// </summary>
public sealed class SchemaPuller
{
    private readonly IOrganizationService _service;

    public SchemaPuller(IOrganizationService service)
    {
        _service = service;
    }

    public SchemaTable PullTable(string logicalName)
    {
        RetrieveEntityResponse response;

        try
        {
            response = (RetrieveEntityResponse)_service.Execute(new RetrieveEntityRequest
            {
                LogicalName = logicalName,
                EntityFilters = EntityFilters.Attributes,
                RetrieveAsIfPublished = true,
            });
        }
        catch (Exception ex)
        {
            throw new ToolException(
                $"Could not read metadata for table '{logicalName}'. Check the logical name " +
                $"(it is lower case, e.g. 'contact' not 'Contact'). {ex.Message}",
                ex);
        }

        var metadata = response.EntityMetadata;

        var columns = metadata.Attributes
            // Skip the internal halves of composite and multi-part columns: they cannot be used in
            // filtering attributes or images, so listing them is noise.
            .Where(attribute => attribute.IsValidForRead != false || attribute.IsValidForCreate == true)
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute.LogicalName))
            .Select(attribute => new SchemaColumn
            {
                LogicalName = attribute.LogicalName,
                SchemaName = string.IsNullOrWhiteSpace(attribute.SchemaName)
                    ? attribute.LogicalName
                    : attribute.SchemaName,
            })
            .GroupBy(column => column.LogicalName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        Log.Item($"{metadata.LogicalName}  ({columns.Count} column(s))");

        return new SchemaTable
        {
            LogicalName = metadata.LogicalName,
            SchemaName = string.IsNullOrWhiteSpace(metadata.SchemaName)
                ? metadata.LogicalName
                : metadata.SchemaName,
            Columns = columns,
        };
    }

    /// <summary>
    /// The SDK messages registerable against the given tables, read through sdkmessagefilter so the
    /// generated constants cover exactly the messages those tables actually support.
    /// </summary>
    public IReadOnlyList<string> PullMessages(IReadOnlyCollection<string> logicalNames)
    {
        var query = new QueryExpression("sdkmessagefilter")
        {
            ColumnSet = new ColumnSet("sdkmessageid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression(
                        "primaryobjecttypecode",
                        ConditionOperator.In,
                        logicalNames.Cast<object>().ToArray()),
                },
            },
            PageInfo = new PagingInfo { Count = 500, PageNumber = 1 },
        };

        var link = query.AddLink("sdkmessage", "sdkmessageid", "sdkmessageid");
        link.EntityAlias = "message";
        link.Columns = new ColumnSet("name");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var page = _service.RetrieveMultiple(query);

            foreach (var record in page.Entities)
            {
                if (record.GetAttributeValue<AliasedValue>("message.name")?.Value is string name &&
                    !string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            if (!page.MoreRecords)
            {
                break;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
