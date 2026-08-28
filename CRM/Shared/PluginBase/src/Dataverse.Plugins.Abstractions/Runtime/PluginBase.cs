using System;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;

namespace Dataverse.Plugins.Abstractions.Runtime
{
    /// <summary>
    /// Base class for plugins. Handles the boilerplate every plugin otherwise repeats: building
    /// the local context, guarding against runaway recursion, and turning exceptions into
    /// something the platform reports usefully.
    /// <para>
    /// Derive from it, add one or more <c>[PluginStep]</c> attributes, and implement
    /// <see cref="Execute(ILocalPluginContext)"/>.
    /// </para>
    /// </summary>
    public abstract class PluginBase : IPlugin
    {
        protected PluginBase()
        {
        }

        /// <summary>
        /// Constructor the platform calls when the step registration supplies configuration.
        /// Dataverse looks for this signature first and falls back to the parameterless one.
        /// </summary>
        protected PluginBase(string unsecureConfiguration, string secureConfiguration)
        {
            UnsecureConfiguration = unsecureConfiguration;
            SecureConfiguration = secureConfiguration;
        }

        /// <summary>Unsecure configuration from the step. Readable by any user - never put secrets here.</summary>
        protected string UnsecureConfiguration { get; }

        /// <summary>Secure configuration from the step.</summary>
        protected string SecureConfiguration { get; }

        /// <summary>
        /// Depth at which the plugin stops executing, to break plugin-triggers-plugin loops.
        /// Override to raise it only when the recursion is genuinely intended.
        /// </summary>
        protected virtual int MaxDepth => 8;

        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException(nameof(serviceProvider));
            }

            var context = new LocalPluginContext(serviceProvider, UnsecureConfiguration, SecureConfiguration);
            var pluginName = GetType().FullName;

            if (context.Depth > MaxDepth)
            {
                context.Trace(
                    "{0}: depth {1} exceeds MaxDepth {2}, skipping to break recursion.",
                    pluginName,
                    context.Depth,
                    MaxDepth);
                return;
            }

            try
            {
                Execute(context);
            }
            catch (InvalidPluginExecutionException)
            {
                // Already the platform's own error type, carrying a message meant for the user.
                // Wrapping it again would bury that message.
                throw;
            }
            catch (FaultException<OrganizationServiceFault> ex)
            {
                context.Trace("{0}: organization service fault: {1}", pluginName, ex);
                throw new InvalidPluginExecutionException(
                    $"{pluginName} failed calling Dataverse: {ex.Detail?.Message ?? ex.Message}",
                    ex);
            }
            catch (Exception ex)
            {
                // Trace before rethrowing: the trace log is usually the only place the original
                // stack trace survives.
                context.Trace("{0}: unhandled exception: {1}", pluginName, ex);
                throw new InvalidPluginExecutionException($"{pluginName} failed: {ex.Message}", ex);
            }
        }

        /// <summary>Plugin logic. Throw <see cref="InvalidPluginExecutionException"/> to surface a message to the user.</summary>
        protected abstract void Execute(ILocalPluginContext context);
    }
}
