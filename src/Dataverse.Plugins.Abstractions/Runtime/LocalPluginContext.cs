using System;
using Microsoft.Xrm.Sdk;

namespace Dataverse.Plugins.Abstractions.Runtime
{
    /// <summary>
    /// Default <see cref="ILocalPluginContext"/>. Services are resolved lazily so a plugin that
    /// never touches the organization service does not pay to create one.
    /// </summary>
    public class LocalPluginContext : ILocalPluginContext
    {
        private readonly IOrganizationServiceFactory _serviceFactory;
        private IOrganizationService _organizationService;
        private IOrganizationService _systemOrganizationService;

        public LocalPluginContext(
            IServiceProvider serviceProvider,
            string unsecureConfiguration = null,
            string secureConfiguration = null)
        {
            if (serviceProvider == null)
            {
                throw new ArgumentNullException(nameof(serviceProvider));
            }

            ServiceProvider = serviceProvider;
            PluginExecutionContext = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            TracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            _serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            UnsecureConfiguration = unsecureConfiguration;
            SecureConfiguration = secureConfiguration;
        }

        public IServiceProvider ServiceProvider { get; }

        public IPluginExecutionContext PluginExecutionContext { get; }

        public ITracingService TracingService { get; }

        public IOrganizationService OrganizationService =>
            _organizationService ?? (_organizationService = _serviceFactory.CreateOrganizationService(PluginExecutionContext.UserId));

        public IOrganizationService SystemOrganizationService =>
            _systemOrganizationService ?? (_systemOrganizationService = _serviceFactory.CreateOrganizationService(null));

        public string MessageName => PluginExecutionContext.MessageName;

        public string PrimaryEntityName => PluginExecutionContext.PrimaryEntityName;

        public Guid PrimaryEntityId => PluginExecutionContext.PrimaryEntityId;

        public int Depth => PluginExecutionContext.Depth;

        public int Stage => PluginExecutionContext.Stage;

        public Guid UserId => PluginExecutionContext.UserId;

        public Guid InitiatingUserId => PluginExecutionContext.InitiatingUserId;

        public string UnsecureConfiguration { get; }

        public string SecureConfiguration { get; }

        public void Trace(string message, params object[] args)
        {
            if (TracingService == null || string.IsNullOrEmpty(message))
            {
                return;
            }

            // Callers pass raw messages containing braces often enough (JSON, FetchXml) that an
            // unguarded Format would throw from inside tracing - the one place that must not throw.
            if (args == null || args.Length == 0)
            {
                TracingService.Trace("{0}", message);
                return;
            }

            try
            {
                TracingService.Trace(string.Format(message, args));
            }
            catch (FormatException)
            {
                TracingService.Trace("{0}", message);
            }
        }

        public Entity GetTarget() => GetInputParameter<Entity>("Target");

        public EntityReference GetTargetReference() => GetInputParameter<EntityReference>("Target");

        public Entity GetPreImage(string name = "PreImage") => GetImage(PluginExecutionContext.PreEntityImages, name);

        public Entity GetPostImage(string name = "PostImage") => GetImage(PluginExecutionContext.PostEntityImages, name);

        public Entity GetMergedTarget(string preImageName = "PreImage")
        {
            var target = GetTarget();
            var preImage = GetPreImage(preImageName);

            if (target == null)
            {
                return preImage;
            }

            if (preImage == null)
            {
                return target;
            }

            var merged = new Entity(target.LogicalName, target.Id);

            foreach (var attribute in preImage.Attributes)
            {
                merged[attribute.Key] = attribute.Value;
            }

            // Target last: it holds the incoming values and must win over the pre-image.
            foreach (var attribute in target.Attributes)
            {
                merged[attribute.Key] = attribute.Value;
            }

            return merged;
        }

        public T GetInputParameter<T>(string name) => GetFrom<T>(PluginExecutionContext.InputParameters, name);

        public T GetOutputParameter<T>(string name) => GetFrom<T>(PluginExecutionContext.OutputParameters, name);

        public void SetOutputParameter(string name, object value) => PluginExecutionContext.OutputParameters[name] = value;

        public T GetSharedVariable<T>(string name)
        {
            // A shared variable set in a pre-stage is readable in a post-stage only via the
            // parent context, so walk up rather than reporting it missing.
            var context = PluginExecutionContext;

            while (context != null)
            {
                if (context.SharedVariables != null &&
                    context.SharedVariables.Contains(name) &&
                    context.SharedVariables[name] is T typed)
                {
                    return typed;
                }

                context = context.ParentContext;
            }

            return default;
        }

        public void SetSharedVariable(string name, object value) => PluginExecutionContext.SharedVariables[name] = value;

        private static Entity GetImage(DataCollection<string, Entity> images, string name)
        {
            if (images == null || string.IsNullOrEmpty(name) || !images.Contains(name))
            {
                return null;
            }

            return images[name];
        }

        private static T GetFrom<T>(ParameterCollection parameters, string name)
        {
            if (parameters == null || !parameters.Contains(name))
            {
                return default;
            }

            return parameters[name] is T typed ? typed : default;
        }
    }
}
