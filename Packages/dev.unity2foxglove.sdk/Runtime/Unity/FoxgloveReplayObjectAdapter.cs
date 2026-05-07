using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Drives Unity GameObjects from MCAP replay topic messages.
    /// Searches scene for GameObjects matching frame_id / entity_id by name.
    /// Manual FrameMapping / EntityMapping arrays override automatic lookup.
    /// </summary>
    public class FoxgloveReplayObjectAdapter : MonoBehaviour
    {
        [Header("Manager")]
        /// <summary>Reference to the scene's FoxgloveManager. Auto-resolved if null.</summary>
        [SerializeField] private FoxgloveManager _manager;

        [Header("Auto-Lookup (by name)")]
        /// <summary>When true, resolve frame/entity IDs by <c>GameObject.Find</c>.</summary>
        [SerializeField] private bool _autoLookup = true;

        [Header("Manual Overrides")]
        /// <summary>Explicit frame_id to Transform mappings.</summary>
        [SerializeField] private FrameMapping[] _frameOverrides;
        /// <summary>Explicit entity_id to Transform mappings.</summary>
        [SerializeField] private EntityMapping[] _entityOverrides;

        [Header("Topics")]
        /// <summary>Process <c>/tf</c> topic messages.</summary>
        [SerializeField] private bool _driveTf = true;
        /// <summary>Process <c>/scene</c> topic messages.</summary>
        [SerializeField] private bool _driveScene = true;

        /// <summary>Maps a frame_id string to a Unity Transform.</summary>
        [System.Serializable]
        public struct FrameMapping
        {
            /// <summary>Foxglove frame_id (child frame).</summary>
            public string ChildFrameId;
            /// <summary>Target Transform in the scene.</summary>
            public Transform Target;
        }

        /// <summary>Maps an entity_id string to a Unity Transform.</summary>
        [System.Serializable]
        public struct EntityMapping
        {
            /// <summary>Foxglove entity ID.</summary>
            public string EntityId;
            /// <summary>Target Transform in the scene.</summary>
            public Transform Target;
        }

        // ── Internal state ──
        /// <summary>Lookup cache for frame_id to Transform.</summary>
        private readonly Dictionary<string, Transform> _frameCache = new();
        /// <summary>Lookup cache for entity_id to Transform.</summary>
        private readonly Dictionary<string, Transform> _entityCache = new();
        /// <summary>Suppresses duplicate warnings for missing frames.</summary>
        private readonly HashSet<string> _warnedFrames = new();
        /// <summary>Suppresses duplicate warnings for missing entities.</summary>
        private readonly HashSet<string> _warnedEntities = new();
        /// <summary>Suppresses duplicate warnings for unparseable topics.</summary>
        private readonly HashSet<string> _warnedTopics = new();
        /// <summary>Reserved for future auto-lookup warnings.</summary>
        private readonly HashSet<string> _warnedAuto = new();
        /// <summary>Reusable MaterialPropertyBlock for colour application.</summary>
        private MaterialPropertyBlock _propBlock;

        /// <summary>
        /// Resolves the FoxgloveManager and subscribes to replay messages.
        /// Loads manual FrameMapping and EntityMapping overrides into the lookup cache.
        /// </summary>
        private void Start()
        {
            if (_manager == null)
                _manager = FindFirstObjectByType<FoxgloveManager>();
            if (_manager != null)
                _manager.OnReplayMessage += OnReplayMessage;

            foreach (var fm in _frameOverrides)
                if (!string.IsNullOrEmpty(fm.ChildFrameId) && fm.Target != null)
                    _frameCache[fm.ChildFrameId] = fm.Target;

            foreach (var em in _entityOverrides)
                if (!string.IsNullOrEmpty(em.EntityId) && em.Target != null)
                    _entityCache[em.EntityId] = em.Target;
        }

        /// <summary>Unsubscribes from replay messages.</summary>
        private void OnDestroy()
        {
            if (_manager != null)
                _manager.OnReplayMessage -= OnReplayMessage;
        }

        /// <summary>
        /// Receives raw JSON replay messages from FoxgloveManager.
        /// Routes <c>/tf</c> and <c>/scene</c> topics to their handlers.
        /// </summary>
        private void OnReplayMessage(string topic, byte[] payload)
        {
            try
            {
                var json = Encoding.UTF8.GetString(payload);
                var obj = JObject.Parse(json);

                switch (topic)
                {
                    case "/tf":
                        if (_driveTf) HandleFrameTransform(obj);
                        break;
                    case "/scene":
                        if (_driveScene) HandleSceneUpdate(obj);
                        break;
                    default:
                        break;
                }
            }
            catch (Exception ex)
            {
                if (_warnedTopics.Add(topic))
                    Debug.LogWarning($"[Foxglove Replay] Failed to parse {topic}: {ex.Message}");
            }
        }

        // ── /tf ──

        /// <summary>True when coordinate conversion is needed (RightHand mode).</summary>
        private bool ShouldConvert =>
            _manager != null && _manager.ActiveCoordinateMode == CoordinateMode.RightHand;

        /// <summary>
        /// Parses a <c>/tf</c> JSON object and applies position and rotation
        /// to the resolved child frame Transform.
        /// </summary>
        private void HandleFrameTransform(JObject tf)
        {
            var childFrameId = (string)tf["child_frame_id"];
            if (childFrameId == null) return;

            var target = ResolveFrame(childFrameId);
            if (target == null) return;

            var translation = tf["translation"];
            if (translation != null)
            {
                var fp = new Vector3((float)translation["x"], (float)translation["y"], (float)translation["z"]);
                target.localPosition = ShouldConvert ? CoordinateConverter.FoxgloveToUnityPosition(fp) : fp;
            }

            var rotation = tf["rotation"];
            if (rotation != null)
            {
                var fr = new Quaternion((float)rotation["x"], (float)rotation["y"], (float)rotation["z"], (float)rotation["w"]);
                target.localRotation = ShouldConvert ? CoordinateConverter.FoxgloveToUnityRotation(fr) : fr;
            }
        }

        /// <summary>
        /// Looks up a frame by ID. Checks the cache first, then auto-lookup
        /// via <c>GameObject.Find</c>. Logs a warning on first miss.
        /// </summary>
        private Transform ResolveFrame(string childFrameId)
        {
            if (_frameCache.TryGetValue(childFrameId, out var target))
                return target;

            if (_autoLookup)
            {
                var go = GameObject.Find(childFrameId);
                if (go != null)
                {
                    var t = go.transform;
                    _frameCache[childFrameId] = t;
                    return t;
                }
            }

            if (_warnedFrames.Add(childFrameId))
                Debug.LogWarning($"[Foxglove Replay] No Transform found for frame_id '{childFrameId}'. Add a FrameMapping override or place a GameObject with this name in the scene.");

            return null;
        }

        // ── /scene ──

        /// <summary>
        /// Parses a <c>/scene</c> JSON object and applies cube/model primitives
        /// to the resolved entity Transforms. Deletions are ignored.
        /// </summary>
        private void HandleSceneUpdate(JObject scene)
        {
            var entities = scene["entities"] as JArray;
            if (entities == null) return;

            var deletions = scene["deletions"] as JArray;

            foreach (var ent in entities)
            {
                var entity = ent as JObject;
                if (entity == null) continue;

                var entityId = (string)entity["id"];
                if (entityId == null) continue;

                var target = ResolveEntity(entityId);
                if (target == null) continue;

                var timestamp = entity["timestamp"];
                var frameId = (string)entity["frame_id"];

                var cubes = entity["cubes"] as JArray;
                if (cubes != null && cubes.Count > 0)
                    ApplyCubePrimitive(cubes[0] as JObject, target);

                var models = entity["models"] as JArray;
                if (models != null && models.Count > 0)
                    ApplyModelPrimitive(models[0] as JObject, target);
            }
        }

        /// <summary>
        /// Looks up an entity by ID. Checks the cache first, then auto-lookup
        /// via <c>GameObject.Find</c>. Logs a warning on first miss.
        /// </summary>
        private Transform ResolveEntity(string entityId)
        {
            if (_entityCache.TryGetValue(entityId, out var target))
                return target;

            if (_autoLookup)
            {
                var go = GameObject.Find(entityId);
                if (go != null)
                {
                    var t = go.transform;
                    _entityCache[entityId] = t;
                    return t;
                }
            }

            if (_warnedEntities.Add(entityId))
                Debug.LogWarning($"[Foxglove Replay] No Transform found for entity_id '{entityId}'. Add an EntityMapping override or place a GameObject with this name in the scene.");

            return null;
        }

        // ── Primitive helpers ──

        /// <summary>Applies size, pose, and color from a cube primitive JSON object.</summary>
        private void ApplyCubePrimitive(JObject cube, Transform target)
        {
            ApplyPrimitive(cube, target, "size");
        }

        /// <summary>Applies scale, pose, and color from a model primitive JSON object.</summary>
        private void ApplyModelPrimitive(JObject model, Transform target)
        {
            ApplyPrimitive(model, target, "scale");
        }

        /// <summary>
        /// Parses pose, size/scale, and color from a primitive JSON object and
        /// applies them to the target Transform and its Renderer.
        /// </summary>
        private void ApplyPrimitive(JObject primitive, Transform target, string sizeKey)
        {
            if (primitive == null) return;

            var pose = primitive["pose"] as JObject;
            if (pose != null)
            {
                var pos = pose["position"];
                var orient = pose["orientation"];
                if (pos != null)
                {
                    var fp = new Vector3((float)pos["x"], (float)pos["y"], (float)pos["z"]);
                    target.localPosition = ShouldConvert ? CoordinateConverter.FoxgloveToUnityPosition(fp) : fp;
                }
                if (orient != null)
                {
                    var fr = new Quaternion((float)orient["x"], (float)orient["y"], (float)orient["z"], (float)orient["w"]);
                    target.localRotation = ShouldConvert ? CoordinateConverter.FoxgloveToUnityRotation(fr) : fr;
                }
            }

            var scaleObj = primitive[sizeKey] as JObject;
            if (scaleObj != null)
                target.localScale = new Vector3((float)scaleObj["x"], (float)scaleObj["y"], (float)scaleObj["z"]);

            var color = primitive["color"] as JObject;
            if (color != null)
            {
                var renderer = target.GetComponent<Renderer>();
                if (renderer != null)
                {
                    if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(_propBlock);
                    _propBlock.SetColor("_BaseColor", new Color((float)color["r"], (float)color["g"], (float)color["b"], (float)color["a"]));
                    renderer.SetPropertyBlock(_propBlock);
                }
            }
        }
    }
}
