using Dataverse.Plugins.Tooling.Configuration;
using Dataverse.Plugins.Tooling.Infrastructure;
using Microsoft.PowerPlatform.Dataverse.Client;

namespace Dataverse.Plugins.Tooling.Deployment;

public static class DataverseConnection
{
    public static ServiceClient Connect(string environmentName, DataverseEnvironment environment)
    {
        Log.Info($"Connecting to '{environmentName}' ({environment.Url})...");

        ServiceClient client;

        try
        {
            client = new ServiceClient(environment.BuildConnectionString(environmentName));
        }
        catch (Exception ex) when (ex is not ToolException)
        {
            throw new ToolException($"Could not connect to '{environmentName}': {ex.Message}", ex);
        }

        if (!client.IsReady)
        {
            // LastError carries the actionable part (expired secret, wrong tenant, no such org);
            // the exception message alone usually does not.
            var detail = string.IsNullOrWhiteSpace(client.LastError)
                ? client.LastException?.Message ?? "no further detail available"
                : client.LastError;

            client.Dispose();
            throw new ToolException($"Could not connect to '{environmentName}': {detail}");
        }

        Log.Success($"Connected to {client.ConnectedOrgFriendlyName} as {client.OAuthUserId ?? "service principal"}.");
        return client;
    }
}
