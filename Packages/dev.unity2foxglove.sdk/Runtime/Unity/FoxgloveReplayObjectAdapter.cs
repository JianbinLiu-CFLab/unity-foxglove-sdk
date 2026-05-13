// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Unity
// Purpose: Drives Unity GameObjects from MCAP replay /tf and /scene topic messages via FoxgloveManager.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
        /// <summary>Whether this adapter is currently subscribed to replay messages.</summary>
        private bool _subscribed;

        /// <summary>
        /// Resolves the FoxgloveManager and loads manual FrameMapping and
        /// EntityMapping overrides into the lookup cache.
        /// </summary>
        private void Start()
        {
            ResolveManager();
            foreach (var fm in _frameOverrides)
                if (!string.IsNullOrEmpty(fm.ChildFrameId) && fm.Target != null)
                    _frameCache[fm.ChildFrameId] = fm.Target;

            foreach (var em in _entityOverrides)
                if (!string.IsNullOrEmpty(em.EntityId) && em.Target != null)
                    _entityCache[em.EntityId] = em.Target;

            if (isActiveAndEnabled)
                SubscribeReplay();
        }

        /// <summary>Subscribes to replay messages while the component is enabled.</summary>
        private void OnEnable()
        {
            ResolveManager();
            SubscribeReplay();
        }

        /// <summary>Unsubscribes from replay messages when the component is disabled.</summary>
        private void OnDisable()
        {
            UnsubscribeReplay();
        }

        /// <summary>Ensures replay messages are detached during object destruction.</summary>
        private void OnDestroy()
        {
            UnsubscribeReplay();
        }

        private void ResolveManager()
        {
            if (_manager == null)
                _manager = FindFirstObjectByType<FoxgloveManager>();
        }

        private void SubscribeReplay()
        {
            if (_subscribed || _manager == null) return;
            _manager.OnReplayMessage += OnReplayMessage;
            _subscribed = true;
        }

        private void UnsubscribeReplay()
        {
            if (!_subscribed) return;
            if (_manager != null)
                _manager.OnReplayMessage -= OnReplayMessage;
            _subscribed = false;
        }

        /// <summary>
        /// Receives raw replay messages from FoxgloveManager.
        /// Routes <c>/tf</c> and <c>/scene</c> JSON or protobuf payloads to their handlers.
        /// </summary>
        private void OnReplayMessage(string topic, byte[] payload)
        {
            if (payload == null) return;

            try
            {
                switch (topic)
                {
                    case "/tf":
                        if (!_driveTf) return;
                        if (TryParseJsonObject(payload, out var tfJson))
                            HandleFrameTransform(tfJson);
                        else
                            HandleFrameTransform(ParseProtobuf("Foxglove.FrameTransform", payload));
                        break;
                    case "/scene":
                        if (!_driveScene) return;
                        if (TryParseJsonObject(payload, out var sceneJson))
                            HandleSceneUpdate(sceneJson);
                        else
                            HandleSceneUpdate(ParseProtobuf("Foxglove.SceneUpdate", payload));
                        break;
                    default:
                        return;
                }
            }
            catch (Exception ex)
            {
                if (_warnedTopics.Add(topic))
                    Debug.LogWarning($"[Foxglove Replay] Failed to parse {topic}: {ex.Message}");
            }
        }

        private static bool TryParseJsonObject(byte[] payload, out JObject obj)
        {
            obj = null;
            if (!LooksLikeJsonObject(payload)) return false;

            var json = Encoding.UTF8.GetString(payload);
            obj = JObject.Parse(json);
            return true;
        }

        private static bool LooksLikeJsonObject(byte[] payload)
        {
            for (var i = 0; i < payload.Length; i++)
            {
                var b = payload[i];
                if (b == (byte)' ' || b == (byte)'\t' || b == (byte)'\r' || b == (byte)'\n')
                    continue;
                return b == (byte)'{';
            }

            return false;
        }

        private static object ParseProtobuf(string typeName, byte[] payload)
        {
            var type = Type.GetType(typeName + ", Unity.FoxgloveSDK.Proto");
            if (type == null)
                throw new InvalidOperationException($"Optional protobuf type '{typeName}' is not available.");

            var parser = type.GetProperty("Parser", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (parser == null)
                throw new InvalidOperationException($"Optional protobuf type '{typeName}' does not expose a Parser.");

            var parseFrom = parser.GetType().GetMethod(
                "ParseFrom",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(byte[]) },
                null);
            if (parseFrom == null)
                throw new InvalidOperationException($"Optional protobuf parser for '{typeName}' does not support ParseFrom(byte[]).");

            try
            {
                return parseFrom.Invoke(parser, new object[] { payload });
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
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
        /// Parses a <c>/tf</c> protobuf message and applies position and rotation
        /// to the resolved child frame Transform.
        /// </summary>
        private void HandleFrameTransform(object tf)
        {
            var childFrameId = GetStringProperty(tf, "ChildFrameId");
            if (string.IsNullOrEmpty(childFrameId)) return;

            var target = ResolveFrame(childFrameId);
            if (target == null) return;

            var translation = GetPropertyValue(tf, "Translation");
            if (translation != null)
            {
                var fp = ToUnityVector(translation);
                target.localPosition = ShouldConvert ? CoordinateConverter.FoxgloveToUnityPosition(fp) : fp;
            }

            var rotation = GetPropertyValue(tf, "Rotation");
            if (rotation != null)
            {
                var fr = ToUnityQuaternion(rotation);
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
        /// Parses a <c>/scene</c> protobuf message and applies cube/model primitives
        /// to the resolved entity Transforms. Deletions are ignored.
        /// </summary>
        private void HandleSceneUpdate(object scene)
        {
            if (scene == null) return;

            var entities = GetPropertyValue(scene, "Entities") as IEnumerable;
            if (entities == null) return;

            foreach (var entity in entities)
            {
                var entityId = GetStringProperty(entity, "Id");
                if (string.IsNullOrEmpty(entityId)) continue;

                var target = ResolveEntity(entityId);
                if (target == null) continue;

                var cube = GetFirstItem(GetPropertyValue(entity, "Cubes"));
                if (cube != null)
                    ApplyCubePrimitive(cube, target);

                var model = GetFirstItem(GetPropertyValue(entity, "Models"));
                if (model != null)
                    ApplyModelPrimitive(model, target);
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

        /// <summary>Applies size, pose, and color from a cube primitive protobuf object.</summary>
        private void ApplyCubePrimitive(object cube, Transform target)
        {
            if (cube == null) return;
            ApplyPrimitive(
                GetPropertyValue(cube, "Pose"),
                GetPropertyValue(cube, "Size"),
                GetPropertyValue(cube, "Color"),
                target);
        }

        /// <summary>Applies scale, pose, and color from a model primitive JSON object.</summary>
        private void ApplyModelPrimitive(JObject model, Transform target)
        {
            ApplyPrimitive(model, target, "scale");
        }

        /// <summary>Applies scale, pose, and color from a model primitive protobuf object.</summary>
        private void ApplyModelPrimitive(object model, Transform target)
        {
            if (model == null) return;
            ApplyPrimitive(
                GetPropertyValue(model, "Pose"),
                GetPropertyValue(model, "Scale"),
                GetPropertyValue(model, "Color"),
                target);
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

        /// <summary>
        /// Parses pose, size/scale, and color from a primitive protobuf object and
        /// applies them to the target Transform and its Renderer.
        /// </summary>
        private void ApplyPrimitive(object pose, object scale, object color, Transform target)
        {
            if (pose != null)
            {
                var position = GetPropertyValue(pose, "Position");
                if (position != null)
                {
                    var fp = ToUnityVector(position);
                    target.localPosition = ShouldConvert ? CoordinateConverter.FoxgloveToUnityPosition(fp) : fp;
                }

                var orientation = GetPropertyValue(pose, "Orientation");
                if (orientation != null)
                {
                    var fr = ToUnityQuaternion(orientation);
                    target.localRotation = ShouldConvert ? CoordinateConverter.FoxgloveToUnityRotation(fr) : fr;
                }
            }

            if (scale != null)
                target.localScale = ToUnityVector(scale);

            if (color != null)
                ApplyColor(color, target);
        }

        private void ApplyColor(object color, Transform target)
        {
            var renderer = target.GetComponent<Renderer>();
            if (renderer == null) return;

            if (_propBlock == null) _propBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor("_BaseColor", new UnityEngine.Color(
                GetFloatProperty(color, "R"),
                GetFloatProperty(color, "G"),
                GetFloatProperty(color, "B"),
                GetFloatProperty(color, "A")));
            renderer.SetPropertyBlock(_propBlock);
        }

        private static object GetPropertyValue(object source, string propertyName)
            => source?.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(source);

        private static string GetStringProperty(object source, string propertyName)
            => GetPropertyValue(source, propertyName) as string;

        private static float GetFloatProperty(object source, string propertyName)
        {
            var value = GetPropertyValue(source, propertyName);
            return value == null ? 0f : Convert.ToSingle(value);
        }

        private static object GetFirstItem(object collection)
        {
            var enumerable = collection as IEnumerable;
            if (enumerable == null) return null;
            foreach (var item in enumerable)
                return item;
            return null;
        }

        private static Vector3 ToUnityVector(object value)
            => new Vector3(GetFloatProperty(value, "X"), GetFloatProperty(value, "Y"), GetFloatProperty(value, "Z"));

        private static Quaternion ToUnityQuaternion(object value)
            => new Quaternion(GetFloatProperty(value, "X"), GetFloatProperty(value, "Y"), GetFloatProperty(value, "Z"), GetFloatProperty(value, "W"));
    }
}
