// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0

using Google.Protobuf;
using Unity.FoxgloveSDK.Components;
using UnityEngine;

namespace Foxglove.Components
{
    /// <summary>
    /// Typed protobuf publisher MonoBehaviour. Subclasses implement CreateMessage()
    /// and the base handles rate throttling, schema resolution, and protobuf publish
    /// through FoxgloveManager.
    /// </summary>
    /// <typeparam name="T">Protobuf message type implementing IMessage.</typeparam>
    public abstract class ProtobufPublisher<T> : FoxglovePublisherBase where T : class, IMessage, new()
    {
        public override bool SupportsJsonEncoding => false;
        public override bool SupportsProtobufEncoding => true;

        /// <summary>
        /// Foxglove schema name for this protobuf message type.
        /// Converts C# namespace (e.g. "Foxglove.FrameTransform") to proto package
        /// convention ("foxglove.FrameTransform"). C# generated classes use PascalCase
        /// namespace, while proto packages are lowercase.
        /// Override in subclass if auto-detection fails.
        /// </summary>
        protected override string SchemaName
        {
            get
            {
                var full = typeof(T).FullName;
                // Convert leading C# namespace segment to lowercase proto package.
                // e.g. "Foxglove.FrameTransform" -> "foxglove.FrameTransform"
                var dot = full.IndexOf('.');
                if (dot < 0) return full.ToLowerInvariant();
                return full.Substring(0, dot).ToLowerInvariant() + full.Substring(dot);
            }
        }

        /// <summary>Called at publish time. Subclass builds the protobuf message object.</summary>
        protected abstract T CreateMessage();

        protected virtual void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;

            var message = CreateMessage();
            if (message == null) return;

            var unixNs = CurrentLogTimeNs;
            PublishProto(message.ToByteArray(), unixNs);
        }
    }
}
