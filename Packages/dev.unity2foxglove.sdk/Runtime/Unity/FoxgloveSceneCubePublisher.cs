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
        [SerializeField] private string _entityId = "";
        [SerializeField] private string _frameId = "";
        [SerializeField] private Vector3 _size = Vector3.one;
        [SerializeField] private Color _color = Color.green;

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
        private void OnValidate()
        {
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null)
                {
                    // Fire the event so FoxgloveDemoSetup knows the color changed externally
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
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _transformPublisher = GetComponent<FoxgloveTransformPublisher>();
        }

        private string ResolvedEntityId =>
            SanitizeFrameId(_entityId, gameObject.name);

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
