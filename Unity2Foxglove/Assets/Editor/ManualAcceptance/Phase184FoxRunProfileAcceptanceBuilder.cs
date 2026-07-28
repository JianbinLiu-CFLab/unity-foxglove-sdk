// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase184
// Purpose: Deterministically creates and configures the Phase184 acceptance scene.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Text;
using Newtonsoft.Json.Linq;
using Unity.FoxgloveSDK.Components;
using Unity.FoxgloveSDK.Editor;
using Unity2Foxglove.ManualAcceptance;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Unity2Foxglove
{
public static class Phase184FoxRunProfileAcceptanceBuilder
{
    public const string AcceptanceSceneAssetPath =
        "Assets/Scenes/ManualAcceptance/Phase184FoxRunProfileAcceptance.unity";

    private const string ProfileRouteName = "Helper-owned Route - Foxglove Profile";
    private const string MultiTargetRouteName = "Helper-owned Route - Multi Target";
    private const string DegradedTargetRouteName = "Helper-owned Route - Degraded Target";
    private const string QosRouteName = "Helper-owned Route - QoS Contract";
    private const string StreamRouteName = "Helper-owned Route - Stream 640 Hz";

    [MenuItem("Foxglove/Manual Acceptance/Phase184/Create or Refresh Profile Acceptance Scene")]
    public static void CreateOrRefreshAcceptanceScene()
    {
        var sceneDirectory = Path.GetDirectoryName(AcceptanceSceneAssetPath);
        if (string.IsNullOrWhiteSpace(sceneDirectory))
            throw new InvalidOperationException("Could not resolve the Phase184 scene directory.");
        Directory.CreateDirectory(Path.Combine(ProjectRoot(), sceneDirectory));

        var sceneExists = File.Exists(Path.Combine(ProjectRoot(), AcceptanceSceneAssetPath));
        var wasOpen = SceneManager.GetSceneByPath(AcceptanceSceneAssetPath);
        var scene = wasOpen.IsValid() && wasOpen.isLoaded
            ? wasOpen
            : sceneExists
                ? EditorSceneManager.OpenScene(
                    AcceptanceSceneAssetPath,
                    Application.isBatchMode ? OpenSceneMode.Single : OpenSceneMode.Additive)
                : EditorSceneManager.NewScene(
                    NewSceneSetup.EmptyScene,
                    Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);

        try
        {
            FoxgloveManager manager;
            Phase184FoxRunProfileAcceptance controller;
            Phase184FoxgloveProfileRoute profile;
            Phase184MultiTargetRoute multi;
            Phase184DegradedTargetRoute degraded;
            Phase184QosContractRoute qos;
            Phase184StreamRoute stream;

            if (sceneExists)
            {
                manager = RequireExactlyOne<FoxgloveManager>(scene);
                controller = RequireExactlyOne<Phase184FoxRunProfileAcceptance>(scene);
                profile = RequireExactlyOne<Phase184FoxgloveProfileRoute>(scene);
                multi = RequireExactlyOne<Phase184MultiTargetRoute>(scene);
                degraded = RequireExactlyOne<Phase184DegradedTargetRoute>(scene);
                qos = RequireExactlyOne<Phase184QosContractRoute>(scene);
                stream = RequireExactlyOne<Phase184StreamRoute>(scene);
            }
            else
            {
                var managerObject = NewObject(scene, "FoxgloveManager");
                managerObject.SetActive(false);
                manager = managerObject.AddComponent<FoxgloveManager>();

                var controllerObject = NewObject(scene, "Phase184 Profile Acceptance");
                controllerObject.SetActive(false);
                controller =
                    controllerObject.AddComponent<Phase184FoxRunProfileAcceptance>();

                var routes = CreateFreshRouteSet(scene);
                profile = routes.Profile;
                multi = routes.Multi;
                degraded = routes.Degraded;
                qos = routes.Qos;
                stream = routes.Stream;
            }

            NormalizeHelperOwnedRoute(profile, ProfileRouteName);
            NormalizeHelperOwnedRoute(multi, MultiTargetRouteName);
            NormalizeHelperOwnedRoute(degraded, DegradedTargetRouteName);
            NormalizeHelperOwnedRoute(qos, QosRouteName);
            NormalizeHelperOwnedRoute(stream, StreamRouteName);

            manager.gameObject.SetActive(false);
            controller.gameObject.SetActive(false);
            ConfigureManager(
                manager,
                Phase184FoxRunProfileAcceptance.MultiTargetCase,
                foxglovePort: 8765,
                bridgePort: 8767);

            var serialized = new SerializedObject(controller);
            SetObject(serialized, "_manager", manager);
            SetObject(serialized, "_foxgloveProfile", profile);
            SetObject(serialized, "_multiTarget", multi);
            SetObject(serialized, "_degradedTarget", degraded);
            SetObject(serialized, "_qosContract", qos);
            SetObject(serialized, "_stream", stream);
            serialized.ApplyModifiedPropertiesWithoutUndo();

            manager.gameObject.SetActive(true);
            controller.gameObject.SetActive(true);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(controller);
            if (!EditorSceneManager.SaveScene(scene, AcceptanceSceneAssetPath))
            {
                throw new IOException(
                    "Unity did not save the Phase184 acceptance scene.");
            }

            var manifestRefresh =
                FoxrunCodeGenerator.GenerateManifestFilesOnlyWithResult();
            Debug.Log(
                "PHASE184G_SCENE_ARTIFACTS_READY manifest="
                + manifestRefresh.Manifest.GlobalManifestHash
                + " schemaInfoChanged="
                + manifestRefresh.SchemaInfoChanged);
            AssetDatabase.SaveAssets();
            NormalizeUnityTextWhitespace(AcceptanceSceneAssetPath);
            NormalizeUnityTextWhitespace(AcceptanceSceneAssetPath + ".meta");
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(
                AcceptanceSceneAssetPath,
                ImportAssetOptions.ForceSynchronousImport);
            ValidateAcceptanceScene();
            Debug.Log(
                "PHASE184G_SCENE_BUILDER_PASS scene=" + AcceptanceSceneAssetPath,
                AssetDatabase.LoadAssetAtPath<SceneAsset>(AcceptanceSceneAssetPath));
        }
        finally
        {
            if (!Application.isBatchMode
                && scene.IsValid()
                && scene.isLoaded
                && (!wasOpen.IsValid() || !wasOpen.isLoaded))
            {
                EditorSceneManager.CloseScene(scene, removeScene: true);
            }
        }
    }

    public static void ValidateAcceptanceScene()
    {
        ValidateFreshRouteSetInMemory();
        if (!File.Exists(Path.Combine(ProjectRoot(), AcceptanceSceneAssetPath)))
        {
            throw new FileNotFoundException(
                "The tracked Phase184 acceptance scene is absent.",
                AcceptanceSceneAssetPath);
        }

        var existing = SceneManager.GetSceneByPath(AcceptanceSceneAssetPath);
        var closeAfter = !existing.IsValid() || !existing.isLoaded;
        var scene = closeAfter
            ? EditorSceneManager.OpenScene(AcceptanceSceneAssetPath, OpenSceneMode.Additive)
            : existing;
        try
        {
            RequireExactlyOne<FoxgloveManager>(scene);
            RequireExactlyOne<Phase184FoxRunProfileAcceptance>(scene);
            RequireHelperOwnedRoute<Phase184FoxgloveProfileRoute>(scene, ProfileRouteName);
            RequireHelperOwnedRoute<Phase184MultiTargetRoute>(scene, MultiTargetRouteName);
            RequireHelperOwnedRoute<Phase184DegradedTargetRoute>(scene, DegradedTargetRouteName);
            RequireHelperOwnedRoute<Phase184QosContractRoute>(scene, QosRouteName);
            RequireHelperOwnedRoute<Phase184StreamRoute>(scene, StreamRouteName);
            var guid = AssetDatabase.AssetPathToGUID(AcceptanceSceneAssetPath);
            if (!IsUnityGuid(guid))
                throw new InvalidOperationException("The Phase184 scene has no valid Unity GUID.");
        }
        finally
        {
            if (closeAfter && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, removeScene: true);
        }
    }

    internal static void ConfigureOpenSceneForRun(
        string caseId,
        int foxglovePort,
        int bridgePort)
    {
        var scene = SceneManager.GetSceneByPath(AcceptanceSceneAssetPath);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new InvalidOperationException(
                "Open the tracked Phase184 acceptance scene before Play Mode.");

        var managers = FindComponentsInScene<FoxgloveManager>(scene);
        if (managers.Count != 1)
            throw new InvalidOperationException(
                "The Phase184 acceptance scene must contain exactly one Manager.");
        ConfigureManager(managers[0], caseId, foxglovePort, bridgePort);
    }

    internal static void ConfigureOpenSceneForRun(JObject config)
    {
        if (config == null)
            throw new ArgumentNullException(nameof(config));
        var caseId = (string)config["case"] ?? string.Empty;
        var foxglovePort = (int?)config["foxglovePort"] ?? 0;
        var bridgePort = (int?)config["bridgePort"] ?? 0;
        ConfigureOpenSceneForRun(caseId, foxglovePort, bridgePort);
    }

    private static void ConfigureManager(
        FoxgloveManager manager,
        string caseId,
        int foxglovePort,
        int bridgePort)
    {
        if (manager == null)
            throw new ArgumentNullException(nameof(manager));
        if (foxglovePort < 1 || foxglovePort > 65535
            || bridgePort < 1 || bridgePort > 65535
            || foxglovePort == bridgePort)
        {
            throw new ArgumentOutOfRangeException(
                nameof(foxglovePort),
                "Phase184 ports must be distinct valid TCP ports.");
        }

        var foxglove =
            caseId == Phase184FoxRunProfileAcceptance.FoxgloveProfileCase
            || caseId == Phase184FoxRunProfileAcceptance.MultiTargetCase
            || caseId == Phase184FoxRunProfileAcceptance.DegradedTargetCase;
        var native =
            caseId == Phase184FoxRunProfileAcceptance.MultiTargetCase
            || caseId == Phase184FoxRunProfileAcceptance.QosContractCase
            || caseId == Phase184FoxRunProfileAcceptance.StreamCase;
        var bridge =
            caseId == Phase184FoxRunProfileAcceptance.MultiTargetCase
            || caseId == Phase184FoxRunProfileAcceptance.DegradedTargetCase
            || caseId == Phase184FoxRunProfileAcceptance.QosContractCase;
        if (!foxglove && !native && !bridge)
            throw new ArgumentException("Unknown Phase184 acceptance case.", nameof(caseId));

        var serialized = new SerializedObject(manager);
        SetBoolean(serialized, "_startOnEnable", true);
        SetBoolean(serialized, "_foxgloveOutputEnabled", foxglove);
        SetBoolean(serialized, "_ros2NativeEnabled", native);
        SetBoolean(serialized, "_ros2BridgeEnabled", bridge);
        SetBoolean(serialized, "_ros2BridgeAutoConnect", bridge);
        SetBoolean(serialized, "_enableFoxRunInbound", true);
        SetString(serialized, "_host", "127.0.0.1");
        SetInteger(serialized, "_port", foxglovePort);
        SetString(serialized, "_ros2BridgeHost", "127.0.0.1");
        SetInteger(serialized, "_ros2BridgePort", bridgePort);
        SetInteger(serialized, "_ros2BridgeSendTimeoutMs", 30000);
        SetInteger(serialized, "_foxRunInboundMaxMessagesPerSecondPerTopic", 1000);
        SetInteger(serialized, "_foxRunDefaultSubscribeRateHz", 60);
        SetFloat(serialized, "_defaultPublishRateHz", 60f);
        SetEnum(
            serialized,
            "_defaultFoxRunPublishEncoding",
            (int)FoxRunEncoding.Protobuf);
        SetEnum(
            serialized,
            "_defaultFoxRunSubscriptionSource",
            (int)(native ? FoxRunEndpoint.Ros2Native : FoxRunEndpoint.Foxglove));
        SetEnum(
            serialized,
            "_defaultFoxRunSubscriptionEncoding",
            (int)FoxRunEncoding.Protobuf);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        manager.EnableFoxRunInbound = true;
        manager.DefaultFoxRunPublishEncoding = FoxRunEncoding.Protobuf;
        manager.DefaultFoxRunSubscriptionSource =
            native ? FoxRunEndpoint.Ros2Native : FoxRunEndpoint.Foxglove;
        manager.DefaultFoxRunSubscriptionEncoding = FoxRunEncoding.Protobuf;
        EditorUtility.SetDirty(manager);
    }

    private static GameObject NewObject(Scene scene, string name)
    {
        var value = new GameObject(name);
        try
        {
            SceneManager.MoveGameObjectToScene(value, scene);
            return value;
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(value);
            throw;
        }
    }

    private static T AddInactiveRoute<T>(Scene scene, string name)
        where T : Phase184AcceptanceRoute
    {
        var value = NewObject(scene, name);
        value.SetActive(false);
        return value.AddComponent<T>();
    }

    private static RouteSet CreateFreshRouteSet(Scene scene)
    {
        return new RouteSet(
            AddInactiveRoute<Phase184FoxgloveProfileRoute>(scene, ProfileRouteName),
            AddInactiveRoute<Phase184MultiTargetRoute>(scene, MultiTargetRouteName),
            AddInactiveRoute<Phase184DegradedTargetRoute>(scene, DegradedTargetRouteName),
            AddInactiveRoute<Phase184QosContractRoute>(scene, QosRouteName),
            AddInactiveRoute<Phase184StreamRoute>(scene, StreamRouteName));
    }

    private static void NormalizeHelperOwnedRoute<T>(T route, string name)
        where T : Phase184AcceptanceRoute
    {
        if (route == null)
            throw new ArgumentNullException(nameof(route));
        route.gameObject.name = name;
        route.gameObject.SetActive(false);
        route.gameObject.hideFlags |= HideFlags.NotEditable;
        route.hideFlags |= HideFlags.NotEditable;
        EditorUtility.SetDirty(route.gameObject);
        EditorUtility.SetDirty(route);
    }

    private static T RequireHelperOwnedRoute<T>(Scene scene, string name)
        where T : Phase184AcceptanceRoute
    {
        var route = RequireExactlyOne<T>(scene, requireInactive: true);
        if (!string.Equals(route.gameObject.name, name, StringComparison.Ordinal)
            || (route.gameObject.hideFlags & HideFlags.NotEditable) == 0
            || (route.hideFlags & HideFlags.NotEditable) == 0)
        {
            throw new InvalidOperationException(
                "Phase184 route " + typeof(T).Name + " must be helper-owned and read-only.");
        }
        return route;
    }

    private static void ValidateFreshRouteSetInMemory()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        Exception validationException = null;
        try
        {
            var routes = CreateFreshRouteSet(scene);
            NormalizeHelperOwnedRoute(routes.Profile, ProfileRouteName);
            NormalizeHelperOwnedRoute(routes.Multi, MultiTargetRouteName);
            NormalizeHelperOwnedRoute(routes.Degraded, DegradedTargetRouteName);
            NormalizeHelperOwnedRoute(routes.Qos, QosRouteName);
            NormalizeHelperOwnedRoute(routes.Stream, StreamRouteName);
            RequireHelperOwnedRoute<Phase184FoxgloveProfileRoute>(scene, ProfileRouteName);
            RequireHelperOwnedRoute<Phase184MultiTargetRoute>(scene, MultiTargetRouteName);
            RequireHelperOwnedRoute<Phase184DegradedTargetRoute>(scene, DegradedTargetRouteName);
            RequireHelperOwnedRoute<Phase184QosContractRoute>(scene, QosRouteName);
            RequireHelperOwnedRoute<Phase184StreamRoute>(scene, StreamRouteName);
        }
        catch (Exception exception)
        {
            validationException = exception;
        }

        var cleanupException = ClosePreviewSceneWithFallback(scene);

        if (validationException != null && cleanupException != null)
        {
            throw new AggregateException(
                "Phase184 preview validation and cleanup both failed.",
                validationException,
                cleanupException);
        }
        if (validationException != null)
            ExceptionDispatchInfo.Capture(validationException).Throw();
        if (cleanupException != null)
            ExceptionDispatchInfo.Capture(cleanupException).Throw();
    }

    private static Exception ClosePreviewSceneWithFallback(Scene scene)
    {
        var failures = new List<Exception>();
        var initiallyClosed = false;
        try
        {
            initiallyClosed = EditorSceneManager.ClosePreviewScene(scene);
            if (!initiallyClosed)
            {
                failures.Add(new InvalidOperationException(
                    "Could not close the Phase184 preview validation scene."));
            }
        }
        catch (Exception exception)
        {
            failures.Add(exception);
        }

        if (!initiallyClosed)
        {
            GameObject[] roots;
            try
            {
                roots = scene.IsValid() && scene.isLoaded
                    ? scene.GetRootGameObjects()
                    : Array.Empty<GameObject>();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
                roots = Array.Empty<GameObject>();
            }

            foreach (var root in roots)
            {
                if (root == null)
                    continue;
                try
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }

            if (scene.IsValid() && scene.isLoaded)
            {
                try
                {
                    if (!EditorSceneManager.ClosePreviewScene(scene))
                    {
                        failures.Add(new InvalidOperationException(
                            "Could not close the Phase184 preview validation scene after fallback cleanup."));
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            }
        }

        return failures.Count == 0
            ? null
            : new AggregateException("Phase184 preview cleanup failed.", failures);
    }

    private static List<T> FindComponentsInScene<T>(Scene scene)
        where T : Component
    {
        var values = new List<T>();
        foreach (var root in scene.GetRootGameObjects())
            values.AddRange(root.GetComponentsInChildren<T>(includeInactive: true));
        return values;
    }

    private static T RequireExactlyOne<T>(Scene scene, bool requireInactive = false)
        where T : Component
    {
        var values = FindComponentsInScene<T>(scene);
        if (values.Count != 1)
        {
            throw new InvalidOperationException(
                "Phase184 scene requires exactly one " + typeof(T).Name + ".");
        }
        if (requireInactive && values[0].gameObject.activeSelf)
        {
            throw new InvalidOperationException(
                "Phase184 route " + typeof(T).Name + " must be inactive in the tracked scene.");
        }
        return values[0];
    }

    private sealed class RouteSet
    {
        internal RouteSet(
            Phase184FoxgloveProfileRoute profile,
            Phase184MultiTargetRoute multi,
            Phase184DegradedTargetRoute degraded,
            Phase184QosContractRoute qos,
            Phase184StreamRoute stream)
        {
            Profile = profile;
            Multi = multi;
            Degraded = degraded;
            Qos = qos;
            Stream = stream;
        }

        internal Phase184FoxgloveProfileRoute Profile { get; }
        internal Phase184MultiTargetRoute Multi { get; }
        internal Phase184DegradedTargetRoute Degraded { get; }
        internal Phase184QosContractRoute Qos { get; }
        internal Phase184StreamRoute Stream { get; }
    }

    private static void SetObject(
        SerializedObject target,
        string name,
        UnityEngine.Object value)
    {
        var property = RequireProperty(target, name);
        property.objectReferenceValue = value;
    }

    private static void SetBoolean(SerializedObject target, string name, bool value)
        => RequireProperty(target, name).boolValue = value;

    private static void SetInteger(SerializedObject target, string name, int value)
        => RequireProperty(target, name).intValue = value;

    private static void SetFloat(SerializedObject target, string name, float value)
        => RequireProperty(target, name).floatValue = value;

    private static void SetString(SerializedObject target, string name, string value)
        => RequireProperty(target, name).stringValue = value;

    private static void SetEnum(SerializedObject target, string name, int value)
        => RequireProperty(target, name).intValue = value;

    private static SerializedProperty RequireProperty(
        SerializedObject target,
        string name)
    {
        var property = target.FindProperty(name);
        if (property == null)
        {
            throw new InvalidOperationException(
                "FoxgloveManager no longer exposes serialized property " + name + ".");
        }
        return property;
    }

    private static string ProjectRoot()
        => Directory.GetParent(Application.dataPath)?.FullName
           ?? throw new InvalidOperationException("Could not resolve the Unity project root.");

    private static void NormalizeUnityTextWhitespace(string assetPath)
    {
        var path = Path.Combine(ProjectRoot(), assetPath);
        if (!File.Exists(path))
            return;

        var lines = File.ReadAllText(path)
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n');
        for (var index = 0; index < lines.Length; index++)
            lines[index] = lines[index].TrimEnd(' ', '\t');
        File.WriteAllText(
            path,
            string.Join("\n", lines),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static bool IsUnityGuid(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 32)
            return false;
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (!(character >= '0' && character <= '9')
                && !(character >= 'a' && character <= 'f'))
            {
                return false;
            }
        }
        return true;
    }
}
}
#endif
