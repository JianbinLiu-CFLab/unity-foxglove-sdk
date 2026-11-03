using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        private const string ComponentPublishContractsServiceName = "/foxglove/component-publish-contracts";
        private uint _componentPublishContractsServiceId;
        private const string TopicContractServiceName = "/foxglove/topic-contracts";
        private const string TopicContractServiceType = "unity2foxglove.topic.TopicContracts";
        private const string TopicContractRequestSchema =
            "{\"type\":\"object\",\"properties\":{\"topic\":{\"type\":\"string\"}}}";
        private const string TopicContractResponseSchema =
            "{\"type\":\"object\",\"properties\":{\"contractVersion\":{\"type\":\"integer\"},\"claimCount\":{\"type\":\"integer\"},\"conflictCount\":{\"type\":\"integer\"},\"responseTruncated\":{\"type\":\"boolean\"},\"claims\":{\"type\":\"array\",\"items\":{\"type\":\"object\"}},\"conflicts\":{\"type\":\"array\",\"items\":{\"type\":\"object\"}}}}";
        private uint _topicContractServiceId;
        private readonly TopicContractLedger _topicContractLedger = new TopicContractLedger();

        private readonly ComponentPublisherSessionLifecycleState<ComponentPublisherSessionSnapshot>
            _componentPublisherSessionState =
                new ComponentPublisherSessionLifecycleState<ComponentPublisherSessionSnapshot>();

        /// <summary>Current immutable Component session used by observability clients.</summary>
        public ComponentPublisherSessionSnapshot ActiveComponentPublisherSession
            => _componentPublisherSessionState.ActiveSession;

        /// <summary>Publishes a newly frozen Component snapshot to the read-only service.</summary>
        internal void SetActiveComponentPublisherSession(ComponentPublisherSessionSnapshot snapshot)
            => _componentPublisherSessionState.Set(snapshot);

        internal void ClearActiveComponentPublisherSession()
            => _componentPublisherSessionState.Clear();

        internal void RecordTopicContract(
            string topic,
            string schemaName,
            string encoding,
            string ownerKind,
            TopicContractScope scope = TopicContractScope.Live)
            => RecordTopicContract(
                topic,
                schemaName,
                encoding,
                ownerKind,
                string.Empty,
                string.Empty,
                string.Empty,
                scope);

        internal void RecordTopicContract(
            string topic,
            string schemaName,
            string encoding,
            string ownerKind,
            string shapeIdentity,
            TopicContractScope scope = TopicContractScope.Live)
            => RecordTopicContract(
                topic,
                schemaName,
                encoding,
                ownerKind,
                shapeIdentity,
                string.Empty,
                string.Empty,
                scope);

        internal void RecordTopicContract(
            string topic,
            string schemaName,
            string encoding,
            string ownerKind,
            string shapeIdentity,
            string schemaEncoding,
            string schemaFingerprint,
            TopicContractScope scope)
        {
            var result = _topicContractLedger.Register(
                new TopicContract(
                    topic,
                    schemaName,
                    encoding,
                    ownerKind,
                    shapeIdentity,
                    schemaEncoding,
                    schemaFingerprint,
                    scope));
            if (!result.ShouldWarn)
                return;

            Debug.LogWarning(
                "[Foxglove] Topic contract conflict for '"
                + BoundTopicContractValue(topic)
                + "': existing "
                + DescribeTopicContract(result.Existing)
                + "; incoming "
                + DescribeTopicContract(result.Incoming));
        }

        private static string DescribeTopicContract(TopicContract contract)
            => "schema=" + BoundTopicContractValue(contract.SchemaName)
               + ", encoding=" + BoundTopicContractValue(contract.Encoding)
               + ", owner=" + BoundTopicContractValue(contract.OwnerKind)
               + ", shape=" + BoundTopicContractValue(contract.ShapeIdentity)
               + ", schemaEncoding=" + BoundTopicContractValue(contract.SchemaEncoding)
               + ", schemaFingerprint=" + BoundTopicContractValue(contract.SchemaFingerprint)
               + ", scope=" + contract.Scope;

        private static string BoundTopicContractValue(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= 96)
                return value ?? string.Empty;
            return value.Substring(0, 96);
        }

        private void RegisterTopicContractService()
        {
            if (_topicContractServiceId != 0)
                return;

            _topicContractServiceId = RegisterService(
                new ServiceDescriptor
                {
                    Name = TopicContractServiceName,
                    Type = TopicContractServiceType,
                    Request = new ServiceSchemaDescriptor
                    {
                        Encoding = "json",
                        SchemaName = "unity2foxglove.topic.TopicContractsRequest",
                        Schema = TopicContractRequestSchema
                    },
                    Response = new ServiceSchemaDescriptor
                    {
                        Encoding = "json",
                        SchemaName = "unity2foxglove.topic.TopicContractsResponse",
                        Schema = TopicContractResponseSchema
                    }
                },
                HandleTopicContractRequest);
        }

        private void UnregisterTopicContractService()
        {
            if (_topicContractServiceId == 0)
                return;

            if (_runtime == null
                || _runtime.UnregisterServiceDuringCleanup(_topicContractServiceId))
            {
                _topicContractServiceId = 0;
            }
        }

        internal JToken HandleTopicContractRequest(JToken request)
        {
            var requestedTopic = (request as JObject)?.Value<string>("topic");
            var claims = new JArray();
            var responseTruncated = false;
            foreach (var claim in _topicContractLedger.Claims.Values)
            {
                if (!string.IsNullOrEmpty(requestedTopic)
                    && !string.Equals(requestedTopic, claim.Topic, StringComparison.Ordinal))
                    continue;
                if (claims.Count >= ComponentPublisherContractCatalogLimits.MaxResults)
                {
                    responseTruncated = true;
                    continue;
                }
                claims.Add(ToTopicContractJson(claim));
            }

            var conflicts = new JArray();
            foreach (var conflict in _topicContractLedger.Conflicts)
            {
                if (!string.IsNullOrEmpty(requestedTopic)
                    && !string.Equals(requestedTopic, conflict.Existing.Topic, StringComparison.Ordinal))
                    continue;
                if (conflicts.Count >= ComponentPublisherContractCatalogLimits.MaxResults)
                {
                    responseTruncated = true;
                    continue;
                }
                conflicts.Add(new JObject
                {
                    ["existing"] = ToTopicContractJson(conflict.Existing),
                    ["incoming"] = ToTopicContractJson(conflict.Incoming)
                });
            }

            var response = new JObject
            {
                ["contractVersion"] = 1,
                ["claimCount"] = claims.Count,
                ["conflictCount"] = conflicts.Count,
                ["responseTruncated"] = responseTruncated,
                ["claims"] = claims,
                ["conflicts"] = conflicts
            };
            if (Encoding.UTF8.GetByteCount(response.ToString(Newtonsoft.Json.Formatting.None))
                > ComponentPublisherContractCatalogLimits.MaxResponseBytes)
            {
                return new JObject
                {
                    ["contractVersion"] = 1,
                    ["claimCount"] = 0,
                    ["conflictCount"] = 0,
                    ["responseTruncated"] = true,
                    ["error"] = "response_limit_exceeded",
                    ["claims"] = new JArray(),
                    ["conflicts"] = new JArray()
                };
            }
            return response;
        }

        internal void ClearTopicContractLedger()
            => _topicContractLedger.Clear();

        private static JObject ToTopicContractJson(TopicContract contract)
            => new JObject
            {
                ["topic"] = contract.Topic,
                ["schemaName"] = contract.SchemaName,
                ["encoding"] = contract.Encoding,
                ["ownerKind"] = contract.OwnerKind,
                ["shapeIdentity"] = contract.ShapeIdentity,
                ["schemaEncoding"] = contract.SchemaEncoding,
                ["schemaFingerprint"] = contract.SchemaFingerprint,
                ["scope"] = contract.Scope.ToString()
            };

        internal bool TryGetActiveComponentPublisherSessionEntry(
            object publisher,
            out ComponentPublisherSessionEntry entry)
        {
            entry = null;
            return _componentPublisherSessionState.ActiveSession != null
                   && _componentPublisherSessionState.ActiveSession.TryGetEntry(publisher, out entry);
        }

        internal ComponentPublisherSessionSnapshot CaptureComponentPublisherSession(ulong generation)
        {
            var drafts = new List<ComponentPublisherContractDraft>();
            var publishers = ComponentPublisherSessionCaptureState.SelectOwned(
                FindObjectsByType<FoxglovePublisherBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None),
                publisher => publisher != null && publisher.ResolveManagerForComponentSession() == this,
                publisher => publisher != null && publisher.HasValidTopic);
            foreach (var publisher in publishers)
            {
                var resolution = publisher.EncodingResolution;
                var messagePackEntry = ComponentPublisherMessagePackEntryResolver.Resolve(
                    publisher.ComponentMessagePackMessageType,
                    resolution.Effective);
                drafts.Add(new ComponentPublisherContractDraft(
                    publisher,
                    publisher.GetInstanceID().ToString(System.Globalization.CultureInfo.InvariantCulture),
                    publisher.GetType(),
                    publisher.GetType().Name,
                    publisher.Topic,
                    publisher.ContractSchemaName,
                    resolution.Requested,
                    resolution.Effective,
                    messagePackEntry));
            }
            return new ComponentPublisherSessionBuilder().Build(generation, drafts);
        }

        private void RegisterComponentPublishContractsService()
        {
            if (_componentPublishContractsServiceId != 0) return;
            _componentPublishContractsServiceId = RegisterService(
                new ServiceDescriptor
                {
                    Name = ComponentPublishContractsServiceName,
                    Type = "unity2foxglove.component.PublishContracts",
                    Request = new ServiceSchemaDescriptor
                    {
                        Encoding = "json",
                        SchemaName = "unity2foxglove.component.PublishContractsRequest",
                        Schema = ComponentPublisherContractServiceSchemas.Request
                    },
                    Response = new ServiceSchemaDescriptor
                    {
                        Encoding = "json",
                        SchemaName = "unity2foxglove.component.PublishContractsResponse",
                        Schema = ComponentPublisherContractServiceSchemas.Response
                    }
                }, HandleComponentPublishContractsRequest);
        }

        private void UnregisterComponentPublishContractsService()
        {
            if (_componentPublishContractsServiceId == 0) return;
            if (_runtime == null || _runtime.UnregisterServiceDuringCleanup(_componentPublishContractsServiceId))
                _componentPublishContractsServiceId = 0;
        }

        private JToken HandleComponentPublishContractsRequest(JToken request)
        {
            var objectRequest = request as JObject;
            var topic = objectRequest?.Value<string>("topic");
            var includeTypeShape = objectRequest?.Value<bool?>("includeTypeShape") == true;
            return ComponentPublisherContractCatalog.BuildResponse(
                _componentPublisherSessionState.ActiveSession,
                topic,
                includeTypeShape);
        }
    }
}
