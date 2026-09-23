using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        private const string ComponentPublishContractsServiceName = "/foxglove/component-publish-contracts";
        private uint _componentPublishContractsServiceId;
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
            foreach (var publisher in FindObjectsByType<FoxglovePublisherBase>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (publisher == null || publisher.ResolveManagerForComponentSession() != this || !publisher.HasValidTopic)
                    continue;
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
