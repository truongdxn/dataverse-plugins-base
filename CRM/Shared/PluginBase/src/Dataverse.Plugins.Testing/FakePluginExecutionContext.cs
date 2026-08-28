using System;
using Microsoft.Xrm.Sdk;

namespace Dataverse.Plugins.Testing
{
    /// <summary>
    /// An <see cref="IPluginExecutionContext"/> whose every property is settable, so a test can
    /// describe the exact pipeline position it wants to reproduce.
    /// <para>
    /// Prefer building it through <see cref="PluginTestHost"/> rather than by hand - the host
    /// keeps the parts that must agree in step, such as the target's id and
    /// <see cref="PrimaryEntityId"/>.
    /// </para>
    /// </summary>
    public class FakePluginExecutionContext : IPluginExecutionContext
    {
        public string MessageName { get; set; } = "Update";

        public string PrimaryEntityName { get; set; } = string.Empty;

        public Guid PrimaryEntityId { get; set; }

        public string SecondaryEntityName { get; set; } = string.Empty;

        /// <summary>10 PreValidation, 20 PreOperation, 40 PostOperation.</summary>
        public int Stage { get; set; } = 40;

        /// <summary>0 synchronous, 1 asynchronous.</summary>
        public int Mode { get; set; }

        /// <summary>1 None, 2 Sandbox.</summary>
        public int IsolationMode { get; set; } = 2;

        /// <summary>1 for a top level operation. Raise it to exercise a recursion guard.</summary>
        public int Depth { get; set; } = 1;

        public ParameterCollection InputParameters { get; set; } = new ParameterCollection();

        public ParameterCollection OutputParameters { get; set; } = new ParameterCollection();

        public ParameterCollection SharedVariables { get; set; } = new ParameterCollection();

        public EntityImageCollection PreEntityImages { get; set; } = new EntityImageCollection();

        public EntityImageCollection PostEntityImages { get; set; } = new EntityImageCollection();

        public Guid UserId { get; set; } = Guid.NewGuid();

        public Guid InitiatingUserId { get; set; } = Guid.NewGuid();

        public Guid BusinessUnitId { get; set; } = Guid.NewGuid();

        public Guid OrganizationId { get; set; } = Guid.NewGuid();

        public string OrganizationName { get; set; } = "fake";

        public Guid CorrelationId { get; set; } = Guid.NewGuid();

        public Guid? RequestId { get; set; } = Guid.NewGuid();

        public Guid OperationId { get; set; } = Guid.NewGuid();

        public DateTime OperationCreatedOn { get; set; } = DateTime.UtcNow;

        public bool IsExecutingOffline { get; set; }

        public bool IsOfflinePlayback { get; set; }

        public bool IsInTransaction { get; set; } = true;

        /// <summary>The SdkMessageProcessingStep the plugin is registered on.</summary>
        public EntityReference OwningExtension { get; set; } =
            new EntityReference("sdkmessageprocessingstep", Guid.NewGuid());

        /// <summary>The context of the operation that triggered this one, or null at the top.</summary>
        public IPluginExecutionContext ParentContext { get; set; }
    }
}
