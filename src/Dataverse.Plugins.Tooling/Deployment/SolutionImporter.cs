using Dataverse.Plugins.Tooling.Infrastructure;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;

namespace Dataverse.Plugins.Tooling.Deployment;

/// <summary>
/// Imports a packed solution zip. Uses the same connection as everything else, so CI needs only
/// the service-principal environment variables and no pac auth profile.
/// </summary>
public sealed class SolutionImporter
{
    private readonly IOrganizationService _service;

    public SolutionImporter(IOrganizationService service)
    {
        _service = service;
    }

    public void Import(string zipPath, bool publish, bool holdingSolution)
    {
        if (!File.Exists(zipPath))
        {
            throw new ToolException($"Solution package not found: {zipPath}");
        }

        var bytes = File.ReadAllBytes(zipPath);
        var importJobId = Guid.NewGuid();

        Log.Info($"Importing {Path.GetFileName(zipPath)} ({bytes.Length / 1024} KB)...");

        var request = new ImportSolutionRequest
        {
            CustomizationFile = bytes,
            ImportJobId = importJobId,
            // Existing customisations win only where the solution does not define them; plugin
            // steps we ship are ours to own.
            OverwriteUnmanagedCustomizations = true,
            PublishWorkflows = true,
            HoldingSolution = holdingSolution,
        };

        try
        {
            _service.Execute(request);
        }
        catch (Exception ex)
        {
            // The fault message is usually generic; the import job holds the real reason.
            var reason = TryReadFailureReason(importJobId);

            throw new ToolException(
                $"Solution import failed: {ex.Message}" +
                (reason is null ? string.Empty : $"{Environment.NewLine}{reason}"),
                ex);
        }

        Log.Success("Solution imported.");

        if (publish)
        {
            Log.Info("Publishing customisations...");
            _service.Execute(new PublishAllXmlRequest());
            Log.Success("Published.");
        }
    }

    private string TryReadFailureReason(Guid importJobId)
    {
        try
        {
            var job = _service.Retrieve("importjob", importJobId, new ColumnSet("data", "progress"));
            var data = job.GetAttributeValue<string>("data");

            if (string.IsNullOrWhiteSpace(data))
            {
                return null;
            }

            // The job data is a large XML document; surface the error text rather than all of it.
            var errors = System.Text.RegularExpressions.Regex
                .Matches(data, "errortext=\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct()
                .Take(10)
                .ToList();

            return errors.Count == 0
                ? null
                : "Import job reported:" + Environment.NewLine + "  - " + string.Join(Environment.NewLine + "  - ", errors);
        }
        catch
        {
            // Best effort only - the original exception is what matters.
            return null;
        }
    }
}
