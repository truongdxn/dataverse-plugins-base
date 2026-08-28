using System;
using Microsoft.Xrm.Sdk;

namespace Dataverse.Plugins.Abstractions.Runtime
{
    /// <summary>
    /// Everything a plugin needs from the execution pipeline, resolved once and exposed as
    /// properties instead of repeated <see cref="IServiceProvider"/> casts.
    /// </summary>
    public interface ILocalPluginContext
    {
        /// <summary>The raw pipeline context, for anything the helpers below do not cover.</summary>
        IPluginExecutionContext PluginExecutionContext { get; }

        /// <summary>Organization service running as the user the step is registered to run as.</summary>
        IOrganizationService OrganizationService { get; }

        /// <summary>
        /// Organization service running as SYSTEM. Bypasses the calling user's privileges, so
        /// reach for <see cref="OrganizationService"/> first and use this only deliberately.
        /// </summary>
        IOrganizationService SystemOrganizationService { get; }

        ITracingService TracingService { get; }

        /// <summary>The underlying provider, for services with no helper here.</summary>
        IServiceProvider ServiceProvider { get; }

        string MessageName { get; }
        string PrimaryEntityName { get; }
        Guid PrimaryEntityId { get; }

        /// <summary>Pipeline recursion depth. 1 for a top level operation.</summary>
        int Depth { get; }

        int Stage { get; }

        /// <summary>The user the plugin is executing as.</summary>
        Guid UserId { get; }

        /// <summary>The user who actually triggered the operation, before any impersonation.</summary>
        Guid InitiatingUserId { get; }

        /// <summary>Unsecure configuration from the step registration. Readable by any user - never secrets.</summary>
        string UnsecureConfiguration { get; }

        /// <summary>Secure configuration from the step registration.</summary>
        string SecureConfiguration { get; }

        /// <summary>Writes to the plugin trace log. Safe to call with no arguments to format.</summary>
        void Trace(string message, params object[] args);

        /// <summary>The Target as an entity, or null when the message carries a reference instead.</summary>
        Entity GetTarget();

        /// <summary>The Target as a reference, or null when the message carries an entity instead.</summary>
        EntityReference GetTargetReference();

        /// <summary>A registered pre-image, or null when it was not registered.</summary>
        Entity GetPreImage(string name = "PreImage");

        /// <summary>A registered post-image, or null when it was not registered.</summary>
        Entity GetPostImage(string name = "PostImage");

        /// <summary>
        /// Target merged over the pre-image, giving the full row as it will look after the
        /// operation. Requires a pre-image to be registered to be meaningful on Update.
        /// </summary>
        Entity GetMergedTarget(string preImageName = "PreImage");

        T GetInputParameter<T>(string name);
        T GetOutputParameter<T>(string name);
        void SetOutputParameter(string name, object value);
        T GetSharedVariable<T>(string name);
        void SetSharedVariable(string name, object value);
    }
}
