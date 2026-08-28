using System;
using Dataverse.Plugins.Abstractions.Registration;
using Dataverse.Plugins.Abstractions.Runtime;
using Dataverse.Plugins.Abstractions.Schema;
using Microsoft.Xrm.Sdk;

namespace Sample.Plugins
{
    /// <summary>
    /// Keeps a contact's description in sync with its name.
    /// <para>
    /// Shows the whole registration surface in one class: two steps on one plugin, each with its
    /// own image, and every table, column and message name coming from a generated constant so a
    /// typo is a build error rather than a step that deploys cleanly and never fires.
    /// </para>
    /// <para>
    /// Both steps carry an explicit <c>Name</c>. Two steps for the same message and table without
    /// one are indistinguishable, and the build rejects that.
    /// </para>
    /// </summary>
    [PluginStep(
        Messages.Create,
        Contact.EntityLogicalName,
        Stage = Stage.PostOperation,
        Order = 10,
        Name = CreateStepName,
        Description = "Sets the description on newly created contacts.",
        PostImage = new[] { Contact.Fields.FirstName, Contact.Fields.LastName })]
    [PluginStep(
        Messages.Update,
        Contact.EntityLogicalName,
        Stage = Stage.PostOperation,
        Order = 20,
        Name = UpdateStepName,
        Description = "Refreshes the description when either name part changes.",
        // Without this the step fires on every column change, including the one it makes itself.
        FilteringAttributes = new[] { Contact.Fields.FirstName, Contact.Fields.LastName },
        PreImage = new[] { Contact.Fields.FirstName, Contact.Fields.LastName })]
    public class ContactPostUpdate : PluginBase
    {
        private const string CreateStepName = "Contact create: set description";
        private const string UpdateStepName = "Contact update: set description";

        protected override void Execute(ILocalPluginContext context)
        {
            // Merging the target over the pre-image gives the row as it now stands, whichever of
            // the two name parts the caller actually supplied. On Create there is no pre-image and
            // the target is already complete.
            var contact = context.GetMergedTarget();

            if (contact == null)
            {
                return;
            }

            var firstName = contact.GetAttributeValue<string>(Contact.Fields.FirstName);
            var lastName = contact.GetAttributeValue<string>(Contact.Fields.LastName);
            var description = string.Join(" ", new[] { firstName, lastName })
                .Trim()
                .Replace("  ", " ");

            context.Trace("Setting description to '{0}'.", description);

            // Write only the computed column. Because the Update step filters on the name columns,
            // writing description here cannot re-trigger it.
            var update = new Entity(Contact.EntityLogicalName, context.PrimaryEntityId)
            {
                [Contact.Fields.Description] = description
            };

            context.OrganizationService.Update(update);
        }
    }
}
