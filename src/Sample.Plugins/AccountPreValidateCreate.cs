using System;
using Dataverse.Plugins.Abstractions.Registration;
using Dataverse.Plugins.Abstractions.Runtime;
using Dataverse.Plugins.Abstractions.Schema;
using Microsoft.Xrm.Sdk;

namespace Sample.Plugins
{
    /// <summary>
    /// Rejects accounts created without a name, and defaults the account category.
    /// <para>
    /// Registered in pre-validation deliberately: it runs before the database transaction opens,
    /// so rejecting here avoids the cost of rolling one back.
    /// </para>
    /// </summary>
    [PluginStep(
        Messages.Create,
        Account.EntityLogicalName,
        Stage = Stage.PreValidation,
        Order = 1,
        Description = "Rejects accounts with no name and defaults the category.")]
    public class AccountPreValidateCreate : PluginBase
    {
        private const int CategoryPreferredCustomer = 1;

        protected override void Execute(ILocalPluginContext context)
        {
            var target = context.GetTarget();

            if (target == null)
            {
                return;
            }

            var name = target.GetAttributeValue<string>(Account.Fields.Name);

            if (string.IsNullOrWhiteSpace(name))
            {
                // InvalidPluginExecutionException is the one exception type whose message is shown
                // to the user rather than swallowed into a generic platform error.
                throw new InvalidPluginExecutionException("An account must have a name.");
            }

            if (!target.Contains(Account.Fields.AccountCategoryCode))
            {
                target[Account.Fields.AccountCategoryCode] = new OptionSetValue(CategoryPreferredCustomer);
                context.Trace("Defaulted the account category for '{0}'.", name);
            }
        }
    }
}
