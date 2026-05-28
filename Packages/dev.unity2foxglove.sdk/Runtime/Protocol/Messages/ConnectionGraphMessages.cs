// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol/Messages
// Purpose: Foxglove WebSocket connection graph DTOs.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>Client -> Server: subscribe to connection graph updates.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class SubscribeConnectionGraph
    {
        [JsonProperty("op")]
        public string Op => "subscribeConnectionGraph";
    }

    /// <summary>Client -> Server: unsubscribe from connection graph updates.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class UnsubscribeConnectionGraph
    {
        [JsonProperty("op")]
        public string Op => "unsubscribeConnectionGraph";
    }

    /// <summary>Server → Client: full connection graph snapshot.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class ConnectionGraphUpdate
    {
        [JsonProperty("op")]
        public string Op => "connectionGraphUpdate";

        /// <summary>Currently published topics.</summary>
        [JsonProperty("publishedTopics")]
        public List<PublishedTopic> PublishedTopics { get; set; } = new List<PublishedTopic>();

        /// <summary>Currently subscribed topics.</summary>
        [JsonProperty("subscribedTopics")]
        public List<SubscribedTopic> SubscribedTopics { get; set; } = new List<SubscribedTopic>();

        /// <summary>Currently advertised services.</summary>
        [JsonProperty("advertisedServices")]
        public List<AdvertisedService> AdvertisedServices { get; set; } = new List<AdvertisedService>();

        /// <summary>Topics to remove from the graph.</summary>
        [JsonProperty("removedTopics")]
        public List<string> RemovedTopics { get; set; } = new List<string>();

        /// <summary>Services to remove from the graph.</summary>
        [JsonProperty("removedServices")]
        public List<string> RemovedServices { get; set; } = new List<string>();
    }

    /// <summary>A published topic with its publisher IDs.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class PublishedTopic
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("publisherIds")]
        public List<string> PublisherIds { get; set; } = new List<string>();
    }

    /// <summary>A subscribed topic with its subscriber IDs.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class SubscribedTopic
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("subscriberIds")]
        public List<string> SubscriberIds { get; set; } = new List<string>();
    }

    /// <summary>An advertised service with its provider IDs.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class AdvertisedService
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("providerIds")]
        public List<string> ProviderIds { get; set; } = new List<string>();
    }
}
