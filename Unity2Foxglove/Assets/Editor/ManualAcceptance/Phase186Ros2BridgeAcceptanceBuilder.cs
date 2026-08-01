// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase186
// Purpose: Unity-owned construction of the controlled Bridge acceptance scene.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity2Foxglove.ManualAcceptance;
using Unity2Foxglove.Ros2Bridge;

namespace Unity2Foxglove
{
    public static class Phase186Ros2BridgeAcceptanceBuilder
    {
        public const string AcceptanceSceneAssetPath =
            "Assets/Scenes/ManualAcceptance/Phase186Ros2BridgeAcceptance.unity";

        [MenuItem(
            "Foxglove/Manual Acceptance/Phase186/Create or Refresh ROS2 Bridge Scene")]
        public static void BuildScene()
        {
            if (!Application.isBatchMode
                && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }
            BuildAndValidate();
        }

        /// <summary>Batch entry point; Unity owns all YAML serialization.</summary>
        public static void BuildFromCommandLine()
            => BuildAndValidate();

        public static void BuildAndValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "The Phase186 scene cannot be rebuilt in Play Mode.");

            var directory = Path.GetDirectoryName(AcceptanceSceneAssetPath);
            if (string.IsNullOrEmpty(directory))
                throw new InvalidDataException("Acceptance scene directory is absent.");
            Directory.CreateDirectory(Path.Combine(ProjectRoot(), directory));

            var scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Single);
            var host = new GameObject("Phase186 ROS2 Bridge Acceptance");
            SceneManager.MoveGameObjectToScene(host, scene);
            host.SetActive(false);

            var manager = host.AddComponent<FoxgloveManager>();
            var provider = host.AddComponent<Ros2BridgeTransportProvider>();
            var acceptance = host.AddComponent<Phase186Ros2BridgeAcceptance>();
            ConfigureManager(manager, null, 8765);
            ConfigureProvider(provider, 8767);
            acceptance.ConfigureSceneReferences(manager, provider);

            var cameraObject = new GameObject("Acceptance Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.AddComponent<Camera>();
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            host.SetActive(true);

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(provider);
            EditorUtility.SetDirty(acceptance);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(
                    scene,
                    AcceptanceSceneAssetPath,
                    saveAsCopy: false))
            {
                throw new IOException(
                    "Unity did not save the Phase186 acceptance scene.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(
                AcceptanceSceneAssetPath,
                ImportAssetOptions.ForceSynchronousImport);
            ValidateAcceptanceScene();
            Debug.Log(
                "PHASE186_ACCEPTANCE_SCENE_BUILDER_PASS scene="
                + AcceptanceSceneAssetPath,
                AssetDatabase.LoadAssetAtPath<SceneAsset>(AcceptanceSceneAssetPath));
        }

        public static void ValidateAcceptanceScene()
        {
            if (!File.Exists(Path.Combine(ProjectRoot(), AcceptanceSceneAssetPath)))
                throw new FileNotFoundException(
                    "The tracked Phase186 acceptance scene is absent.",
                    AcceptanceSceneAssetPath);

            var existing = SceneManager.GetSceneByPath(AcceptanceSceneAssetPath);
            var closeAfter = !existing.IsValid() || !existing.isLoaded;
            var scene = closeAfter
                ? EditorSceneManager.OpenScene(
                    AcceptanceSceneAssetPath,
                    OpenSceneMode.Additive)
                : existing;
            try
            {
                var manager = RequireExactlyOne<FoxgloveManager>(scene);
                var provider = RequireExactlyOne<Ros2BridgeTransportProvider>(scene);
                var acceptance = RequireExactlyOne<Phase186Ros2BridgeAcceptance>(scene);
                if (!ReferenceEquals(manager.gameObject, provider.gameObject)
                    || !ReferenceEquals(manager.gameObject, acceptance.gameObject))
                {
                    throw new InvalidDataException(
                        "Manager, Provider, and acceptance controller must share one host.");
                }
                ValidateManager(manager);
                ValidateProvider(provider);
            }
            finally
            {
                if (closeAfter && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }

        internal static void ConfigureOpenSceneForRun(
            Phase186RunConfiguration config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            var scene = SceneManager.GetSceneByPath(AcceptanceSceneAssetPath);
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException(
                    "The Phase186 acceptance scene must be open before configuration.");

            var manager = RequireExactlyOne<FoxgloveManager>(scene);
            var provider = RequireExactlyOne<Ros2BridgeTransportProvider>(scene);
            var acceptance = RequireExactlyOne<Phase186Ros2BridgeAcceptance>(scene);
            EnsureFanoutProvider(manager, config.CaseId);
            ConfigureManager(manager, config.CaseId, config.FoxglovePort);
            ConfigureProvider(provider, config.BridgePort);
            acceptance.ConfigureSceneReferences(manager, provider);
            acceptance.ConfigureForRun(
                config.RunId,
                config.CaseId,
                config.TokenHash,
                config.Head,
                config.Topics,
                config.Manual,
                config.SlowMainThread ? 12 : 0,
                config.OutputRoot,
                config.ExternalGate,
                config.ExerciseGate);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(provider);
            EditorUtility.SetDirty(acceptance);
        }

        private static void ConfigureManager(
            FoxgloveManager manager,
            string caseId,
            int foxglovePort)
        {
            var serialized = new SerializedObject(manager);
            var publishIds = RequireProperty(
                serialized,
                "_foxRunPublishTransportIds");
            var fanout = string.Equals(
                caseId,
                "fanout-fairness-health",
                StringComparison.Ordinal);
            publishIds.arraySize = fanout ? 3 : 1;
            publishIds.GetArrayElementAtIndex(0).stringValue = fanout
                ? FoxgloveWebSocketTransport.Id
                : Ros2BridgeTransportProvider.ProviderId;
            if (fanout)
            {
                publishIds.GetArrayElementAtIndex(1).stringValue =
                    "unity2foxglove.r2fu";
                publishIds.GetArrayElementAtIndex(2).stringValue =
                    Ros2BridgeTransportProvider.ProviderId;
            }
            RequireProperty(serialized, "_foxRunSubscribeTransportId")
                .stringValue = Ros2BridgeTransportProvider.ProviderId;
            RequireProperty(serialized, "_enableFoxRunInbound").boolValue = true;
            RequireProperty(serialized, "_startOnEnable").boolValue = true;
            RequireProperty(serialized, "_port").intValue = foxglovePort;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFanoutProvider(
            FoxgloveManager manager,
            string caseId)
        {
            if (!string.Equals(
                    caseId,
                    "fanout-fairness-health",
                    StringComparison.Ordinal))
            {
                return;
            }
            var type = Type.GetType(
                "Unity2Foxglove.Ros2ForUnity.Native.FoxRunRos2TransportProvider, "
                + "Unity2Foxglove.Ros2ForUnity.Native",
                throwOnError: false);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
            {
                throw new InvalidOperationException(
                    "The all-Providers fanout case requires the R2FU Provider package.");
            }
            if (manager.GetComponent(type) == null)
                manager.gameObject.AddComponent(type);
        }

        private static void ConfigureProvider(
            Ros2BridgeTransportProvider provider,
            int port)
        {
            if (port < 1 || port > 65535)
                throw new ArgumentOutOfRangeException(nameof(port));
            var serialized = new SerializedObject(provider);
            RequireProperty(serialized, "_available").boolValue = true;
            RequireProperty(serialized, "_autoConnect").boolValue = true;
            RequireProperty(serialized, "_host").stringValue = "127.0.0.1";
            RequireProperty(serialized, "_port").intValue = port;
            RequireProperty(serialized, "_queueCapacity").intValue = 1024;
            RequireProperty(serialized, "_reconnectIntervalMs").intValue = 250;
            RequireProperty(serialized, "_sendTimeoutMs").intValue = 1000;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ValidateManager(FoxgloveManager manager)
        {
            var serialized = new SerializedObject(manager);
            var publishIds = RequireProperty(
                serialized,
                "_foxRunPublishTransportIds");
            if (publishIds.arraySize != 1
                || !string.Equals(
                    publishIds.GetArrayElementAtIndex(0).stringValue,
                    Ros2BridgeTransportProvider.ProviderId,
                    StringComparison.Ordinal)
                || !string.Equals(
                    RequireProperty(serialized, "_foxRunSubscribeTransportId")
                        .stringValue,
                    Ros2BridgeTransportProvider.ProviderId,
                    StringComparison.Ordinal)
                || !RequireProperty(serialized, "_enableFoxRunInbound").boolValue)
            {
                throw new InvalidDataException(
                    "Phase186 Manager Provider selection differs from authority.");
            }
        }

        private static void ValidateProvider(Ros2BridgeTransportProvider provider)
        {
            var serialized = new SerializedObject(provider);
            if (!RequireProperty(serialized, "_available").boolValue
                || !RequireProperty(serialized, "_autoConnect").boolValue
                || !string.Equals(
                    RequireProperty(serialized, "_host").stringValue,
                    "127.0.0.1",
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Phase186 Bridge Provider must be available, automatic, and loopback-only.");
            }
        }

        private static T RequireExactlyOne<T>(Scene scene)
            where T : Component
        {
            var values = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<T>(true))
                .ToArray();
            if (values.Length != 1)
                throw new InvalidDataException(
                    "Expected exactly one " + typeof(T).FullName
                    + " but found " + values.Length + ".");
            return values[0];
        }

        private static SerializedProperty RequireProperty(
            SerializedObject serialized,
            string propertyName)
            => serialized.FindProperty(propertyName)
               ?? throw new MissingFieldException(
                   serialized.targetObject.GetType().FullName,
                   propertyName);

        private static string ProjectRoot()
            => Path.GetDirectoryName(Application.dataPath)
               ?? throw new DirectoryNotFoundException(
                   "Unity project root could not be resolved.");
    }
}
