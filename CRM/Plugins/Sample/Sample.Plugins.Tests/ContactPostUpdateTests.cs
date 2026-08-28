using System;
using Dataverse.Plugins.Abstractions.Schema;
using Dataverse.Plugins.Testing;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Xunit;

namespace Sample.Plugins.Tests
{
    /// <summary>
    /// The same plugin registered on two messages, so these tests double as the worked example for
    /// pre-images, post-operation writes, tracing, and the recursion guard.
    /// </summary>
    public class ContactPostUpdateTests
    {
        private static readonly Guid ContactId = new Guid("11111111-1111-1111-1111-111111111111");

        [Fact]
        public void OnUpdateTheDescriptionIsBuiltFromTheTargetMergedOverThePreImage()
        {
            // The point of the pre-image: the caller changed only the last name, but the
            // description still needs the first name, which only the pre-image carries.
            var host = Host()
                .WithTarget(ContactRow(lastName: "Tran"))
                .WithPreImage("PreImage", ContactRow(firstName: "Nhat Truong", lastName: "Dao"))
                .WithRows(ContactRow(firstName: "Nhat Truong", lastName: "Dao"))
                .Execute(new ContactPostUpdate());

            Assert.Equal("Nhat Truong Tran", Saved(host));
        }

        [Fact]
        public void WithNoPreImageOnlyWhatTheTargetCarriesIsUsed()
        {
            // How a Create step behaves: the target is already the whole row.
            var host = PluginTestHost
                .For(Messages.Create, Contact.EntityLogicalName, Stages.PostOperation)
                .WithTarget(ContactRow(firstName: "Nhat Truong", lastName: "Dao"))
                .WithRows(ContactRow())
                .Execute(new ContactPostUpdate());

            Assert.Equal("Nhat Truong Dao", Saved(host));
        }

        [Fact]
        public void OnlyTheComputedColumnIsWrittenBack()
        {
            // Writing the name columns back would re-trigger the step's own filter. Asserting the
            // update carried nothing else is what pins that down.
            var host = Host()
                .WithTarget(ContactRow(firstName: "Nhat Truong", lastName: "Dao"))
                .WithRows(ContactRow())
                .Execute(new ContactPostUpdate());

            var saved = host.FakeService.Retrieve(Contact.EntityLogicalName, ContactId, new ColumnSet(true));

            Assert.True(saved.Contains(Contact.Fields.Description));
            Assert.False(saved.Contains(Contact.Fields.FirstName));
            Assert.False(saved.Contains(Contact.Fields.LastName));
        }

        [Fact]
        public void WhatItDidIsTraced()
        {
            var host = Host()
                .WithTarget(ContactRow(firstName: "Nhat Truong", lastName: "Dao"))
                .WithRows(ContactRow())
                .Execute(new ContactPostUpdate());

            Assert.True(
                host.Tracing.Contains("Nhat Truong Dao"),
                "Expected the new description in the trace log, got:" + Environment.NewLine + host.Tracing.Text);
        }

        [Fact]
        public void ItRunsAsTheStepsUserRatherThanAsSystem()
        {
            // Reaching for the SYSTEM service bypasses the caller's privileges, so a plugin using
            // it should be doing so on purpose. null in this list means SYSTEM was requested.
            var user = Guid.NewGuid();

            var host = Host()
                .WithUser(user)
                .WithTarget(ContactRow(firstName: "Nhat Truong", lastName: "Dao"))
                .WithRows(ContactRow())
                .Execute(new ContactPostUpdate());

            Assert.Equal(new Guid?[] { user }, host.ImpersonatedUsers);
        }

        [Fact]
        public void PastMaxDepthItStopsInsteadOfRecursing()
        {
            // PluginBase's guard against plugin-triggers-plugin loops. Depth 9 is past the
            // default MaxDepth of 8, so nothing should be written at all.
            var host = Host()
                .WithDepth(9)
                .WithTarget(ContactRow(firstName: "Nhat Truong", lastName: "Dao"))
                .WithRows(ContactRow())
                .Execute(new ContactPostUpdate());

            var saved = host.FakeService.Retrieve(Contact.EntityLogicalName, ContactId, new ColumnSet(true));

            Assert.False(saved.Contains(Contact.Fields.Description));
            Assert.True(host.Tracing.Contains("skipping to break recursion"));
        }

        [Fact]
        public void AFailureFromDataverseIsSurfacedWithThePluginNamed()
        {
            // The row is never seeded, so the Update inside the plugin fails. PluginBase must turn
            // that into an InvalidPluginExecutionException naming itself, or the trace log is the
            // only clue about which plugin broke.
            var host = Host().WithTarget(ContactRow(firstName: "Nhat Truong", lastName: "Dao"));

            var exception = Assert.Throws<InvalidPluginExecutionException>(
                () => host.Execute(new ContactPostUpdate()));

            Assert.Contains(nameof(ContactPostUpdate), exception.Message);
        }

        private static PluginTestHost Host() =>
            PluginTestHost.For(Messages.Update, Contact.EntityLogicalName, Stages.PostOperation);

        private static Entity ContactRow(string firstName = null, string lastName = null)
        {
            var contact = new Entity(Contact.EntityLogicalName, ContactId);

            if (firstName != null)
            {
                contact[Contact.Fields.FirstName] = firstName;
            }

            if (lastName != null)
            {
                contact[Contact.Fields.LastName] = lastName;
            }

            return contact;
        }

        private static string Saved(PluginTestHost host) =>
            host.FakeService
                .Retrieve(Contact.EntityLogicalName, ContactId, new ColumnSet(true))
                .GetAttributeValue<string>(Contact.Fields.Description);
    }
}
