// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Core
// Purpose: Handles Foxglove fetchAsset requests for FoxgloveSession.

using System;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Transport;

namespace Unity.FoxgloveSDK.Core
{
    internal sealed class SessionAssetHandler
    {
        private readonly Func<IRuntimeContext> _runtimeProvider;
        private readonly IFoxgloveTransport _transport;

        public SessionAssetHandler(Func<IRuntimeContext> runtimeProvider, IFoxgloveTransport transport)
        {
            _runtimeProvider = runtimeProvider ?? throw new ArgumentNullException(nameof(runtimeProvider));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public void Fetch(uint clientId, string json)
        {
            FetchAsset msg;
            try { msg = JsonConvert.DeserializeObject<FetchAsset>(json); }
            catch
            {
                _transport.SendBinary(clientId, BinaryEncoding.EncodeFetchAssetResponseError(0, "Malformed JSON"));
                return;
            }

            if (msg == null || string.IsNullOrWhiteSpace(msg.Uri))
            {
                _transport.SendBinary(clientId,
                    BinaryEncoding.EncodeFetchAssetResponseError(msg?.RequestId ?? 0, "Asset URI is required"));
                return;
            }

            var runtime = _runtimeProvider();
            if (runtime?.Assets == null || !runtime.Assets.HasRoots)
            {
                _transport.SendBinary(clientId, BinaryEncoding.EncodeFetchAssetResponseError(msg.RequestId, "No asset roots registered"));
                return;
            }

            if (runtime.Assets.TryRead(msg.Uri, out var data, out var error))
                _transport.SendBinary(clientId, BinaryEncoding.EncodeFetchAssetResponseSuccess(msg.RequestId, data));
            else
                _transport.SendBinary(clientId, BinaryEncoding.EncodeFetchAssetResponseError(msg.RequestId, error));
        }
    }
}
