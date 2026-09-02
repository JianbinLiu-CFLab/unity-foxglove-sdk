// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Manager-owned FoxRun subscription-contract directory service.

using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Protocol;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        private const string FoxRunSubscriptionCatalogServiceName = "/foxrun/subscription-contracts";
        private uint _foxRunSubscriptionCatalogServiceId;

        private void RegisterFoxRunSubscriptionCatalogService()
        {
            if (_foxRunSubscriptionCatalogServiceId != 0)
                return;

            _foxRunSubscriptionCatalogServiceId = RegisterService(
                new ServiceDescriptor
                {
                    Name = FoxRunSubscriptionCatalogServiceName,
                    Type = "unity2foxglove.foxrun.SubscriptionContracts",
                    Request = new ServiceSchemaDescriptor
                    {
                        Encoding = "json",
                        SchemaName = "unity2foxglove.foxrun.SubscriptionContractsRequest",
                        Schema = FoxRunSubscriptionCatalogServiceSchemas.Request
                    },
                    Response = new ServiceSchemaDescriptor
                    {
                        Encoding = "json",
                        SchemaName = "unity2foxglove.foxrun.SubscriptionContractsResponse",
                        Schema = FoxRunSubscriptionCatalogServiceSchemas.Response
                    }
                },
                HandleFoxRunSubscriptionCatalogRequest);
        }

        private void UnregisterFoxRunSubscriptionCatalogService()
        {
            if (_foxRunSubscriptionCatalogServiceId == 0)
                return;

            // Teardown may have already retired the active session while a
            // failed cleanup retry still owns the shared registry. Use the
            // runtime's cleanup-aware path so this manager-owned service can be
            // removed without admitting a new user mutation into that epoch.
            if (_runtime == null
                || _runtime.UnregisterServiceDuringCleanup(_foxRunSubscriptionCatalogServiceId))
            {
                _foxRunSubscriptionCatalogServiceId = 0;
            }
        }

        private JToken HandleFoxRunSubscriptionCatalogRequest(JToken request)
        {
            var objectRequest = request as JObject;
            var requestedTopic = objectRequest?.Value<string>("topic");
            var includeDescriptor = objectRequest?.Value<bool?>("includeDescriptor") == true;
            var subscriptionPolicy = ActiveFoxRunSubscriptionSessionPolicy;
            return FoxRunSubscriptionCatalog.BuildResponse(
                FoxRunSchemaInfoRegistry.Current,
                subscriptionPolicy.SubscriptionsEnabled && IsFoxRunInboundAuthorized,
                ActiveFoxRunPublishEncoding,
                subscriptionPolicy.WebSocketEncoding,
                subscriptionPolicy.DefaultProvider.Value,
                subscriptionPolicy.TransportAdmissionRateLimitHz,
                requestedTopic,
                includeDescriptor);
        }
    }

}
