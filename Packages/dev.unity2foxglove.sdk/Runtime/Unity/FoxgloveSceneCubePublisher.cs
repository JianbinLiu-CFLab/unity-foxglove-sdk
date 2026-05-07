// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Unity
// Purpose: Publishes a SceneUpdate with a single cube entity representing this GameObject to Foxglove /scene.

using System.Collections.Generic;
using UnityEngine;
using Unity.FoxgloveSDK.Schemas;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Publishes a SceneUpdate with a single cube entity representing this GameObject.
    /// </summary>
    public class FoxgloveSceneCubePublisher : FoxglovePublisher<SceneUpdateMessage>
    {
        // ── Serialized fields ──
        /// <summary>Foxglove entity ID. Falls back to GameObject name if empty.</summary>
        [SerializeField] private string _entityId = "";
        /// <summary>Foxglove frame_id. Falls back to resolve chain or <c>"unity_world"</c>.</summary>
        [SerializeField] private string _frameId = "";
        /// <summary>Size of the cube in Foxglove meters.</summary>
        [SerializeField] private Vector3 _size = Vector3.one;
        /// <summary>Color of the cube in the Foxglove scene.</summary>
        [SerializeField] private Color _color = Color.green;

        /// <summary>
        /// Gets or sets the cube colour. Setting the value also applies it
        /// to the local Renderer and fires <see cref="OnSceneCubeColorChanged"/>.
        /// </summary>
        public Color SceneCubeColor
        {
            get => _color;
            set
            {
                if (_color == value)
                {
                    ApplyColorToRenderer(value);
                    return;
                }
                _color = value;
                ApplyColorToRenderer(value);
                OnSceneCubeColorChanged?.Invoke(value);
            }
        }

        /// <summary>Fired when SceneCubeColor changes (Inspector or Foxglove side). Subscribe to sync parameters.</summary>
        public event System.Action<Color> OnSceneCubeColorChanged;

        /// <summary>Writes <c>_BaseColor</c> to the Renderer's MaterialPropertyBlock.</summary>
        private void ApplyColorToRenderer(Color c)
        {
            var renderer = GetComponent<Renderer>();
            if (renderer != null)
            {
                var block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", c);
                renderer.SetPropertyBlock(block);
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only hook that fires <see cref="OnSceneCubeColorChanged"/>
        /// and re-applies the colour to the Renderer on the next editor tick.
        /// </summary>
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

        // ── Internal state ──
        /// <summary>Cached reference to the co-located FoxgloveTransformPublisher.</summary>
        private FoxgloveTransformPublisher _transformPublisher;

        /// <summary>Defaults the topic to <c>/scene</c> if not set.</summary>
        private void Awake()
        {
            if (string.IsNullOrEmpty(_topic)) _topic = "/scene";
        }

        /// <summary>Caches the FoxgloveTransformPublisher on the same GameObject.</summary>
        protected override void OnEnable()
        {
            base.OnEnable();
            _transformPublisher = GetComponent<FoxgloveTransformPublisher>();
        }

        /// <summary>Resolved entity ID, using GameObject name as fallback.</summary>
        private string ResolvedEntityId =>
            SanitizeFrameId(_entityId, gameObject.name);

        /// <summary>
        /// Resolved frame ID. Uses the explicit <c>_frameId</c> if set,
        /// otherwise chains through FoxgloveTransformPublisher, falling back to <c>"unity_world"</c>.
        /// </summary>
        private string ResolvedFrameId
        {
            get
            {
                if (!string.IsNullOrEmpty(_frameId)) return _frameId;
                if (_transformPublisher != null)
                {
                    var child = _transformPublisher.ResolvedChildFrameId;
                    if (!string.IsNullOrEmpty(child)) return child;
                }
                return "unity_world";
            }
        }

        /// <summary>
        /// Builds a <c>SceneUpdateMessage</c> containing a single cube entity
        /// with identity pose, configured size, and configured colour.
        /// </summary>
        protected override SceneUpdateMessage CreateMessage()
        {
            return new SceneUpdateMessage
            {
                Entities = new List<SceneEntity>
                {
                    new SceneEntity
                    {
                        Id = ResolvedEntityId,
                        FrameId = ResolvedFrameId,
                        Timestamp = FoxgloveTimeUtil.ToFoxgloveTime(CurrentLogTimeNs),
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
    }
}
