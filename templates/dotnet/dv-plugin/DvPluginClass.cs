using Dataverse.Plugins.Abstractions.Registration;
using Dataverse.Plugins.Abstractions.Runtime;
using Microsoft.Xrm.Sdk;

namespace DvPluginNamespace
{
    // Prefer generated constants over the literals below, so a typo is a build error:
    //   dv schema pull --env dev --tables DV_ENTITY
    // then  using Dataverse.Plugins.Abstractions.Schema;  and write
    //   [PluginStep(Messages.DV_MESSAGE, SomeTable.EntityLogicalName, ...)]
    [PluginStep(
        "DV_MESSAGE",
        "DV_ENTITY",
        Stage = Stage.DV_STAGE,
        Order = 1,
        Description = "TODO: describe what this step does.")]
    public class DvPluginClass : PluginBase
    {
        protected override void Execute(ILocalPluginContext context)
        {
            context.Trace(
                "{0}: {1} of {2}.",
                nameof(DvPluginClass),
                context.MessageName,
                context.PrimaryEntityName);

            // Target is an Entity on Create and Update, but an EntityReference on Delete -
            // use GetTargetReference() instead for Delete steps.
            var target = context.GetTarget();

            if (target == null)
            {
                return;
            }

            // TODO: implement. Throw InvalidPluginExecutionException to show the user a message.
        }
    }
}
