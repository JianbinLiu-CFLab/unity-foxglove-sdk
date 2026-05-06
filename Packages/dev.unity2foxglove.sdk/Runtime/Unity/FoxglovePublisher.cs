// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Unity
// Purpose: Typed publisher base class. Subclasses implement CreateMessage()
// and the base handles rate throttling, schema resolution via
// [FoxgloveSchema] attribute, and JSON publish through FoxgloveManager.

using System;
using Newtonsoft.Json;
using Unity.FoxgloveSDK.Protocol;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Generic publisher base class with automatic schema binding and built-in Update loop.
    /// Subclasses provide CreateMessage(); the base handles FPS throttling, serialization, and publish.
    /// </summary>
    public abstract class FoxglovePublisher<TMessage> : FoxglovePublisherBase where TMessage : class, new()
    {
        private string _cachedSchemaName;

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

        protected virtual void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;

            var message = CreateMessage();
            if (message == null) return;

            var unixNs = CurrentLogTimeNs;
            _manager.PublishJson(_topic, SchemaName, message, unixNs);
        }
    }
}
