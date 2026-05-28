// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Protocol/Messages
// Purpose: Foxglove WebSocket subscribe/unsubscribe DTOs.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.FoxgloveSDK.Protocol
{
    /// <summary>Client -> Server: subscribe to channels.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class SubscribeMessage
    {
        [JsonProperty("op")]
        public string Op => "subscribe";

        [JsonProperty("subscriptions")]
        public List<Subscription> Subscriptions { get; set; } = new List<Subscription>();
    }

    /// <summary>
    /// A single subscription entry. "id" is the subscription ID assigned by the client;
    /// "channelId" is the channel the client wants to receive data from.
    /// Server must send MessageData using this subscription ID, not the channel ID.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class Subscription
    {
        /// <summary>Client-assigned subscription ID.</summary>
        [JsonProperty("id")]
        public uint Id { get; set; }

        /// <summary>Channel to subscribe to.</summary>
        [JsonProperty("channelId")]
        public uint ChannelId { get; set; }
    }

    /// <summary>Client -> Server: unsubscribe from channels.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class UnsubscribeMessage
    {
        [JsonProperty("op")]
        public string Op => "unsubscribe";

        [JsonProperty("subscriptionIds")]
        public List<uint> SubscriptionIds { get; set; } = new List<uint>();
    }
}
