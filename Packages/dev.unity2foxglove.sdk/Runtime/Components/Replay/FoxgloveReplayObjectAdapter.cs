// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Runtime/Components/Replay
// Purpose: Drives Unity GameObjects from MCAP replay messages via behavior-based pose ownership.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Core;
using UnityEngine;

namespace Unity.FoxgloveSDK.Components
{
    /// <summary>
    /// Drives Unity GameObjects from MCAP replay messages.
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
        [SerializeField] private FrameMapping[] _frameOverrides = Array.Empty<FrameMapping>();
        /// <summary>Explicit entity_id to Transform mappings.</summary>
        [SerializeField] private EntityMapping[] _entityOverrides = Array.Empty<EntityMapping>();

        [Header("Topics")]
        /// <summary>Process frame-transform pose messages, regardless of topic name.</summary>
        [SerializeField] private bool _driveTf = true;
        /// <summary>Process scene update messages, regardless of topic name.</summary>
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
        /// <summary>Transform instance lookup used when deferred poses resolve after the init window.</summary>
        private readonly Dictionary<int, Transform> _transformByPoseKey = new();
        /// <summary>Local channel overrides used when heuristic topic fallback fails to decode.</summary>
        private readonly Dictionary<ushort, ReplayChannelBehavior> _channelBehaviorOverrides = new();
        /// <summary>Pure pose ownership state for the active replay session.</summary>
        private readonly ReplayPoseOwnershipArbiter _poseArbiter = new();
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
        private bool _hasReplaySession;
        private ulong _activeReplayStartTimeNs;
        /// <summary>Disabled trace hook used only for manual before/after replay pose investigations.</summary>
        private const bool ReplayPoseTraceEnabled = false;

        /// <summary>
        /// Resolves the FoxgloveManager and loads manual FrameMapping and
        /// EntityMapping overrides into the lookup cache.
        /// </summary>
        private void Start()
        {
            ResolveManager();
            EnsureMappingArrays();
            foreach (var fm in _frameOverrides)
                if (!string.IsNullOrEmpty(fm.ChildFrameId) && fm.Target != null)
                    _frameCache[fm.ChildFrameId] = fm.Target;

            foreach (var em in _entityOverrides)
                if (!string.IsNullOrEmpty(em.EntityId) && em.Target != null)
                    _entityCache[em.EntityId] = em.Target;

            if (isActiveAndEnabled)
                SubscribeReplay();
        }

        private void OnValidate()
        {
            EnsureMappingArrays();
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
            ResetPoseOwnershipSession();
        }

        /// <summary>Ensures replay messages are detached during object destruction.</summary>
        private void OnDestroy()
        {
            UnsubscribeReplay();
            ResetPoseOwnershipSession();
        }

        private void ResolveManager()
        {
            if (_manager == null)
                _manager = FindFirstObjectByType<FoxgloveManager>();
        }

        private void EnsureMappingArrays()
        {
            if (_frameOverrides == null)
                _frameOverrides = Array.Empty<FrameMapping>();
            if (_entityOverrides == null)
                _entityOverrides = Array.Empty<EntityMapping>();
        }

        private void SubscribeReplay()
        {
            if (_subscribed || _manager == null) return;
            _manager.OnReplayMessageContext += OnReplayMessage;
            _manager.OnReplayBatchCompleted += OnReplayBatchCompleted;
            _subscribed = true;
        }

        private void UnsubscribeReplay()
        {
            if (!_subscribed) return;
            if (_manager != null)
            {
                _manager.OnReplayMessageContext -= OnReplayMessage;
                _manager.OnReplayBatchCompleted -= OnReplayBatchCompleted;
            }
            _subscribed = false;
        }

        /// <summary>
        /// Receives replay messages with source context and routes pose-capable
        /// payloads through behavior-based ownership arbitration.
        /// </summary>
        private void OnReplayMessage(ReplayMessageContext context)
        {
            if (context.Payload == null) return;

            try
            {
                EnsureReplaySession(context);

                if (TryParseJsonObject(context.Payload, out var json))
                {
                    var behavior = ResolveBehavior(context, json);
                    if (behavior == ReplayChannelBehavior.FrameTransformPose && _driveTf)
                        HandleFrameTransform(json, context);
                    else if (behavior == ReplayChannelBehavior.ScenePrimitivePose && _driveScene)
                        HandleSceneUpdate(json, context);
                    return;
                }

                var protobufBehavior = ResolveBehavior(context, null);
                if (protobufBehavior == ReplayChannelBehavior.FrameTransformPose && _driveTf)
                    HandleFrameTransform(ParseProtobuf(GetFrameTransformProtoTypeName(context.SchemaName), context.Payload), context);
                else if (protobufBehavior == ReplayChannelBehavior.ScenePrimitivePose && _driveScene)
                    HandleSceneUpdate(ParseProtobuf("Foxglove.SceneUpdate", context.Payload), context);
            }
            catch (Exception ex)
            {
                var warningKey = string.IsNullOrEmpty(context.Topic) ? context.SchemaName : context.Topic;
                if (_warnedTopics.Add(warningKey ?? string.Empty))
                    Debug.LogWarning($"[Foxglove Replay] Failed to parse replay channel {context.ChannelId} ({warningKey}): {ex.Message}");
                if (IsTopicFallbackBehavior(context))
                    _channelBehaviorOverrides[context.ChannelId] = ReplayChannelBehavior.NonPose;
            }
        }

        private ReplayChannelBehavior ResolveBehavior(ReplayMessageContext context, JObject json)
        {
            if (_channelBehaviorOverrides.TryGetValue(context.ChannelId, out var overrideBehavior))
                return overrideBehavior;

            var behavior = _manager != null
                ? _manager.GetReplayChannelBehavior(context.ChannelId)
                : ReplayChannelBehavior.NotLoaded;
            if (behavior != ReplayChannelBehavior.NotLoaded
                && behavior != ReplayChannelBehavior.Unclassified
                && behavior != ReplayChannelBehavior.NonPose)
                return behavior;

            if (json != null)
            {
                var jsonBehavior = ReplayChannelBehaviorClassifier.ClassifyJsonObject(json);
                if (jsonBehavior != ReplayChannelBehavior.NonPose)
                    return jsonBehavior;
            }

            return behavior == ReplayChannelBehavior.NotLoaded
                ? ReplayChannelBehaviorClassifier.ClassifyChannel(context.MessageEncoding, context.SchemaName, context.SchemaEncoding, context.Topic)
                : behavior;
        }

        private void OnReplayBatchCompleted(ReplayBatchContext context)
        {
            if (!_hasReplaySession || _activeReplayStartTimeNs != context.ReplayStartTimeNs)
                return;

            FlushDeferredScenePoses(context);
        }

        private void EnsureReplaySession(ReplayMessageContext context)
        {
            if (_hasReplaySession && _activeReplayStartTimeNs == context.ReplayStartTimeNs)
                return;

            ResetPoseOwnershipSession();
            _activeReplayStartTimeNs = context.ReplayStartTimeNs;
            _hasReplaySession = true;
        }

        private void ResetPoseOwnershipSession()
        {
            _poseArbiter.Reset();
            _transformByPoseKey.Clear();
            _channelBehaviorOverrides.Clear();
            _hasReplaySession = false;
            _activeReplayStartTimeNs = 0;
        }

        private void FlushDeferredScenePoses(ReplayBatchContext context)
        {
            if (!_poseArbiter.IsDeferralActive)
                return;

            foreach (var decision in _poseArbiter.EndInitDeferral())
            {
                if (TryGetLivePoseTarget(decision.TransformKey, out var target))
                {
                    ApplyPoseSample(target, decision.Pose);
                    TracePoseWrite("held-scene", context, decision.OwnerChannelId.ToString(), target);
                }
            }
        }

        private bool TryGetLivePoseTarget(int transformKey, out Transform target)
        {
            if (_transformByPoseKey.TryGetValue(transformKey, out target) && target != null)
                return true;

            RemoveStalePoseTarget(transformKey);
            target = null;
            return false;
        }

        private void RemoveStalePoseTarget(int transformKey)
            => _transformByPoseKey.Remove(transformKey);

        private static bool IsTopicFallbackBehavior(ReplayMessageContext context)
        {
            if (!string.IsNullOrWhiteSpace(context.SchemaName))
                return false;

            if (!(string.IsNullOrEmpty(context.MessageEncoding)
                  || string.Equals(context.MessageEncoding, "protobuf", StringComparison.OrdinalIgnoreCase)))
                return false;

            return string.Equals(context.Topic, "/tf", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(context.Topic, "/tf_static", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(context.Topic, "/scene", StringComparison.OrdinalIgnoreCase);
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

        // ── Frame transforms ──

        /// <summary>True when coordinate conversion is needed (RightHand mode).</summary>
        private bool ShouldConvert =>
            _manager != null && _manager.ActiveCoordinateMode == CoordinateMode.RightHand;

        private void HandleFrameTransform(JObject message, ReplayMessageContext context)
        {
            if (message["transforms"] is JArray transforms)
            {
                foreach (var item in transforms)
                    if (item is JObject tf)
                        ApplyFrameTransform(tf, context);
                return;
            }

            ApplyFrameTransform(message, context);
        }

        private void HandleFrameTransform(object message, ReplayMessageContext context)
        {
            var transforms = GetPropertyValue(message, "Transforms") as IEnumerable;
            if (transforms != null)
            {
                foreach (var item in transforms)
                    ApplyFrameTransform(item, context);
                return;
            }

            ApplyFrameTransform(message, context);
        }

        private void ApplyFrameTransform(JObject tf, ReplayMessageContext context)
        {
            if (!TryReadFramePose(tf, out var childFrameId, out var pose))
                return;

            var target = ResolveFrame(childFrameId);
            if (target == null) return;

            ApplyOwnedPose(
                target,
                context,
                ReplayChannelBehavior.FrameTransformPose,
                pose,
                "frame-transform",
                childFrameId);
        }

        private void ApplyFrameTransform(object tf, ReplayMessageContext context)
        {
            if (!TryReadFramePose(tf, out var childFrameId, out var pose))
                return;

            var target = ResolveFrame(childFrameId);
            if (target == null) return;

            ApplyOwnedPose(
                target,
                context,
                ReplayChannelBehavior.FrameTransformPose,
                pose,
                "frame-transform",
                childFrameId);
        }

        private bool TryReadFramePose(JObject tf, out string childFrameId, out ReplayPoseSample pose)
        {
            childFrameId = (string)tf?["child_frame_id"];
            if (string.IsNullOrEmpty(childFrameId))
            {
                pose = default;
                return false;
            }

            var translation = tf["translation"];
            var rotation = tf["rotation"];
            pose = new ReplayPoseSample(
                translation != null,
                translation != null ? (float)translation["x"] : 0,
                translation != null ? (float)translation["y"] : 0,
                translation != null ? (float)translation["z"] : 0,
                rotation != null,
                rotation != null ? (float)rotation["x"] : 0,
                rotation != null ? (float)rotation["y"] : 0,
                rotation != null ? (float)rotation["z"] : 0,
                rotation != null ? (float)rotation["w"] : 1);
            return pose.HasPosition || pose.HasRotation;
        }

        private bool TryReadFramePose(object tf, out string childFrameId, out ReplayPoseSample pose)
        {
            childFrameId = GetStringProperty(tf, "ChildFrameId");
            if (string.IsNullOrEmpty(childFrameId))
            {
                pose = default;
                return false;
            }

            var translation = GetPropertyValue(tf, "Translation");
            var rotation = GetPropertyValue(tf, "Rotation");
            pose = ToPoseSample(translation, rotation);
            return pose.HasPosition || pose.HasRotation;
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

        // ── Scene updates ──

        private void HandleSceneUpdate(JObject scene, ReplayMessageContext context)
        {
            var entities = scene["entities"] as JArray;
            if (entities == null) return;

            foreach (var ent in entities)
            {
                var entity = ent as JObject;
                if (entity == null) continue;

                var entityId = (string)entity["id"];
                if (entityId == null) continue;

                var target = ResolveEntity(entityId);
                if (target == null) continue;

                var cubes = entity["cubes"] as JArray;
                if (cubes != null && cubes.Count > 0)
                    ApplyCubePrimitive(cubes[0] as JObject, target, context, entityId);

                var models = entity["models"] as JArray;
                if (models != null && models.Count > 0)
                    ApplyModelPrimitive(models[0] as JObject, target, context, entityId);
            }
        }

        private void HandleSceneUpdate(object scene, ReplayMessageContext context)
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
                    ApplyCubePrimitive(cube, target, context, entityId);

                var model = GetFirstItem(GetPropertyValue(entity, "Models"));
                if (model != null)
                    ApplyModelPrimitive(model, target, context, entityId);
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

        private void ApplyCubePrimitive(JObject cube, Transform target, ReplayMessageContext context, string entityId)
        {
            ApplyPrimitive(cube, target, context, entityId, "size");
        }

        private void ApplyCubePrimitive(object cube, Transform target, ReplayMessageContext context, string entityId)
        {
            if (cube == null) return;
            ApplyPrimitive(
                GetPropertyValue(cube, "Pose"),
                GetPropertyValue(cube, "Size"),
                GetPropertyValue(cube, "Color"),
                target,
                context,
                entityId);
        }

        private void ApplyModelPrimitive(JObject model, Transform target, ReplayMessageContext context, string entityId)
        {
            ApplyPrimitive(model, target, context, entityId, "scale");
        }

        private void ApplyModelPrimitive(object model, Transform target, ReplayMessageContext context, string entityId)
        {
            if (model == null) return;
            ApplyPrimitive(
                GetPropertyValue(model, "Pose"),
                GetPropertyValue(model, "Scale"),
                GetPropertyValue(model, "Color"),
                target,
                context,
                entityId);
        }

        private void ApplyPrimitive(
            JObject primitive,
            Transform target,
            ReplayMessageContext context,
            string entityId,
            string sizeKey)
        {
            if (primitive == null) return;

            if (TryReadPrimitivePose(primitive["pose"] as JObject, out var pose))
                ApplyOwnedPose(target, context, ReplayChannelBehavior.ScenePrimitivePose, pose, "scene", entityId);

            ApplySceneVisuals(primitive, target, sizeKey);
        }

        private void ApplyPrimitive(
            object pose,
            object scale,
            object color,
            Transform target,
            ReplayMessageContext context,
            string entityId)
        {
            if (TryReadPrimitivePose(pose, out var poseSample))
                ApplyOwnedPose(target, context, ReplayChannelBehavior.ScenePrimitivePose, poseSample, "scene", entityId);

            ApplySceneVisuals(scale, color, target);
        }

        private void ApplyOwnedPose(
            Transform target,
            ReplayMessageContext context,
            ReplayChannelBehavior behavior,
            ReplayPoseSample pose,
            string source,
            string id)
        {
            var transformKey = target.GetInstanceID();
            _transformByPoseKey[transformKey] = target;
            var decision = _poseArbiter.OfferPose(transformKey, context.ChannelId, behavior, context.LogTimeNs, pose);
            if (decision.Kind == ReplayPoseOwnershipDecisionKind.Apply)
            {
                ApplyPoseSample(target, decision.Pose);
                TracePoseWrite(source, context, id, target);
            }
            else if (decision.ShouldReportContention)
            {
                Debug.Log(
                    $"[Foxglove Replay] Pose ownership contention skipped for '{target.name}'. " +
                    $"ownerChannel={decision.OwnerChannelId}, skippedChannel={context.ChannelId}, " +
                    $"source={source}, topic='{context.Topic}', schema='{context.SchemaName}'.");
            }
        }

        private void ApplyPoseSample(Transform target, ReplayPoseSample pose)
        {
            if (pose.HasPosition)
            {
                var fp = new Vector3(pose.PositionX, pose.PositionY, pose.PositionZ);
                target.localPosition = ShouldConvert ? CoordinateConverter.FoxgloveToUnityPosition(fp) : fp;
            }

            if (pose.HasRotation)
            {
                var fr = new Quaternion(pose.RotationX, pose.RotationY, pose.RotationZ, pose.RotationW);
                target.localRotation = ShouldConvert ? CoordinateConverter.FoxgloveToUnityRotation(fr) : fr;
            }
        }

        private void ApplySceneVisuals(JObject primitive, Transform target, string sizeKey)
        {
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

        private void ApplySceneVisuals(object scale, object color, Transform target)
        {
            if (scale != null)
                target.localScale = ToUnityVector(scale);

            if (color != null)
                ApplyColor(color, target);
        }

        private bool TryReadPrimitivePose(JObject pose, out ReplayPoseSample sample)
        {
            if (pose == null)
            {
                sample = default;
                return false;
            }

            var pos = pose["position"];
            var orient = pose["orientation"];
            sample = new ReplayPoseSample(
                pos != null,
                pos != null ? (float)pos["x"] : 0,
                pos != null ? (float)pos["y"] : 0,
                pos != null ? (float)pos["z"] : 0,
                orient != null,
                orient != null ? (float)orient["x"] : 0,
                orient != null ? (float)orient["y"] : 0,
                orient != null ? (float)orient["z"] : 0,
                orient != null ? (float)orient["w"] : 1);
            return sample.HasPosition || sample.HasRotation;
        }

        private bool TryReadPrimitivePose(object pose, out ReplayPoseSample sample)
        {
            if (pose == null)
            {
                sample = default;
                return false;
            }

            sample = ToPoseSample(GetPropertyValue(pose, "Position"), GetPropertyValue(pose, "Orientation"));
            return sample.HasPosition || sample.HasRotation;
        }

        private static ReplayPoseSample ToPoseSample(object position, object rotation)
            => new(
                position != null,
                position != null ? GetFloatProperty(position, "X") : 0,
                position != null ? GetFloatProperty(position, "Y") : 0,
                position != null ? GetFloatProperty(position, "Z") : 0,
                rotation != null,
                rotation != null ? GetFloatProperty(rotation, "X") : 0,
                rotation != null ? GetFloatProperty(rotation, "Y") : 0,
                rotation != null ? GetFloatProperty(rotation, "Z") : 0,
                rotation != null ? GetFloatProperty(rotation, "W") : 1);

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

        private void TracePoseWrite(string source, ReplayMessageContext context, string id, Transform target)
        {
            if (!ReplayPoseTraceEnabled || target == null) return;
            Debug.Log($"[Foxglove Replay Pose Trace] source={source} channel={context.ChannelId} id={id} target={target.name} localPosition={target.localPosition} localRotation={target.localRotation}");
        }

        private void TracePoseWrite(string source, ReplayBatchContext context, string id, Transform target)
        {
            if (!ReplayPoseTraceEnabled || target == null) return;
            Debug.Log($"[Foxglove Replay Pose Trace] source={source} batchSource={context.Source} id={id} target={target.name} localPosition={target.localPosition} localRotation={target.localRotation}");
        }

        private static string GetFrameTransformProtoTypeName(string schemaName)
            => schemaName != null && schemaName.EndsWith(".FrameTransforms", StringComparison.OrdinalIgnoreCase)
                ? "Foxglove.FrameTransforms"
                : "Foxglove.FrameTransform";

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
    }
}
