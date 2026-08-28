using Dataverse.Plugins.Tooling.Infrastructure;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Dataverse.Plugins.Tooling.Deployment;

/// <summary>
/// Reads sdkmessage ids out of an environment into the committed cache, so that packing - which
/// can only reference messages by GUID - works afterwards with no environment at all.
/// </summary>
public sealed class MessagePuller
{
    private readonly IOrganizationService _service;

    public MessagePuller(IOrganizationService service)
    {
        _service = service;
    }

    /// <param name="wanted">
    /// Messages to look up. Pass null to pull every message in the environment, which is
    /// occasionally useful but produces a much larger file to review.
    /// </param>
    public IReadOnlyDictionary<string, Guid> Pull(IReadOnlyCollection<string> wanted)
    {
        var query = new QueryExpression("sdkmessage")
        {
            ColumnSet = new ColumnSet("sdkmessageid", "name"),
            Criteria = new FilterExpression(),
            PageInfo = new PagingInfo { Count = 500, PageNumber = 1 },
            Orders = { new OrderExpression("name", OrderType.Ascending) },
        };

        if (wanted is { Count: > 0 })
        {
            query.Criteria.AddCondition("name", ConditionOperator.In, wanted.Cast<object>().ToArray());
        }

        var found = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        while (true)
        {
            var page = _service.RetrieveMultiple(query);

            foreach (var record in page.Entities)
            {
                var name = record.GetAttributeValue<string>("name");

                if (!string.IsNullOrWhiteSpace(name))
                {
                    found[name] = record.Id;
                }
            }

            if (!page.MoreRecords)
            {
                break;
            }

            query.PageInfo.PageNumber++;
            query.PageInfo.PagingCookie = page.PagingCookie;
        }

        if (wanted is { Count: > 0 })
        {
            var missing = wanted
                .Where(name => !found.ContainsKey(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missing.Count > 0)
            {
                Log.Warn(
                    "These messages do not exist in this environment and were not cached: " +
                    string.Join(", ", missing) +
                    ". A custom API or action must be deployed before its message can be resolved.");
            }
        }

        return found;
    }
}
