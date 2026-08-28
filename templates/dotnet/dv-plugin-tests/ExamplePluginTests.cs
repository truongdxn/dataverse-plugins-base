using Dataverse.Plugins.Testing;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace DvPluginTests
{
    /// <summary>
    /// A plugin test runs the real plugin against a fake pipeline: no connection, no environment,
    /// no registration. PluginTestHost is the IServiceProvider Dataverse would have handed it.
    /// </summary>
    public class ExamplePluginTests
    {
        [Fact]
        public void TheHarnessSuppliesEverythingAPluginAsksFor()
        {
            var target = new Entity("account") { ["name"] = "Contoso" };

            var host = PluginTestHost
                .For("Create", "account", Stages.PreValidation)
                .WithTarget(target);

            // TODO: replace with your own plugin.
            //   host.Execute(new MyPlugin());
            //   Assert.Equal("...", host.Target.GetAttributeValue<string>("..."));
            //
            // Other things the host can arrange:
            //   .WithPreImage("PreImage", entity)   a registered pre-image
            //   .WithRows(entity, entity)           rows the plugin can Retrieve
            //   .WithDepth(9)                       to exercise a recursion guard
            //   .WithService(myMock)                when the in-memory service is not enough
            //
            // And assert on afterwards:
            //   host.Target            the target as the plugin left it
            //   host.FakeService       rows it created or updated
            //   host.Tracing.Text      everything it traced

            Assert.Equal("Contoso", host.Target.GetAttributeValue<string>("name"));
        }
    }
}
