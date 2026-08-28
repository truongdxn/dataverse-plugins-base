using Dataverse.Plugins.Abstractions.Schema;
using Dataverse.Plugins.Testing;
using Microsoft.Xrm.Sdk;
using Xunit;

namespace Sample.Plugins.Tests
{
    /// <summary>
    /// The shortest useful shape a plugin test takes: arrange a context, run the plugin, assert on
    /// what it did to the target. No connection, no environment, no registration.
    /// <para>
    /// Note that the Schema constants are usable here too - the test project references the plugin
    /// assembly, which compiled them in - so a test cannot drift from the columns the plugin uses.
    /// </para>
    /// </summary>
    public class AccountPreValidateCreateTests
    {
        [Fact]
        public void AnAccountWithNoNameIsRejectedWithAMessageTheUserWillSee()
        {
            var host = PluginTestHost
                .For(Messages.Create, Account.EntityLogicalName, Stages.PreValidation)
                .WithTarget(new Entity(Account.EntityLogicalName));

            // InvalidPluginExecutionException is the one type whose message reaches the user
            // rather than being swallowed into a generic platform error.
            var exception = Assert.Throws<InvalidPluginExecutionException>(
                () => host.Execute(new AccountPreValidateCreate()));

            Assert.Contains("must have a name", exception.Message);
        }

        [Fact]
        public void AWhitespaceOnlyNameIsRejectedToo()
        {
            var target = new Entity(Account.EntityLogicalName)
            {
                [Account.Fields.Name] = "   ",
            };

            var host = PluginTestHost
                .For(Messages.Create, Account.EntityLogicalName, Stages.PreValidation)
                .WithTarget(target);

            Assert.Throws<InvalidPluginExecutionException>(() => host.Execute(new AccountPreValidateCreate()));
        }

        [Fact]
        public void TheCategoryIsDefaultedOnTheTargetItself()
        {
            // Pre-validation runs before the write, so editing the target IS the way to change
            // what gets saved. Asserting on the target proves the plugin did not instead issue a
            // second Update - which would work, but at the cost of a whole extra round trip.
            var target = new Entity(Account.EntityLogicalName)
            {
                [Account.Fields.Name] = "Contoso",
            };

            var host = PluginTestHost
                .For(Messages.Create, Account.EntityLogicalName, Stages.PreValidation)
                .WithTarget(target)
                .Execute(new AccountPreValidateCreate());

            var category = host.Target.GetAttributeValue<OptionSetValue>(Account.Fields.AccountCategoryCode);

            Assert.NotNull(category);
            Assert.Equal(1, category.Value);
            Assert.Empty(host.FakeService.Rows);
        }

        [Fact]
        public void ACategoryTheCallerSuppliedIsLeftAlone()
        {
            var target = new Entity(Account.EntityLogicalName)
            {
                [Account.Fields.Name] = "Contoso",
                [Account.Fields.AccountCategoryCode] = new OptionSetValue(2),
            };

            var host = PluginTestHost
                .For(Messages.Create, Account.EntityLogicalName, Stages.PreValidation)
                .WithTarget(target)
                .Execute(new AccountPreValidateCreate());

            Assert.Equal(
                2,
                host.Target.GetAttributeValue<OptionSetValue>(Account.Fields.AccountCategoryCode).Value);
        }

        [Fact]
        public void AMessageWithNoTargetDoesNothingRatherThanThrow()
        {
            var host = PluginTestHost.For(Messages.Create, Account.EntityLogicalName, Stages.PreValidation);

            host.Execute(new AccountPreValidateCreate());

            Assert.Null(host.Target);
        }
    }
}
