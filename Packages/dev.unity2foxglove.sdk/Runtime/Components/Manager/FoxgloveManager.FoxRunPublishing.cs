// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Manager
// Purpose: Directional Provider-neutral FoxRun publish policy.

using System;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    public partial class FoxgloveManager
    {
        [SerializeField]
        private FoxRunEncoding _defaultFoxRunPublishEncoding =
            FoxRunEncoding.Protobuf;

        private readonly FoxRunPublishSessionState
            _foxRunPublishSessionState =
                new FoxRunPublishSessionState();

        public FoxRunPublishSessionPolicy
            ActiveFoxRunPublishSessionPolicy =>
                _foxRunPublishSessionState.Current;

        public event Action<FoxRunPublishSessionPolicy>
            FoxRunPublishSessionChanged;

        public FoxRunEncoding DefaultFoxRunPublishEncoding
        {
            get => _defaultFoxRunPublishEncoding
                   == (FoxRunEncoding)0
                ? FoxRunEncoding.Protobuf
                : FoxRunEncodingResolver
                    .ValidateProfileDefault(
                        _defaultFoxRunPublishEncoding);
            set => _defaultFoxRunPublishEncoding =
                FoxRunEncodingResolver
                    .ValidateProfileDefault(value);
        }

        public FoxRunEncoding ActiveFoxRunPublishEncoding =>
            ActiveFoxRunPublishSessionPolicy.SessionActive
                ? ActiveFoxRunPublishSessionPolicy
                    .WebSocketEncoding
                : DefaultFoxRunPublishEncoding;

        public float ActiveFoxRunDefaultPublishRateHz =>
            ActiveFoxRunPublishSessionPolicy.SessionActive
                ? ActiveFoxRunPublishSessionPolicy
                    .DefaultPublishRateHz
                : DefaultPublishRateHz;

        internal void BeginFoxRunPublishSessionIfNeeded()
        {
            if (_foxRunPublishSessionState.Current
                .SessionActive)
            {
                return;
            }

            var policy =
                _foxRunPublishSessionState.BeginIfNeeded(
                    ConfiguredFoxRunPublishTransportIds,
                    DefaultFoxRunPublishEncoding,
                    DefaultPublishRateHz,
                    FoxRunDeliveryPolicy
                        .ProviderDefault);
            NotifyFoxRunPublishSessionChanged(policy);
        }

        internal void EndFoxRunPublishSession()
        {
            try
            {
                if (_foxRunPublishSessionState.Current
                    .SessionActive)
                {
                    NotifyFoxRunPublishSessionChanged(
                        _foxRunPublishSessionState.End());
                }
            }
            finally
            {
                EndFoxRunTransportSession();
            }
        }

        private void NotifyFoxRunPublishSessionChanged(
            FoxRunPublishSessionPolicy policy)
        {
            var handlers =
                FoxRunPublishSessionChanged;
            if (handlers == null)
                return;
            foreach (var subscriber in
                     handlers.GetInvocationList())
            {
                try
                {
                    ((Action<FoxRunPublishSessionPolicy>)
                        subscriber)(policy);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
