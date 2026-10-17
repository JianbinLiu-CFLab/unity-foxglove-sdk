// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/FoxRun
// Purpose: Provider-neutral publish transport selection and encoding.

using System;
using Unity.FoxgloveSDK.Util;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public sealed partial class FoxgloveLogHub : MonoBehaviour
    {
        private bool SelectsWebSocket(
            FoxgloveLogTopicInfo info)
        {
            var ids = info.PublishTransportIds;
            if (ids != null)
            {
                for (var index = 0;
                     index < ids.Count;
                     index++)
                {
                    if (string.Equals(
                            ids[index],
                            FoxgloveWebSocketTransport.Id,
                            StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }

            var manager = _manager;
            if (manager == null)
                return false;

            var active =
                manager.ActiveFoxRunPublishSessionPolicy;
            var inherited =
                active != null && active.SessionActive
                    ? active.PublishTransportIds
                    : manager
                        .ConfiguredFoxRunPublishTransportIds;
            for (var index = 0;
                 index < inherited.Count;
                 index++)
            {
                if (inherited[index]
                    == FoxgloveWebSocketTransport.TransportId)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasSelectedPublishProviders(
            FoxgloveLogTopicInfo info)
        {
            var ids = info.PublishTransportIds;
            if (ids != null)
                return ids.Count > 0;

            var manager = _manager;
            if (manager == null)
                return false;
            var active =
                manager.ActiveFoxRunPublishSessionPolicy;
            return active != null && active.SessionActive
                ? active.PublishTransportIds.Count > 0
                : manager
                    .ConfiguredFoxRunPublishTransportIds
                    .Count > 0;
        }

        private FoxRunEncoding ResolveWebSocketEncoding(
            FoxgloveLogTopicInfo info)
        {
            if (info.HasExplicitEncoding)
            {
                return FoxRunEncodingResolver
                    .ValidateProfileDefault(
                        info.DeclaredEncoding);
            }

            return _manager != null
                ? _manager.ActiveFoxRunPublishEncoding
                : FoxRunEncoding.Protobuf;
        }

        private FoxRunEncoding ResolveAdditiveSinkEncoding(
            FoxgloveLogTopicInfo info)
            => SelectsWebSocket(info)
               && ResolveWebSocketEncoding(info)
                   == FoxRunEncoding.MessagePack
                ? FoxRunEncoding.MessagePack
                : FoxRunEncoding.JSON;

    }
}
