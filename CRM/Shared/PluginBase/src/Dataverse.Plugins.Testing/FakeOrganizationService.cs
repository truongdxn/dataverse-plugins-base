using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Dataverse.Plugins.Testing
{
    /// <summary>
    /// An in-memory <see cref="IOrganizationService"/>: rows live in a dictionary, and
    /// Create/Retrieve/Update/Delete really do what they say.
    /// <para>
    /// It is deliberately small. <see cref="RetrieveMultiple"/> understands a QueryExpression with
    /// plain equality-style conditions and nothing else; <see cref="Execute"/> only knows the
    /// requests a test registers with <see cref="On{TRequest}"/>. Anything past that is a signal to
    /// pass a different <see cref="IOrganizationService"/> to
    /// <see cref="PluginTestHost.WithService"/> - a mocking library, or FakeXrmEasy - rather than
    /// to grow this class into a second Dataverse.
    /// </para>
    /// </summary>
    public class FakeOrganizationService : IOrganizationService
    {
        private readonly Dictionary<string, Entity> _rows =
            new Dictionary<string, Entity>(StringComparer.OrdinalIgnoreCase);

        private readonly Dictionary<Type, Func<OrganizationRequest, OrganizationResponse>> _handlers =
            new Dictionary<Type, Func<OrganizationRequest, OrganizationResponse>>();

        private readonly List<OrganizationRequest> _requests = new List<OrganizationRequest>();

        /// <summary>Every Execute call made, in order. Useful for asserting a request was issued.</summary>
        public IReadOnlyList<OrganizationRequest> Requests
        {
            get { return _requests; }
        }

        /// <summary>Every row currently stored, whatever its table.</summary>
        public IEnumerable<Entity> Rows
        {
            get { return _rows.Values; }
        }

        /// <summary>
        /// Seeds rows without going through <see cref="Create"/>, so arranging a test's starting
        /// state does not show up as something the plugin did.
        /// </summary>
        public FakeOrganizationService Seed(params Entity[] entities)
        {
            foreach (var entity in entities ?? new Entity[0])
            {
                if (entity.Id == Guid.Empty)
                {
                    entity.Id = Guid.NewGuid();
                }

                _rows[Key(entity.LogicalName, entity.Id)] = entity;
            }

            return this;
        }

        /// <summary>Handles one request type. A later registration replaces an earlier one.</summary>
        public FakeOrganizationService On<TRequest>(Func<TRequest, OrganizationResponse> handler)
            where TRequest : OrganizationRequest
        {
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }

            _handlers[typeof(TRequest)] = request => handler((TRequest)request);
            return this;
        }

        public Guid Create(Entity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("entity");
            }

            var id = entity.Id == Guid.Empty ? Guid.NewGuid() : entity.Id;
            var stored = Clone(entity);
            stored.Id = id;

            _rows[Key(entity.LogicalName, id)] = stored;
            return id;
        }

        public Entity Retrieve(string entityName, Guid id, ColumnSet columnSet)
        {
            Entity stored;

            if (!_rows.TryGetValue(Key(entityName, id), out stored))
            {
                // The real service faults rather than returning null, and a plugin that assumes
                // otherwise should fail here rather than at a NullReferenceException later.
                throw new InvalidOperationException(
                    string.Format("{0} with id {1} does not exist. Seed it first.", entityName, id));
            }

            return Project(stored, columnSet);
        }

        public void Update(Entity entity)
        {
            if (entity == null)
            {
                throw new ArgumentNullException("entity");
            }

            Entity stored;

            if (!_rows.TryGetValue(Key(entity.LogicalName, entity.Id), out stored))
            {
                throw new InvalidOperationException(
                    string.Format("{0} with id {1} does not exist. Seed it first.", entity.LogicalName, entity.Id));
            }

            // An update carries only the columns it changes, exactly as the real message does.
            foreach (var attribute in entity.Attributes)
            {
                stored[attribute.Key] = attribute.Value;
            }
        }

        public void Delete(string entityName, Guid id)
        {
            _rows.Remove(Key(entityName, id));
        }

        public EntityCollection RetrieveMultiple(QueryBase query)
        {
            var expression = query as QueryExpression;

            if (expression == null)
            {
                throw new NotSupportedException(
                    "FakeOrganizationService.RetrieveMultiple handles QueryExpression only. For " +
                    "FetchXml or anything richer, pass your own IOrganizationService to " +
                    "PluginTestHost.WithService.");
            }

            var matches = _rows.Values
                .Where(row => string.Equals(row.LogicalName, expression.EntityName, StringComparison.OrdinalIgnoreCase))
                .Where(row => Matches(row, expression.Criteria))
                .Select(row => Project(row, expression.ColumnSet))
                .ToList();

            return new EntityCollection(matches) { EntityName = expression.EntityName };
        }

        public OrganizationResponse Execute(OrganizationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException("request");
            }

            _requests.Add(request);

            Func<OrganizationRequest, OrganizationResponse> handler;

            if (_handlers.TryGetValue(request.GetType(), out handler))
            {
                return handler(request);
            }

            throw new NotSupportedException(
                string.Format(
                    "No handler registered for {0}. Register one with " +
                    "service.On<{0}>(request => ...) before running the plugin.",
                    request.GetType().Name));
        }

        public void Associate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities)
        {
            throw new NotSupportedException(
                "Associate is not modelled. Pass your own IOrganizationService to " +
                "PluginTestHost.WithService if the plugin depends on it.");
        }

        public void Disassociate(
            string entityName,
            Guid entityId,
            Relationship relationship,
            EntityReferenceCollection relatedEntities)
        {
            throw new NotSupportedException(
                "Disassociate is not modelled. Pass your own IOrganizationService to " +
                "PluginTestHost.WithService if the plugin depends on it.");
        }

        private static bool Matches(Entity row, FilterExpression filter)
        {
            if (filter == null)
            {
                return true;
            }

            var results = filter.Conditions
                .Select(condition => Matches(row, condition))
                .Concat(filter.Filters.Select(child => Matches(row, child)))
                .ToList();

            if (results.Count == 0)
            {
                return true;
            }

            return filter.FilterOperator == LogicalOperator.Or
                ? results.Any(result => result)
                : results.All(result => result);
        }

        private static bool Matches(Entity row, ConditionExpression condition)
        {
            var actual = row.Contains(condition.AttributeName) ? row[condition.AttributeName] : null;
            var expected = condition.Values.Count > 0 ? condition.Values[0] : null;

            switch (condition.Operator)
            {
                case ConditionOperator.Equal:
                    return AreEqual(actual, expected);
                case ConditionOperator.NotEqual:
                    return !AreEqual(actual, expected);
                case ConditionOperator.Null:
                    return actual == null;
                case ConditionOperator.NotNull:
                    return actual != null;
                case ConditionOperator.In:
                    return condition.Values.Any(value => AreEqual(actual, value));
                default:
                    throw new NotSupportedException(
                        string.Format(
                            "ConditionOperator.{0} is not modelled. Supported: Equal, NotEqual, " +
                            "Null, NotNull, In. Pass your own IOrganizationService for anything else.",
                            condition.Operator));
            }
        }

        private static bool AreEqual(object actual, object expected)
        {
            // A test writes a raw Guid where the row holds an EntityReference more often than not,
            // and failing that comparison silently is a confusing way to lose an hour.
            actual = Normalise(actual);
            expected = Normalise(expected);

            if (actual == null || expected == null)
            {
                return actual == null && expected == null;
            }

            return actual.Equals(expected);
        }

        private static object Normalise(object value)
        {
            var reference = value as EntityReference;
            if (reference != null)
            {
                return reference.Id;
            }

            var optionSet = value as OptionSetValue;
            if (optionSet != null)
            {
                return optionSet.Value;
            }

            var money = value as Money;
            if (money != null)
            {
                return money.Value;
            }

            return value;
        }

        private static Entity Project(Entity stored, ColumnSet columnSet)
        {
            var projected = new Entity(stored.LogicalName) { Id = stored.Id };

            foreach (var attribute in stored.Attributes)
            {
                if (columnSet == null || columnSet.AllColumns || columnSet.Columns.Contains(attribute.Key))
                {
                    projected[attribute.Key] = attribute.Value;
                }
            }

            return projected;
        }

        /// <summary>
        /// Stored rows are copies. Without this a plugin mutating the entity it passed to Create
        /// would retroactively change what was "saved", which no real service does.
        /// </summary>
        private static Entity Clone(Entity entity)
        {
            var copy = new Entity(entity.LogicalName) { Id = entity.Id };

            foreach (var attribute in entity.Attributes)
            {
                copy[attribute.Key] = attribute.Value;
            }

            return copy;
        }

        private static string Key(string entityName, Guid id)
        {
            return string.Concat(entityName, ":", id.ToString("N"));
        }
    }
}
