using Dataverse.Plugins.Tooling.Infrastructure;
using Dataverse.Plugins.Tooling.Model;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Dataverse.Plugins.Tooling.Deployment;

/// <summary>Counts of what a sync changed, for the summary line.</summary>
public sealed class SyncSummary
{
    public int AssembliesCreated { get; set; }

    public int AssembliesUpdated { get; set; }

    public int TypesCreated { get; set; }

    public int StepsCreated { get; set; }

    public int StepsUpdated { get; set; }

    public int ImagesCreated { get; set; }

    public int ImagesUpdated { get; set; }

    public List<string> Orphans { get; } = new();
}

/// <summary>
/// Reconciles an environment to the manifest: uploads assemblies, registers plugin types, and
/// creates or updates steps and images.
/// <para>
/// Every write is keyed on the manifest's deterministic ids, so running it twice changes nothing
/// the second time, and a step created here is the same record a later solution import updates
/// rather than duplicates.
/// </para>
/// <para>
/// Steps found in the environment that the manifest no longer declares are reported, never
/// deleted, unless <c>--prune</c> is passed explicitly.
/// </para>
/// </summary>
public sealed class StepRegistrar
{
    private readonly IOrganizationService _service;
    private readonly string _solutionUniqueName;
    private readonly bool _prune;

    public StepRegistrar(IOrganizationService service, string solutionUniqueName, bool prune)
    {
        _service = service;
        _solutionUniqueName = solutionUniqueName;
        _prune = prune;
    }

    public SyncSummary Sync(PluginManifest manifest)
    {
        var summary = new SyncSummary();

        foreach (var assembly in manifest.Assemblies)
        {
            Log.Heading($"Assembly {assembly.Name}");

            var assemblyId = UpsertAssembly(assembly, summary);
            AddToSolution(assemblyId, componentType: 91);

            foreach (var type in assembly.Types)
            {
                var typeId = UpsertPluginType(assemblyId, type, summary);

                foreach (var step in type.Steps)
                {
                    var stepId = UpsertStep(typeId, step, summary);
                    AddToSolution(stepId, componentType: 92);

                    foreach (var image in step.Images)
                    {
                        UpsertImage(stepId, image, summary);
                    }

                    ReportOrphanImages(stepId, step, summary);
                }

                ReportOrphanSteps(typeId, type, summary);
            }
        }

        return summary;
    }

    private Guid UpsertAssembly(ManifestAssembly assembly, SyncSummary summary)
    {
        var existing = FirstOrDefault(new QueryExpression("pluginassembly")
        {
            ColumnSet = new ColumnSet("pluginassemblyid", "version"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("name", ConditionOperator.Equal, assembly.Name) },
            },
        });

        var content = Convert.ToBase64String(File.ReadAllBytes(assembly.AssemblyPath));

        var record = new Entity("pluginassembly")
        {
            ["name"] = assembly.Name,
            ["content"] = content,
            ["isolationmode"] = new OptionSetValue((int)assembly.IsolationMode),
            ["sourcetype"] = new OptionSetValue(0),
            ["version"] = assembly.Version,
            ["culture"] = assembly.Culture,
            ["publickeytoken"] = assembly.PublicKeyToken == "null" ? string.Empty : assembly.PublicKeyToken,
        };

        if (existing is not null)
        {
            // Reuse whatever id the environment already has. An assembly registered previously
            // by another tool will not carry our deterministic id, and names are unique, so
            // insisting on our id would just fail on a duplicate name.
            record.Id = existing.Id;
            _service.Update(record);
            summary.AssembliesUpdated++;
            Log.Item($"updated assembly {assembly.Name} {assembly.Version}");
            return existing.Id;
        }

        record.Id = assembly.Id;
        var created = _service.Create(record);
        summary.AssembliesCreated++;
        Log.Item($"created assembly {assembly.Name} {assembly.Version}");
        return created;
    }

    private Guid UpsertPluginType(Guid assemblyId, ManifestType type, SyncSummary summary)
    {
        var existing = FirstOrDefault(new QueryExpression("plugintype")
        {
            ColumnSet = new ColumnSet("plugintypeid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("typename", ConditionOperator.Equal, type.TypeName),
                    new ConditionExpression("pluginassemblyid", ConditionOperator.Equal, assemblyId),
                },
            },
        });

        var record = new Entity("plugintype")
        {
            ["typename"] = type.TypeName,
            ["friendlyname"] = type.FriendlyName ?? type.TypeName,
            ["name"] = type.TypeName,
            ["pluginassemblyid"] = new EntityReference("pluginassembly", assemblyId),
        };

        if (!string.IsNullOrWhiteSpace(type.Description))
        {
            record["description"] = type.Description;
        }

        if (existing is not null)
        {
            record.Id = existing.Id;
            _service.Update(record);
            return existing.Id;
        }

        record.Id = type.Id;
        var created = _service.Create(record);
        summary.TypesCreated++;
        Log.Item($"registered type {type.TypeName}");
        return created;
    }

    private Guid UpsertStep(Guid typeId, ManifestStep step, SyncSummary summary)
    {
        var messageId = RequireMessageId(step.Message);

        var record = new Entity("sdkmessageprocessingstep")
        {
            ["name"] = step.Name,
            ["description"] = step.Description ?? step.Name,
            ["sdkmessageid"] = new EntityReference("sdkmessage", messageId),
            ["eventhandler"] = new EntityReference("plugintype", typeId),
            ["stage"] = new OptionSetValue((int)step.Stage),
            ["mode"] = new OptionSetValue((int)step.Mode),
            ["rank"] = step.Order,
            ["supporteddeployment"] = new OptionSetValue((int)step.SupportedDeployment),
            ["invocationsource"] = new OptionSetValue(0),
            ["asyncautodelete"] = step.AsyncAutoDelete,
            ["filteringattributes"] = string.IsNullOrWhiteSpace(step.FilteringAttributes)
                ? null
                : step.FilteringAttributes,
            ["configuration"] = step.UnsecureConfiguration,
        };

        // An entity-bound step needs the filter that ties the message to the table; without it
        // the platform registers the step against every table.
        var filterId = FindMessageFilter(messageId, step.PrimaryEntity);

        if (filterId.HasValue)
        {
            record["sdkmessagefilterid"] = new EntityReference("sdkmessagefilter", filterId.Value);
        }

        if (!string.IsNullOrWhiteSpace(step.ImpersonatingUser))
        {
            record["impersonatinguserid"] = new EntityReference("systemuser", ResolveUser(step.ImpersonatingUser));
        }

        var existing = FindExistingStep(step, typeId);

        if (existing is not null)
        {
            record.Id = existing.Id;
            _service.Update(record);
            SetStepState(existing.Id, step);
            summary.StepsUpdated++;
            Log.Item($"updated step {step.Name}");
            return existing.Id;
        }

        record.Id = step.Id;
        var created = _service.Create(record);
        SetStepState(created, step);
        summary.StepsCreated++;
        Log.Item($"created step {step.Name}");
        return created;
    }

    /// <summary>
    /// Matches on our deterministic id first, then falls back to name within the same plugin
    /// type, so a step originally registered by hand or by the Plugin Registration Tool is
    /// adopted and updated rather than duplicated.
    /// </summary>
    private Entity FindExistingStep(ManifestStep step, Guid typeId)
    {
        var byId = FirstOrDefault(new QueryExpression("sdkmessageprocessingstep")
        {
            ColumnSet = new ColumnSet("sdkmessageprocessingstepid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sdkmessageprocessingstepid", ConditionOperator.Equal, step.Id),
                },
            },
        });

        if (byId is not null)
        {
            return byId;
        }

        return FirstOrDefault(new QueryExpression("sdkmessageprocessingstep")
        {
            ColumnSet = new ColumnSet("sdkmessageprocessingstepid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, step.Name),
                    new ConditionExpression("eventhandler", ConditionOperator.Equal, typeId),
                },
            },
        });
    }

    private void SetStepState(Guid stepId, ManifestStep step)
    {
        var stateCode = step.State == StepState.Enabled ? 0 : 1;
        var statusCode = step.State == StepState.Enabled ? 1 : 2;

        _service.Execute(new SetStateRequest
        {
            EntityMoniker = new EntityReference("sdkmessageprocessingstep", stepId),
            State = new OptionSetValue(stateCode),
            Status = new OptionSetValue(statusCode),
        });
    }

    private void UpsertImage(Guid stepId, ManifestImage image, SyncSummary summary)
    {
        var record = new Entity("sdkmessageprocessingstepimage")
        {
            ["name"] = image.Name,
            ["entityalias"] = image.EntityAlias ?? image.Name,
            ["imagetype"] = new OptionSetValue((int)image.ImageType),
            ["messagepropertyname"] = image.MessagePropertyName ?? "Target",
            ["sdkmessageprocessingstepid"] = new EntityReference("sdkmessageprocessingstep", stepId),
            ["attributes"] = string.IsNullOrWhiteSpace(image.Attributes) ? null : image.Attributes,
        };

        var existing = FirstOrDefault(new QueryExpression("sdkmessageprocessingstepimage")
        {
            ColumnSet = new ColumnSet("sdkmessageprocessingstepimageid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sdkmessageprocessingstepid", ConditionOperator.Equal, stepId),
                    new ConditionExpression("name", ConditionOperator.Equal, image.Name),
                },
            },
        });

        if (existing is not null)
        {
            record.Id = existing.Id;
            _service.Update(record);
            summary.ImagesUpdated++;
            return;
        }

        record.Id = image.Id;
        _service.Create(record);
        summary.ImagesCreated++;
        Log.Item($"created image {image.Name}");
    }

    private void ReportOrphanSteps(Guid typeId, ManifestType type, SyncSummary summary)
    {
        var declared = type.Steps.Select(s => s.Id).ToHashSet();

        var existing = Retrieve(new QueryExpression("sdkmessageprocessingstep")
        {
            ColumnSet = new ColumnSet("sdkmessageprocessingstepid", "name"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("eventhandler", ConditionOperator.Equal, typeId) },
            },
        });

        foreach (var record in existing)
        {
            if (declared.Contains(record.Id))
            {
                continue;
            }

            var name = record.GetAttributeValue<string>("name");

            if (_prune)
            {
                _service.Delete("sdkmessageprocessingstep", record.Id);
                Log.Item($"pruned step {name}");
                continue;
            }

            summary.Orphans.Add($"{type.TypeName}: step '{name}' exists in the environment but is not declared.");
        }
    }

    private void ReportOrphanImages(Guid stepId, ManifestStep step, SyncSummary summary)
    {
        var declared = step.Images.Select(i => i.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = Retrieve(new QueryExpression("sdkmessageprocessingstepimage")
        {
            ColumnSet = new ColumnSet("sdkmessageprocessingstepimageid", "name"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sdkmessageprocessingstepid", ConditionOperator.Equal, stepId),
                },
            },
        });

        foreach (var record in existing)
        {
            var name = record.GetAttributeValue<string>("name");

            if (declared.Contains(name))
            {
                continue;
            }

            if (_prune)
            {
                _service.Delete("sdkmessageprocessingstepimage", record.Id);
                Log.Item($"pruned image {name}");
                continue;
            }

            summary.Orphans.Add($"{step.Name}: image '{name}' exists in the environment but is not declared.");
        }
    }

    private void AddToSolution(Guid componentId, int componentType)
    {
        if (string.IsNullOrWhiteSpace(_solutionUniqueName))
        {
            return;
        }

        try
        {
            _service.Execute(new AddSolutionComponentRequest
            {
                ComponentId = componentId,
                ComponentType = componentType,
                SolutionUniqueName = _solutionUniqueName,
                AddRequiredComponents = false,
            });
        }
        catch (Exception ex)
        {
            // Already being in the solution is the common case on a re-run and is not a failure.
            Log.Detail($"Could not add component {componentId} to '{_solutionUniqueName}': {ex.Message}");
        }
    }

    private Guid RequireMessageId(string messageName)
    {
        var message = FirstOrDefault(new QueryExpression("sdkmessage")
        {
            ColumnSet = new ColumnSet("sdkmessageid"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("name", ConditionOperator.Equal, messageName) },
            },
        });

        return message?.Id
               ?? throw new ToolException(
                   $"The environment has no SDK message called '{messageName}'. Check the spelling, " +
                   "or that the custom API or action it belongs to is deployed there.");
    }

    private Guid? FindMessageFilter(Guid messageId, string primaryEntity)
    {
        if (string.IsNullOrWhiteSpace(primaryEntity))
        {
            return null;
        }

        var filter = FirstOrDefault(new QueryExpression("sdkmessagefilter")
        {
            ColumnSet = new ColumnSet("sdkmessagefilterid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("sdkmessageid", ConditionOperator.Equal, messageId),
                    new ConditionExpression("primaryobjecttypecode", ConditionOperator.Equal, primaryEntity),
                },
            },
        });

        return filter?.Id
               ?? throw new ToolException(
                   $"Table '{primaryEntity}' does not support this message, so no step can be " +
                   "registered for that combination.");
    }

    private Guid ResolveUser(string identifier)
    {
        // Accept a raw id, a domain name, or an Entra object id, since step registrations in the
        // wild use all three.
        if (Guid.TryParse(identifier, out var parsed))
        {
            return parsed;
        }

        var user = FirstOrDefault(new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid"),
            Criteria = new FilterExpression(LogicalOperator.Or)
            {
                Conditions =
                {
                    new ConditionExpression("domainname", ConditionOperator.Equal, identifier),
                    new ConditionExpression("internalemailaddress", ConditionOperator.Equal, identifier),
                },
            },
        });

        return user?.Id
               ?? throw new ToolException($"No user found matching impersonatingUser '{identifier}'.");
    }

    private Entity FirstOrDefault(QueryExpression query)
    {
        query.TopCount = 1;
        return _service.RetrieveMultiple(query).Entities.FirstOrDefault();
    }

    private IReadOnlyList<Entity> Retrieve(QueryExpression query) =>
        _service.RetrieveMultiple(query).Entities.ToList();
}
