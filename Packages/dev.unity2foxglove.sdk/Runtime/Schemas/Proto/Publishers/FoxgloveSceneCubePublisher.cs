// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Schemas/Proto/Publishers
// Purpose: Publishes a SceneUpdate with a single cube entity representing this GameObject to Foxglove /scene.
// Supports JSON (default) and protobuf encoding.

using System.Collections.Generic;
using Google.Protobuf;
using Foxglove.Schemas;
using UnityEngine;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Schemas;
using Unity.FoxgloveSDK.Schemas.Ros2Msg;
using Google.Protobuf.WellKnownTypes;
using UVector3 = UnityEngine.Vector3;
using UColor = UnityEngine.Color;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes a SceneUpdate with a single cube entity representing this GameObject.
    /// Supports dual encoding: JSON (default) and protobuf.
    /// </summary>
    public class FoxgloveSceneCubePublisher : FoxglovePublisher<SceneUpdateMessage>
    {
        [SerializeField] private string _entityId = "";
        [SerializeField] private string _frameId = "";
        [SerializeField] private UVector3 _size = UVector3.one;
        [SerializeField] private UColor _color = UColor.green;
        private UnityEngine.Renderer _renderer;
        private UnityEngine.MaterialPropertyBlock _propertyBlock;

        public override bool SupportsProtobufEncoding => true;
        public override bool SupportsRos2Encoding => true;
        protected override string Ros2SchemaName => Ros2PublisherSchemaNames.SceneUpdate;

        public UColor SceneCubeColor
        {
            get => _color;
            set
            {
                if (_color == value)
                {
                    return;
                }
                _color = value;
                ApplyColorToRenderer(value);
                OnSceneCubeColorChanged?.Invoke(value);
            }
        }

        public event System.Action<UColor> OnSceneCubeColorChanged;

        private void ApplyColorToRenderer(UColor c)
        {
            EnsureRendererCache();
            if (_renderer != null)
            {
                _renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor("_BaseColor", c);
                _renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private void EnsureRendererCache()
        {
            if (_renderer == null)
                _renderer = GetComponent<UnityEngine.Renderer>();
            if (_propertyBlock == null)
                _propertyBlock = new UnityEngine.MaterialPropertyBlock();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    OnSceneCubeColorChanged?.Invoke(_color);
                    ApplyColorToRenderer(_color);
                }
            };
        }
#endif

        private FoxgloveTransformPublisher _transformPublisher;

        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/scene";
            EnsureRendererCache();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _transformPublisher = GetComponent<FoxgloveTransformPublisher>();
            EnsureRendererCache();
        }

        private string ResolvedEntityId =>
            SanitizeFrameId(_entityId, gameObject.name);

        private string ResolvedFrameId
        {
            get
            {
                if (!string.IsNullOrEmpty(_frameId))
                    return SanitizeFrameId(_frameId, gameObject.name);
                if (_transformPublisher != null)
                {
                    var child = _transformPublisher.ResolvedChildFrameId;
                    if (!string.IsNullOrEmpty(child)) return child;
                }
                return "unity_world";
            }
        }

        protected override void Update()
        {
            if (_manager == null) return;
            if (!_publishOnEnable) return;
            if (_manager.Runtime?.ReplayEnabled == true) return;
            if (!ShouldPublishNow()) return;
            if (!ShouldPrepareAnyPublishPayload()) return;

            var unixNs = CurrentLogTimeNs;
            var publishWebSocket = ShouldPreparePublishPayload();
            var publishBridge = ShouldPrepareRos2BridgePayload();
            var message = CreateMessage(unixNs);
            if (message == null) return;
            byte[] ros2Payload = null;

            if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Protobuf)
            {
                PublishProtobufSceneUpdate(unixNs);
            }
            else if (publishWebSocket && EffectiveEncoding == PublisherEffectiveEncoding.Ros2)
            {
                if (TryBuildRos2SceneUpdate(message, out ros2Payload))
                    PublishRos2(ros2Payload, unixNs);
            }
            else if (publishWebSocket)
            {
                Publish(message, unixNs);
            }

            if (publishBridge)
            {
                if (ros2Payload != null || TryBuildRos2SceneUpdate(message, out ros2Payload))
                    PublishRos2Bridge(ros2Payload, unixNs);
            }
        }

        protected override SceneUpdateMessage CreateMessage()
            => CreateMessage(CurrentLogTimeNs);

        private SceneUpdateMessage CreateMessage(ulong unixNs)
        {
            return new SceneUpdateMessage
            {
                Entities = new List<SceneEntity>
                {
                    new SceneEntity
                    {
                        Id = ResolvedEntityId,
                        FrameId = ResolvedFrameId,
                        Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(unixNs),
                        Lifetime = new FoxgloveDuration(),
                        Cubes = new List<CubePrimitive>
                        {
                            new CubePrimitive
                            {
                                Pose = new FoxglovePose
                                {
                                    Position = new FoxgloveVector3 { X = 0, Y = 0, Z = 0 },
                                    Orientation = new FoxgloveQuaternion { X = 0, Y = 0, Z = 0, W = 1 }
                                },
                                Size = new FoxgloveVector3 { X = _size.x, Y = _size.y, Z = _size.z },
                                Color = new FoxgloveColor { R = _color.r, G = _color.g, B = _color.b, A = _color.a }
                            }
                        }
                    }
                }
            };
        }

        private bool TryBuildRos2SceneUpdate(SceneUpdateMessage message, out byte[] payload)
        {
            payload = null;
            try
            {
                payload = Ros2CdrSceneUpdateBuilder.Serialize(message);
                return true;
            }
            catch (System.NotSupportedException ex)
            {
                Debug.LogWarning("[Foxglove] SceneUpdate ROS2 publish skipped: " + ex.Message);
                return false;
            }
        }

        private void PublishProtobufSceneUpdate(ulong unixNs)
        {
            var protoScene = new Foxglove.SceneUpdate
            {
                Entities =
                {
                    new Foxglove.SceneEntity
                    {
                        Id = ResolvedEntityId,
                        FrameId = ResolvedFrameId,
                        Timestamp = FoxgloveProtoBuilderUtil.ToTimestamp(unixNs),
                        Lifetime = new Duration(),
                        Cubes =
                        {
                            new Foxglove.CubePrimitive
                            {
                                Pose = new Foxglove.Pose
                                {
                                    Position = new Foxglove.Vector3 { X = 0, Y = 0, Z = 0 },
                                    Orientation = new Foxglove.Quaternion { X = 0, Y = 0, Z = 0, W = 1 }
                                },
                                Size = new Foxglove.Vector3 { X = (double)_size.x, Y = (double)_size.y, Z = (double)_size.z },
                                Color = new Foxglove.Color { R = (double)_color.r, G = (double)_color.g, B = (double)_color.b, A = (double)_color.a }
                            }
                        }
                    }
                }
            };

            PublishProto(protoScene.ToByteArray(), unixNs);
        }
    }
}
