using System;
using System.Collections.Generic;
using Microsoft.Xrm.Sdk;

namespace Dataverse.Plugins.Testing
{
    /// <summary>
    /// Runs a plugin the way Dataverse does: it is the <see cref="IServiceProvider"/> handed to
    /// <see cref="IPlugin.Execute(IServiceProvider)"/>, and it hands back the fakes afterwards so
    /// a test can assert on what happened.
    /// <para>
    /// <b>Why this talks only in Microsoft.Xrm.Sdk types.</b> The registration attributes and
    /// PluginBase are compiled INTO each plugin assembly as source, because the Dataverse sandbox
    /// resolves no dependent assemblies. Every plugin assembly therefore holds its own copy of
    /// <c>ILocalPluginContext</c>, and those copies are different CLR types even though the source
    /// is identical. A harness that referenced its own copy could not hand it to somebody else's
    /// plugin. <see cref="IServiceProvider"/> and the SDK interfaces are the one seam that is
    /// genuinely shared - the same seam the platform itself uses - so the harness stays on that
    /// side of the line and never references the abstractions at all.
    /// </para>
    /// </summary>
    /// <example>
    /// <code>
    /// var host = PluginTestHost.For("Update", "contact", Stages.PostOperation)
    ///     .WithTarget(target)
    ///     .WithPreImage("PreImage", pre);
    ///
    /// host.Execute(new ContactPostUpdate());
    ///
    /// Assert.True(host.Tracing.Contains("renamed"));
    /// </code>
    /// </example>
    public class PluginTestHost : IServiceProvider
    {
        private IOrganizationService _service;

        private PluginTestHost(FakePluginExecutionContext context)
        {
            Context = context;
            Tracing = new FakeTracingService();
            FakeService = new FakeOrganizationService();
            _service = FakeService;
        }

        /// <summary>The pipeline context. Settable in full for anything the builders do not cover.</summary>
        public FakePluginExecutionContext Context { get; private set; }

        /// <summary>Trace output the plugin produced.</summary>
        public FakeTracingService Tracing { get; private set; }

        /// <summary>
        /// The built-in in-memory service. Null-safe to read even after
        /// <see cref="WithService"/> replaced what the plugin sees.
        /// </summary>
        public FakeOrganizationService FakeService { get; private set; }

        /// <summary>What the plugin actually receives - the fake, unless one was substituted.</summary>
        public IOrganizationService Service
        {
            get { return _service; }
        }

        /// <summary>
        /// Ids the plugin asked <see cref="IOrganizationServiceFactory"/> to impersonate, in order.
        /// Null means SYSTEM. Lets a test prove a plugin used the elevated service deliberately.
        /// </summary>
        public IList<Guid?> ImpersonatedUsers { get; private set; }

        /// <summary>Starts a host for one message on one table.</summary>
        /// <param name="stage">10 PreValidation, 20 PreOperation, 40 PostOperation.</param>
        public static PluginTestHost For(string message, string entityLogicalName, int stage = 40)
        {
            var host = new PluginTestHost(new FakePluginExecutionContext
            {
                MessageName = message,
                PrimaryEntityName = entityLogicalName,
                Stage = stage,
            });

            host.ImpersonatedUsers = new List<Guid?>();
            return host;
        }

        /// <summary>
        /// Sets the Target input parameter. Also aligns <c>PrimaryEntityId</c> with the entity's
        /// own id, which the platform does and a hand-built context usually forgets - leaving a
        /// plugin that reads PrimaryEntityId looking broken when it is not.
        /// </summary>
        public PluginTestHost WithTarget(Entity target)
        {
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            Context.InputParameters["Target"] = target;

            if (target.Id != Guid.Empty)
            {
                Context.PrimaryEntityId = target.Id;
            }

            if (!string.IsNullOrEmpty(target.LogicalName))
            {
                Context.PrimaryEntityName = target.LogicalName;
            }

            return this;
        }

        /// <summary>Sets a Target that is a reference rather than an entity, as Delete carries.</summary>
        public PluginTestHost WithTargetReference(EntityReference target)
        {
            if (target == null)
            {
                throw new ArgumentNullException("target");
            }

            Context.InputParameters["Target"] = target;
            Context.PrimaryEntityId = target.Id;
            Context.PrimaryEntityName = target.LogicalName;
            return this;
        }

        public PluginTestHost WithPreImage(string name, Entity image)
        {
            Context.PreEntityImages[name] = image;
            return this;
        }

        public PluginTestHost WithPostImage(string name, Entity image)
        {
            Context.PostEntityImages[name] = image;
            return this;
        }

        public PluginTestHost WithInputParameter(string name, object value)
        {
            Context.InputParameters[name] = value;
            return this;
        }

        public PluginTestHost WithSharedVariable(string name, object value)
        {
            Context.SharedVariables[name] = value;
            return this;
        }

        /// <summary>Pipeline depth. Raise it to exercise a recursion guard.</summary>
        public PluginTestHost WithDepth(int depth)
        {
            Context.Depth = depth;
            return this;
        }

        public PluginTestHost WithUser(Guid userId)
        {
            Context.UserId = userId;
            Context.InitiatingUserId = userId;
            return this;
        }

        /// <summary>Seeds rows into the in-memory service. Shorthand for <c>host.FakeService.Seed(...)</c>.</summary>
        public PluginTestHost WithRows(params Entity[] entities)
        {
            FakeService.Seed(entities);
            return this;
        }

        /// <summary>
        /// Substitutes the organization service the plugin sees - a mock, or FakeXrmEasy - for
        /// anything the in-memory one deliberately does not model.
        /// </summary>
        public PluginTestHost WithService(IOrganizationService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException("service");
            }

            _service = service;
            return this;
        }

        /// <summary>
        /// Runs the plugin. Exceptions propagate untouched, so a test asserts on
        /// <c>InvalidPluginExecutionException</c> exactly as the platform would surface it.
        /// </summary>
        public PluginTestHost Execute(IPlugin plugin)
        {
            if (plugin == null)
            {
                throw new ArgumentNullException("plugin");
            }

            plugin.Execute(this);
            return this;
        }

        /// <summary>The Target as it stands after the plugin ran, for asserting on in-place edits.</summary>
        public Entity Target
        {
            get
            {
                return Context.InputParameters.Contains("Target")
                    ? Context.InputParameters["Target"] as Entity
                    : null;
            }
        }

        /// <summary>An output parameter the plugin set, or default when it set none.</summary>
        public T OutputParameter<T>(string name)
        {
            return Context.OutputParameters.Contains(name) && Context.OutputParameters[name] is T
                ? (T)Context.OutputParameters[name]
                : default(T);
        }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IPluginExecutionContext) || serviceType == typeof(IExecutionContext))
            {
                return Context;
            }

            if (serviceType == typeof(ITracingService))
            {
                return Tracing;
            }

            if (serviceType == typeof(IOrganizationServiceFactory))
            {
                return new Factory(this);
            }

            // The platform returns null for a service it does not offer rather than throwing,
            // and a plugin asking for something exotic should see the same thing here.
            return null;
        }

        private sealed class Factory : IOrganizationServiceFactory
        {
            private readonly PluginTestHost _host;

            public Factory(PluginTestHost host)
            {
                _host = host;
            }

            public IOrganizationService CreateOrganizationService(Guid? userId)
            {
                _host.ImpersonatedUsers.Add(userId);
                return _host._service;
            }
        }
    }
}
