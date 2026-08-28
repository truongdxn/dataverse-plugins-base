using Dataverse.Plugins.Abstractions.Registration;
using Dataverse.Plugins.Abstractions.Runtime;
using Microsoft.Xrm.Sdk;

namespace DvPluginAssembly
{
    /// <summary>
    /// Starter plugin. Rename it, or delete it once you have added your own.
    /// </summary>
    /// <remarks>
    /// Prefer generated constants over literal names. Run
    /// <c>dv schema pull --env dev --tables account</c>, add
    /// <c>using Dataverse.Plugins.Abstractions.Schema;</c>, and the table, columns and message all
    /// become compile-checked.
    /// <para>
    /// A step can carry its own images without a separate attribute, using
    /// <c>PreImage = new[] { ... }</c> or <c>PostImage = new[] { ... }</c>.
    /// </para>
    /// </remarks>
    [PluginStep(
        "Update",
        "account",
        Stage = Stage.PostOperation,
        Order = 1,
        // Without this the step fires on every column change.
        FilteringAttributes = new[] { "name" },
        Description = "Example step - change or remove.")]
    public class ExamplePlugin : PluginBase
    {
        protected override void Execute(ILocalPluginContext context)
        {
            var target = context.GetTarget();

            if (target == null)
            {
                return;
            }

            context.Trace("{0} ran for {1}.", nameof(ExamplePlugin), target.LogicalName);
        }
    }
}
