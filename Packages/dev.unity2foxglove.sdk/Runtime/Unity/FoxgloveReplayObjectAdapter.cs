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
        [SerializeField] private FoxgloveManager _manager;

        [Header("Auto-Lookup (by name)")]
        [SerializeField] private bool _autoLookup = true;

        [Header("Manual Overrides")]
        [SerializeField] private FrameMapping[] _frameOverrides;
        [SerializeField] private EntityMapping[] _entityOverrides;

        [Header("Topics")]
        [SerializeField] private bool _driveTf = true;
        [SerializeField] private bool _driveScene = true;

        [System.Serializable]
        public struct FrameMapping
        {
            public string ChildFrameId;
            public Transform Target;
        }

        [System.Serializable]
        public struct EntityMapping
        {
            public string EntityId;
            public Transform Target;
        }

        private readonly Dictionary<string, Transform> _frameCache = new();
        private readonly Dictionary<string, Transform> _entityCache = new();
        private readonly HashSet<string> _warnedFrames = new();
        private readonly HashSet<string> _warnedEntities = new();
        private readonly HashSet<string> _warnedTopics = new();
        private readonly HashSet<string> _warnedAuto = new();

        private void Start()
        {
            if (_manager == null)
                _manager = FindFirstObjectByType<FoxgloveManager>();
            if (_manager != null)
                _manager.OnReplayMessage += OnReplayMessage;

            // Load manual overrides
            foreach (var fm in _frameOverrides)
                if (!string.IsNullOrEmpty(fm.ChildFrameId) && fm.Target != null)
                    _frameCache[fm.ChildFrameId] = fm.Target;

            foreach (var em in _entityOverrides)
                if (!string.IsNullOrEmpty(em.EntityId) && em.Target != null)
                    _entityCache[em.EntityId] = em.Target;
        }

        private void OnDestroy()
        {
            if (_manager != null)
                _manager.OnReplayMessage -= OnReplayMessage;
        }

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
                target.localPosition = _manager != null ? _manager.FoxgloveToUnityPosition(fp) : fp;
            }

            var rotation = tf["rotation"];
            if (rotation != null)
            {
                var fr = new Quaternion((float)rotation["x"], (float)rotation["y"], (float)rotation["z"], (float)rotation["w"]);
                target.localRotation = _manager != null ? _manager.FoxgloveToUnityRotation(fr) : fr;
            }
        }

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

        private void HandleSceneUpdate(JObject scene)
        {
            var entities = scene["entities"] as JArray;
            if (entities == null) return;

            var deletions = scene["deletions"] as JArray;
            // deletions: ignore for now; user manages scene lifecycle

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

                // Cubes
                var cubes = entity["cubes"] as JArray;
                if (cubes != null && cubes.Count > 0)
                    ApplyCubePrimitive(cubes[0] as JObject, target);

                // Models
                var models = entity["models"] as JArray;
                if (models != null && models.Count > 0)
                    ApplyModelPrimitive(models[0] as JObject, target);
            }
        }

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

        private void ApplyCubePrimitive(JObject cube, Transform target)
        {
            if (cube == null) return;

            var pose = cube["pose"] as JObject;
            if (pose != null)
            {
                var pos = pose["position"];
                var orient = pose["orientation"];
                if (pos != null)
                {
                    var fp = new Vector3((float)pos["x"], (float)pos["y"], (float)pos["z"]);
                    target.localPosition = _manager != null ? _manager.FoxgloveToUnityPosition(fp) : fp;
                }
                if (orient != null)
                {
                    var fr = new Quaternion((float)orient["x"], (float)orient["y"], (float)orient["z"], (float)orient["w"]);
                    target.localRotation = _manager != null ? _manager.FoxgloveToUnityRotation(fr) : fr;
                }
            }

            var size = cube["size"] as JObject;
            if (size != null)
                target.localScale = new Vector3((float)size["x"], (float)size["y"], (float)size["z"]);

            var color = cube["color"] as JObject;
            if (color != null)
            {
                var renderer = target.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = new Color((float)color["r"], (float)color["g"], (float)color["b"], (float)color["a"]);
            }
        }

        private void ApplyModelPrimitive(JObject model, Transform target)
        {
            if (model == null) return;

            var pose = model["pose"] as JObject;
            if (pose != null)
            {
                var pos = pose["position"];
                var orient = pose["orientation"];
                if (pos != null)
                {
                    var fp = new Vector3((float)pos["x"], (float)pos["y"], (float)pos["z"]);
                    target.localPosition = _manager != null ? _manager.FoxgloveToUnityPosition(fp) : fp;
                }
                if (orient != null)
                {
                    var fr = new Quaternion((float)orient["x"], (float)orient["y"], (float)orient["z"], (float)orient["w"]);
                    target.localRotation = _manager != null ? _manager.FoxgloveToUnityRotation(fr) : fr;
                }
            }

            var scale = model["scale"] as JObject;
            if (scale != null)
                target.localScale = new Vector3((float)scale["x"], (float)scale["y"], (float)scale["z"]);

            var color = model["color"] as JObject;
            if (color != null)
            {
                var renderer = target.GetComponent<Renderer>();
                if (renderer != null)
                    renderer.material.color = new Color((float)color["r"], (float)color["g"], (float)color["b"], (float)color["a"]);
            }
        }
    }
}
