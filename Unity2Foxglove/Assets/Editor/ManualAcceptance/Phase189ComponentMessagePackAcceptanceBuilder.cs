// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase189
// Purpose: Unity-owned deterministic construction of the Phase189 acceptance scene.

#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity2Foxglove.ManualAcceptance;

namespace Unity2Foxglove
{
    /// <summary>Builds and validates the maintained Phase189 acceptance scene.</summary>
    public static class Phase189ComponentMessagePackAcceptanceBuilder
    {
        public const string AcceptanceSceneAssetPath = "Assets/Scenes/ManualAcceptance/Phase189ComponentMessagePackAcceptance.unity";

        [MenuItem("Foxglove/Manual Acceptance/Phase189/Create or Refresh Component MessagePack Scene")]
        public static void CreateOrRefreshAcceptanceScene() => BuildAndValidate();

        /// <summary>Batch entry point used by the real Unity Editor.</summary>
        public static void BuildFromCommandLine() => BuildAndValidate();

        public static void BuildAndValidate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Phase189 scene cannot be built during Play Mode.");
            var sceneDirectory = Path.GetDirectoryName(AcceptanceSceneAssetPath);
            if (string.IsNullOrWhiteSpace(sceneDirectory)) throw new InvalidDataException("Scene directory is missing.");
            Directory.CreateDirectory(Path.Combine(ProjectRoot(), sceneDirectory));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var host = new GameObject("Phase189 Component MessagePack Acceptance");
            SceneManager.MoveGameObjectToScene(host, scene);
            var manager = host.AddComponent<FoxgloveManager>();
            var acceptance = host.AddComponent<Phase189ComponentMessagePackAcceptance>();
            ConfigureManager(manager);

            var scalarObject = CreateFixture(scene, "Component Scalar Publisher", Phase189ComponentMessagePackAcceptance.ScalarTopic);
            var scalar = scalarObject.AddComponent<Phase189ScalarPublisher>();
            ConfigurePublisher(scalar, manager, Phase189ComponentMessagePackAcceptance.ScalarTopic, PublisherEncodingOverride.MsgPack);
            var nestedObject = CreateFixture(scene, "Component Nested Publisher", Phase189ComponentMessagePackAcceptance.NestedTopic);
            var nested = nestedObject.AddComponent<Phase189NestedPublisher>();
            ConfigurePublisher(nested, manager, Phase189ComponentMessagePackAcceptance.NestedTopic, PublisherEncodingOverride.MsgPack);
            var transformObject = CreateFixture(scene, "Transform Publisher", "/phase189/component/transform");
            var transform = transformObject.AddComponent<FoxgloveTransformPublisher>();
            ConfigurePublisher(transform, manager, "/phase189/component/transform", PublisherEncodingOverride.MsgPack);

            var systemObject = CreateFixture(scene, "SystemInfo Publisher", "/phase189/component/system");
            var system = systemObject.AddComponent<FoxgloveSystemInfoPublisher>();
            ConfigurePublisher(system, manager, "/phase189/component/system", PublisherEncodingOverride.Json);

            var cameraObject = CreateFixture(scene, "JPEG Camera Publisher", Phase189ComponentMessagePackAcceptance.JpegTopic);
            var camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<FoxgloveCameraPublisher>();
            var cameraPublisher = cameraObject.GetComponent<FoxgloveCameraPublisher>();
            ConfigurePublisher(cameraPublisher, manager, Phase189ComponentMessagePackAcceptance.JpegTopic, PublisherEncodingOverride.MsgPack);

            var pointObject = CreateFixture(scene, "Raw PointCloud Publisher", Phase189ComponentMessagePackAcceptance.PointCloudTopic);
            var pointCloud = pointObject.AddComponent<FoxglovePointCloudPublisher>();
            ConfigurePublisher(pointCloud, manager, Phase189ComponentMessagePackAcceptance.PointCloudTopic, PublisherEncodingOverride.MsgPack);

            var testLogObject = CreateFixture(scene, "Component Scalar and Nested Fixture", "/phase189/component/scalar");
            var testLog = testLogObject.AddComponent<TestLog>();
            acceptance.ConfigureSceneReferences(manager, new FoxglovePublisherBase[] { scalar, nested, transform, system, cameraPublisher, pointCloud });
            acceptance.ConfigureComponentFixtures(scalar, nested);
            acceptance.ConfigureRun("phase189-manual", string.Empty, string.Empty);

            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(acceptance);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, AcceptanceSceneAssetPath))
                throw new IOException("Unity did not save the Phase189 acceptance scene.");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            ValidateAcceptanceScene();
            Debug.Log("PHASE189_ACCEPTANCE_SCENE_BUILDER_PASS scene=" + AcceptanceSceneAssetPath);
        }

        public static void ValidateAcceptanceScene()
        {
            var full = Path.Combine(ProjectRoot(), AcceptanceSceneAssetPath);
            if (!File.Exists(full)) throw new FileNotFoundException("Phase189 acceptance scene is absent.", full);
            var scene = SceneManager.GetSceneByPath(AcceptanceSceneAssetPath);
            var closeAfter = !scene.IsValid() || !scene.isLoaded;
            if (closeAfter) scene = EditorSceneManager.OpenScene(AcceptanceSceneAssetPath, OpenSceneMode.Additive);
            try
            {
                RequireExactlyOne<FoxgloveManager>(scene);
                RequireExactlyOne<Phase189ComponentMessagePackAcceptance>(scene);
                if (scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<FoxgloveCameraPublisher>(true)).Count() != 1)
                    throw new InvalidDataException("Phase189 scene must contain one JPEG camera publisher.");
                if (scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<FoxglovePointCloudPublisher>(true)).Count() != 1)
                    throw new InvalidDataException("Phase189 scene must contain one raw point-cloud publisher.");
            }
            finally { if (closeAfter && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true); }
        }

        private static GameObject CreateFixture(Scene scene, string name, string topic)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, scene);
            return go;
        }

        private static void ConfigureManager(FoxgloveManager manager)
        {
            var serialized = new SerializedObject(manager);
            Require(serialized, "_startOnEnable").boolValue = true;
            Require(serialized, "_port").intValue = 8765;
            SetEnumValue(Require(serialized, "_defaultPublisherEncoding"), GlobalEncoding.MsgPack);
            Require(serialized, "_allowPublisherOverride").boolValue = true;
            Require(serialized, "_enableRecording").boolValue = false;
            Require(serialized, "_recordingPrefix").stringValue = "phase189";
            Require(serialized, "_recordingDirectory").stringValue = "../build/phase189/manual";
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void ConfigurePublisher(FoxglovePublisherBase publisher, FoxgloveManager manager, string topic, PublisherEncodingOverride encoding)
        {
            var serialized = new SerializedObject(publisher);
            Require(serialized, "_manager").objectReferenceValue = manager;
            Require(serialized, "_topic").stringValue = topic;
            SetEnumValue(Require(serialized, "_encodingOverride"), encoding);
            Require(serialized, "_publishOnEnable").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty Require(SerializedObject serialized, string name)
            => serialized.FindProperty(name) ?? throw new MissingFieldException(serialized.targetObject.GetType().FullName, name);

        private static void SetEnumValue(SerializedProperty property, Enum value)
        {
            var index = Array.IndexOf(property.enumNames, value.ToString());
            if (index < 0)
                throw new InvalidDataException($"Enum value {value} is not exposed by {property.propertyPath}.");
            property.enumValueIndex = index;
        }

        private static T RequireExactlyOne<T>(Scene scene) where T : Component
        {
            var values = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
            if (values.Length != 1) throw new InvalidDataException("Expected one " + typeof(T).Name + " but found " + values.Length + ".");
            return values[0];
        }

        private static string ProjectRoot() => Path.GetDirectoryName(Application.dataPath) ?? throw new DirectoryNotFoundException("Unity project root missing.");
    }
}
#endif
