using System;

namespace Dataverse.Plugins.Abstractions.Registration
{
    /// <summary>
    /// Declares one plugin step registration on a plugin class. Apply it once per step; a class
    /// handling several messages carries several attributes.
    /// <para>
    /// This attribute is the single source of truth for the step. The build tooling reads it to
    /// produce the manifest, the solution package and the direct registration - the Plugin
    /// Registration Tool is never needed.
    /// </para>
    /// <para>
    /// Prefer the generated schema constants over literal strings, so a typo is a build error
    /// rather than a step that deploys cleanly and never fires.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// [PluginStep(Messages.Update, Contact.EntityLogicalName,
    ///     Stage = Stage.PostOperation,
    ///     Order = 10,
    ///     FilteringAttributes = new[] { Contact.Fields.FirstName, Contact.Fields.LastName },
    ///     PreImage            = new[] { Contact.Fields.FirstName, Contact.Fields.LastName })]
    /// public class ContactPostUpdate : PluginBase { }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class PluginStepAttribute : Attribute
    {
        /// <param name="message">
        /// SDK message name, e.g. <c>Messages.Create</c>, or a custom API / custom action unique name.
        /// </param>
        /// <param name="primaryEntity">
        /// Logical name of the primary table, e.g. <c>Contact.EntityLogicalName</c>. Leave empty for
        /// a message registered globally (all tables), such as an unbound custom API.
        /// </param>
        public PluginStepAttribute(string message, string primaryEntity = "")
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("A plugin step must specify a message.", nameof(message));
            }

            Message = message;
            PrimaryEntity = primaryEntity ?? string.Empty;
        }

        /// <summary>SDK message name the step is registered against.</summary>
        public string Message { get; }

        /// <summary>Primary table logical name, or empty for a global registration.</summary>
        public string PrimaryEntity { get; }

        /// <summary>Secondary table logical name. Only meaningful for a few messages such as SetRelated.</summary>
        public string SecondaryEntity { get; set; }

        /// <summary>Pipeline stage. Defaults to <see cref="Stage.PostOperation"/>.</summary>
        public Stage Stage { get; set; } = Stage.PostOperation;

        /// <summary>Synchronous or asynchronous. Defaults to synchronous.</summary>
        public ExecutionMode Mode { get; set; } = ExecutionMode.Synchronous;

        /// <summary>Execution order (rank) within the stage. Defaults to 1.</summary>
        public int Order { get; set; } = 1;

        /// <summary>
        /// Step name. Leave unset to get a generated name of the form
        /// "Namespace.Type: Update of contact". Set it explicitly when the class declares more than
        /// one step for the same message and table, which is otherwise ambiguous.
        /// </summary>
        public string Name { get; set; }

        /// <summary>Free text description stored on the step.</summary>
        public string Description { get; set; }

        /// <summary>
        /// Column logical names that trigger the step. Applies to Update only; leaving it unset
        /// means "any column", which is almost always a performance mistake on Update.
        /// </summary>
        public string[] FilteringAttributes { get; set; }

        /// <summary>
        /// Registers a pre-image named "PreImage" carrying these columns, without needing a separate
        /// <see cref="PluginImageAttribute"/> - which means there is no step name to keep in sync.
        /// <para>
        /// Unset means no pre-image. An empty array means every column, which is worth avoiding on
        /// wide tables. Use <see cref="PluginImageAttribute"/> when you need a custom name or alias,
        /// <see cref="ImageType.Both"/>, or a message property other than Target.
        /// </para>
        /// </summary>
        public string[] PreImage { get; set; }

        /// <summary>
        /// Registers a post-image named "PostImage" carrying these columns. See
        /// <see cref="PreImage"/> for the semantics.
        /// </summary>
        public string[] PostImage { get; set; }

        /// <summary>
        /// User the step runs as. Accepts a systemuser domain name or Entra object id. Leave unset
        /// to run as the calling user.
        /// </summary>
        public string ImpersonatingUser { get; set; }

        /// <summary>Unsecure configuration passed to the plugin constructor. Never secrets - any user can read it.</summary>
        public string UnsecureConfiguration { get; set; }

        /// <summary>
        /// Name of the entry in the environment's secure-config store to bind as the secure
        /// configuration. The value itself is resolved at deploy time and never lives in source.
        /// </summary>
        public string SecureConfigurationKey { get; set; }

        /// <summary>State the step is created in. Defaults to enabled.</summary>
        public StepState State { get; set; } = StepState.Enabled;

        /// <summary>Delete the async system job when it completes successfully. Ignored for synchronous steps.</summary>
        public bool AsyncAutoDelete { get; set; }

        /// <summary>Deployment the step runs in. Defaults to server only.</summary>
        public Deployment SupportedDeployment { get; set; } = Deployment.ServerOnly;
    }
}
