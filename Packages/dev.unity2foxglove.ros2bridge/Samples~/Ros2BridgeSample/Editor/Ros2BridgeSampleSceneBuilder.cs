// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: Samples/Ros2BridgeSample/Editor
// Purpose: Unity-owned generation and validation of the Bridge sample scene.

using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Unity2Foxglove.Ros2Bridge.Sample.Editor
{
    /// <summary>Builds the checked-in sample scene through Unity serialization.</summary>
    public static class Ros2BridgeSampleSceneBuilder
    {
        private const string SceneRelativePath =
            "Scenes/Ros2BridgeSample.unity";

        [MenuItem("Foxglove/ROS2 Bridge Sample/Rebuild Scene")]
        public static void BuildScene()
            => BuildAndValidate();

        /// <summary>Batch-mode entry point used by repository verification.</summary>
        public static void BuildFromCommandLine()
            => BuildAndValidate();

        private static void BuildAndValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "The ROS2 Bridge sample scene cannot be rebuilt in Play Mode.");

            var scenePath = ResolveSampleRoot() + "/" + SceneRelativePath;
            if (!File.Exists(scenePath))
                throw new FileNotFoundException(
                    "The imported ROS2 Bridge sample scene is missing.",
                    scenePath);

            var scene = EditorSceneManager.OpenScene(
                scenePath,
                OpenSceneMode.Single);
            var host = GameObject.Find("Foxglove");
            if (host == null)
                throw new InvalidDataException(
                    "The ROS2 Bridge sample scene has no Foxglove root.");

            var manager = host.GetComponent<FoxgloveManager>();
            if (manager == null)
                throw new InvalidDataException(
                    "The Foxglove root has no FoxgloveManager.");
            var provider = host.GetComponent<Ros2BridgeTransportProvider>();
            if (provider == null)
                provider = Undo.AddComponent<Ros2BridgeTransportProvider>(host);
            var duplex = host.GetComponent<Ros2BridgeSampleDuplex>();
            if (duplex == null)
                duplex = Undo.AddComponent<Ros2BridgeSampleDuplex>(host);

            Undo.RecordObject(manager, "Configure ROS2 Bridge sample Manager");
            var serializedManager = new SerializedObject(manager);
            var publishIds = RequireProperty(
                serializedManager,
                "_foxRunPublishTransportIds");
            publishIds.arraySize = 1;
            publishIds.GetArrayElementAtIndex(0).stringValue =
                Ros2BridgeTransportProvider.ProviderId;
            RequireProperty(
                    serializedManager,
                    "_foxRunSubscribeTransportId")
                .stringValue = Ros2BridgeTransportProvider.ProviderId;
            RequireProperty(serializedManager, "_enableFoxRunInbound")
                .boolValue = true;
            serializedManager.ApplyModifiedProperties();

            Undo.RecordObject(provider, "Configure ROS2 Bridge sample Provider");
            var serializedProvider = new SerializedObject(provider);
            RequireProperty(serializedProvider, "_available").boolValue = true;
            RequireProperty(serializedProvider, "_autoConnect").boolValue = true;
            RequireProperty(serializedProvider, "_host").stringValue = "127.0.0.1";
            RequireProperty(serializedProvider, "_port").intValue = 8767;
            serializedProvider.ApplyModifiedProperties();

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(provider);
            EditorUtility.SetDirty(duplex);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, scenePath, saveAsCopy: false))
                throw new IOException(
                    "Unity failed to save the ROS2 Bridge sample scene.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            ValidateScene(host, manager, provider, duplex);
            Debug.Log(
                "PHASE186G_BRIDGE_SAMPLE_SCENE_PASS path=" + scenePath);
        }

        private static void ValidateScene(
            GameObject host,
            FoxgloveManager manager,
            Ros2BridgeTransportProvider provider,
            Ros2BridgeSampleDuplex duplex)
        {
            if (host.GetComponents<Ros2BridgeTransportProvider>().Length != 1)
                throw new InvalidDataException(
                    "The sample must contain exactly one Bridge Provider companion.");
            if (host.GetComponents<Ros2BridgeSampleDuplex>().Length != 1)
                throw new InvalidDataException(
                    "The sample must contain exactly one duplex demonstration component.");
            if (manager == null || provider == null || duplex == null)
                throw new InvalidDataException(
                    "The sample's Manager, Provider, and duplex component are required.");

            var serializedManager = new SerializedObject(manager);
            var publishIds = RequireProperty(
                serializedManager,
                "_foxRunPublishTransportIds");
            if (publishIds.arraySize != 1
                || !string.Equals(
                    publishIds.GetArrayElementAtIndex(0).stringValue,
                    Ros2BridgeTransportProvider.ProviderId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The sample must select only the Bridge publish Provider.");
            }
            if (!string.Equals(
                    RequireProperty(
                            serializedManager,
                            "_foxRunSubscribeTransportId")
                        .stringValue,
                    Ros2BridgeTransportProvider.ProviderId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The sample must select the Bridge subscribe Provider.");
            }
            if (!RequireProperty(serializedManager, "_enableFoxRunInbound")
                    .boolValue)
            {
                throw new InvalidDataException(
                    "The sample must enable FoxRun subscriptions.");
            }
        }

        private static SerializedProperty RequireProperty(
            SerializedObject serialized,
            string propertyName)
            => serialized.FindProperty(propertyName)
               ?? throw new MissingFieldException(
                   serialized.targetObject.GetType().FullName,
                   propertyName);

        private static string ResolveSampleRoot()
        {
            var candidates = AssetDatabase
                .FindAssets("Ros2BridgeSampleSceneBuilder t:MonoScript")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(
                    "/Editor/Ros2BridgeSampleSceneBuilder.cs",
                    StringComparison.Ordinal))
                .OrderBy(
                    path => path.StartsWith("Assets/", StringComparison.Ordinal)
                        ? 0
                        : 1)
                .ThenBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (candidates.Length == 0)
                throw new FileNotFoundException(
                    "Unity could not locate Ros2BridgeSampleSceneBuilder.cs.");

            var editorDirectory = Path.GetDirectoryName(candidates[0]);
            var sampleRoot = Path.GetDirectoryName(editorDirectory);
            if (string.IsNullOrEmpty(sampleRoot))
                throw new InvalidDataException(
                    "The ROS2 Bridge sample root could not be resolved.");
            return sampleRoot.Replace('\\', '/');
        }
    }
}
