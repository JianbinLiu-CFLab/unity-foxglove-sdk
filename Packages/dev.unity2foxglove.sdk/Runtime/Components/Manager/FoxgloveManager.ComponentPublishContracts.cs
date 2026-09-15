using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components.Publishing.Session;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        private const string ComponentPublishContractsServiceName = "/foxglove/component-publish-contracts";
        private uint _componentPublishContractsServiceId;
        private ComponentPublisherSessionSnapshot _activeComponentPublisherSession;

        /// <summary>Current immutable Component session used by observability clients.</summary>
        public ComponentPublisherSessionSnapshot ActiveComponentPublisherSession => _activeComponentPublisherSession;

        /// <summary>Publishes a newly frozen Component snapshot to the read-only service.</summary>
        internal void SetActiveComponentPublisherSession(ComponentPublisherSessionSnapshot snapshot)
            => _activeComponentPublisherSession = snapshot;

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
            return ComponentPublisherContractCatalog.BuildResponse(_activeComponentPublisherSession, topic, includeTypeShape);
        }
    }
}
