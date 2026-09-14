// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Publishing
// Purpose: Typed publisher base class. Subclasses implement CreateMessage()
// and the base handles rate throttling, schema resolution via
// [FoxgloveSchema] attribute, and JSON publish through FoxgloveManager.

using System;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Components.Publishing.MessagePack;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Generic publisher base class with automatic schema binding and built-in Update loop.
    /// Subclasses provide CreateMessage(); the base handles FPS throttling, serialization, and publish.
    /// </summary>
    public abstract class FoxglovePublisher<TMessage> : FoxglovePublisherBase where TMessage : class, new()
    {
        private string _cachedSchemaName;
        private bool _warnedMissingMsgPackPayload;

        protected override string SchemaName
        {
            get
            {
                if (_cachedSchemaName == null)
                {
                    var attr = typeof(TMessage).GetCustomAttributes(typeof(FoxgloveSchemaAttribute), false);
                    _cachedSchemaName = attr.Length > 0 ? ((FoxgloveSchemaAttribute)attr[0]).SchemaName : "";
                }
                return _cachedSchemaName;
            }
        }

        /// <summary>Called at publish time. Subclass builds the message object.</summary>
        protected abstract TMessage CreateMessage();

        /// <summary>Legacy compatibility hook; generated registry codecs are authoritative.</summary>
        [Obsolete("Component MessagePack codecs are generated from FoxgloveSchema DTOs; override only for source compatibility.")]
        protected virtual byte[] CreateMsgPackPayload(TMessage message) => null;

        public override bool SupportsMsgPackEncoding
        {
            get
            {
                return ComponentMessagePackCodecRegistry.TryGet(typeof(TMessage), out var entry)
                    && entry.IsAvailable;
            }
        }

        protected virtual void Update()
        {
            if (!EnsureManagerAvailable()) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;
            if (!TryPreparePublishPayload(out var resolution)) return;

            var message = CreateMessage();
            if (message == null) return;

            var unixNs = CurrentLogTimeNs;
            if (resolution.Effective == PublisherEffectiveEncoding.MsgPack)
            {
                if (ComponentMessagePackCodecRegistry.TryGet(typeof(TMessage), out var generated)
                    && generated.IsAvailable)
                {
                    var payload = generated.Serialize(message);
                    _warnedMissingMsgPackPayload = false;
                    PublishMsgPack(payload, unixNs, resolution);
                }
                else if (!_warnedMissingMsgPackPayload)
                {
                    Debug.LogWarning(
                        $"[Foxglove] {GetType().Name} selected MsgPack encoding but did not provide a MessagePack payload; skipping publish.");
                    _warnedMissingMsgPackPayload = true;
                }
                return;
            }

            Publish(message, unixNs, resolution);
        }
    }
}
