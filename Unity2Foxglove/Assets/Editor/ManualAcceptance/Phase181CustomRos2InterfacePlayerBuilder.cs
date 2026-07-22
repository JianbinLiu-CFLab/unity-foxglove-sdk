// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase181
// Purpose: Creates the custom ROS2 interface acceptance scene and isolated Player.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Components;
using Unity2Foxglove.ManualAcceptance;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Owns only the reproducible Phase181 custom-interface scene and Player
/// build. The tool reads resolved package manifests rather than adding an
/// editor assembly reference from the project to an optional R2FU package.
/// </summary>
public static class Phase181CustomRos2InterfacePlayerBuilder
{
    public const string AcceptanceSceneAssetPath =
        "Assets/Scenes/ManualAcceptance/Phase181FoxRunCustomRos2InterfaceAcceptance.unity";

    private const string RuntimePackagePrefix =
        "dev.unity2foxglove.ros2forunity.runtime.";
    private const string TypesupportPackagePrefix =
        "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.";
    private const string StaticInterfacePackageId =
        "dev.unity2foxglove.foxrun.ros2.interfaces";
    private const string StaticInterfaceLockRelativePath =
        "RuntimeSupport/foxrun-ros2-interface-lock.json";
    private const string ExecutableName =
        "Phase181FoxRunCustomRos2Interface.exe";
    private const string SampleDirectoryAssetPath =
        "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun Custom ROS2 Interface";
    private const string SampleMetadataStagingAssetPath =
        "Assets/__Phase181SampleMetadataStaging";
    private const string SampleMetadataStagingSampleAssetPath =
        SampleMetadataStagingAssetPath + "/FoxRun Custom ROS2 Interface";

    private static readonly string[] SampleMetadataAssetPaths =
    {
        SampleDirectoryAssetPath,
        SampleDirectoryAssetPath + "/Phase181FoxRunCustomRos2Interface.cs",
        SampleDirectoryAssetPath + "/README.md",
    };

    // The external Player host supplies values only for this compact contract.
    // It must not forward an opaque run token, router configuration, home path,
    // or arbitrary ambient variables into the persisted Player build report.
    private static readonly string[] PlayerEnvironmentKeys =
    {
        "ROS_DISTRO",
        "RMW_IMPLEMENTATION",
        "ROS_DOMAIN_ID",
        "ROS_AUTOMATIC_DISCOVERY_RANGE",
        "UNITY2FOXGLOVE_FOXRUN_INTERFACE_REVISION",
        "UNITY2FOXGLOVE_FOXRUN_INTERFACE_DIGEST",
        "UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID",
    };

    [MenuItem("Foxglove/Manual Acceptance/Phase181/Create Custom ROS2 Interface Acceptance Scene")]
    public static void CreateAcceptanceScene()
    {
        EnsureSampleMetadata();
        var sceneAbsolutePath = Path.Combine(ProjectRoot(), AcceptanceSceneAssetPath);
        if (File.Exists(sceneAbsolutePath) || File.Exists(sceneAbsolutePath + ".meta"))
        {
            throw new InvalidOperationException(
                "The Phase181 acceptance scene already exists. Refusing to overwrite tracked manual-acceptance evidence: "
                + AcceptanceSceneAssetPath);
        }

        var directory = Path.GetDirectoryName(sceneAbsolutePath);
        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Could not resolve the Phase181 acceptance-scene directory.");
        Directory.CreateDirectory(directory);

        // A batch Unity process starts in an unsaved, untitled scene. Unity rejects
        // adding another scene beside it, while an interactive operator may have
        // unsaved work that this command must preserve.
        var sceneMode = Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive;
        var acceptanceScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, sceneMode);
        try
        {
            var managerObject = new GameObject("FoxgloveManager");
            SceneManager.MoveGameObjectToScene(managerObject, acceptanceScene);
            managerObject.SetActive(false);
            var manager = managerObject.AddComponent<FoxgloveManager>();
            ConfigureManager(manager);

            var receiverObject = new GameObject("Phase181 Custom ROS2 Interface Acceptance");
            SceneManager.MoveGameObjectToScene(receiverObject, acceptanceScene);
            receiverObject.SetActive(false);
            var receiver = receiverObject.AddComponent<Phase181FoxRunCustomRos2InterfaceAcceptance>();
            ConfigureAcceptanceReceiver(receiver, manager);

            managerObject.SetActive(true);
            receiverObject.SetActive(true);
            if (!EditorSceneManager.SaveScene(acceptanceScene, sceneAbsolutePath, saveAsCopy: false))
            {
                throw new IOException(
                    "Unity did not save the Phase181 acceptance scene: " + AcceptanceSceneAssetPath);
            }
        }
        finally
        {
            // Keep the sole scene open until the batch process exits. Closing it
            // would leave Unity with no scene, whereas interactive creation used
            // an additive scene and can safely restore the operator's workspace.
            if (!Application.isBatchMode)
                EditorSceneManager.CloseScene(acceptanceScene, removeScene: true);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(AcceptanceSceneAssetPath, ImportAssetOptions.ForceSynchronousImport);
        if (!IsUnityGuid(AssetDatabase.AssetPathToGUID(AcceptanceSceneAssetPath)))
        {
            throw new InvalidOperationException(
                "Unity did not generate a valid GUID for the Phase181 acceptance scene.");
        }

        Debug.Log(
            "[Phase181] Created custom ROS2 interface acceptance scene at " + AcceptanceSceneAssetPath + ".",
            AssetDatabase.LoadAssetAtPath<SceneAsset>(AcceptanceSceneAssetPath));
    }

    [MenuItem("Foxglove/Manual Acceptance/Phase181/Generate Custom Interface Sample Metadata")]
    public static void GenerateSampleMetadata()
    {
        EnsureSampleMetadata();
        Debug.Log("[Phase181] Unity generated valid metadata for the custom ROS2 interface sample assets.");
    }

    [MenuItem("Foxglove/Manual Acceptance/Phase181/Build Custom ROS2 Interface Windows Standalone64")]
    public static void BuildWindowsStandalone64()
        => BuildWindowsStandalone64Core();

    /// <summary>
    /// Batch entry point. Invoke only when no interactive Unity Editor holds
    /// this project: <c>-executeMethod
    /// Phase181CustomRos2InterfacePlayerBuilder.BuildWindowsStandalone64FromCommandLine</c>.
    /// </summary>
    public static void BuildWindowsStandalone64FromCommandLine()
        => BuildWindowsStandalone64Core();

    private static void BuildWindowsStandalone64Core()
    {
        EnsureSampleMetadata();
        ValidateAcceptanceScene();
        var runtime = ResolveExactlyOneActiveRuntime();
        ValidateMatchingTypesupportPackage(runtime.RosDistro);
        var staticInterface = ResolveStaticInterfaceLock();
        var buildDirectory = BuildDirectoryFor(runtime.RosDistro);
        var executablePath = Path.Combine(buildDirectory, ExecutableName);
        EnsurePathWithinBuildRoot(buildDirectory);
        Directory.CreateDirectory(buildDirectory);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { AcceptanceSceneAssetPath },
            locationPathName = executablePath,
            target = BuildTarget.StandaloneWindows64,
            subtarget = (int)StandaloneBuildSubtarget.Player,
            options = BuildOptions.None,
        };

        Debug.Log(
            "[Phase181] Building WindowsStandalone64 custom ROS2 interface acceptance Player for "
            + runtime.PackageName + " (" + runtime.RosDistro + ").");
        var buildReport = BuildPipeline.BuildPlayer(options);
        var reportPath = Path.Combine(buildDirectory, "phase181-runtime-build-report.json");
        File.WriteAllText(reportPath, JsonUtility.ToJson(new Phase181BuildReport
        {
            runtimePackage = runtime.PackageName,
            rosDistro = runtime.RosDistro,
            interfacePackage = StaticInterfacePackageId,
            interfaceRevision = staticInterface.InterfaceRevision,
            interfaceDigest = staticInterface.InterfaceDigest,
            playerEnvironmentKeys = PlayerEnvironmentKeys,
            acceptanceScene = AcceptanceSceneAssetPath,
            executablePath = RelativeToRepository(executablePath),
            playerAutoQuitFlag = "--phase181-custom-ros2-player-auto-quit",
            totalErrors = buildReport.summary.totalErrors,
            totalWarnings = buildReport.summary.totalWarnings,
            outputSize = buildReport.summary.totalSize,
        }, prettyPrint: true));

        if (buildReport.summary.totalErrors != 0)
        {
            throw new InvalidOperationException(
                "Phase181 WindowsStandalone64 Player build failed with "
                + buildReport.summary.totalErrors + " errors. See " + reportPath);
        }

        Debug.Log("[Phase181] Built WindowsStandalone64 Player: " + executablePath + ".");
    }

    private static void EnsureSampleMetadata()
    {
        var existingMetadataCount = 0;
        foreach (var assetPath in SampleMetadataAssetPaths)
        {
            if (File.Exists(SampleMetadataAbsolutePath(assetPath)))
                existingMetadataCount++;
        }

        if (existingMetadataCount == SampleMetadataAssetPaths.Length)
        {
            ValidateSampleMetadata();
            return;
        }

        if (existingMetadataCount != 0)
        {
            throw new InvalidOperationException(
                "Phase181 sample metadata is incomplete. Refusing to overwrite existing Unity GUIDs; restore or remove the partial set before generation.");
        }

        GenerateSampleMetadataFromUnityStaging();
        ValidateSampleMetadata();
    }

    private static void GenerateSampleMetadataFromUnityStaging()
    {
        CleanupStaleSampleMetadataStaging();
        var stagingRootAbsolutePath = ProjectAssetPathToAbsolutePath(SampleMetadataStagingAssetPath);
        if (Directory.Exists(stagingRootAbsolutePath) || File.Exists(stagingRootAbsolutePath + ".meta"))
        {
            throw new InvalidOperationException(
                "Phase181 metadata staging could not be cleared: " + SampleMetadataStagingAssetPath);
        }

        var stagingSampleAbsolutePath = ProjectAssetPathToAbsolutePath(SampleMetadataStagingSampleAssetPath);
        try
        {
            Directory.CreateDirectory(stagingSampleAbsolutePath);
            for (var index = 1; index < SampleMetadataAssetPaths.Length; index++)
            {
                var sourceAssetPath = SampleMetadataAssetPaths[index];
                var suffix = sourceAssetPath.Substring(SampleDirectoryAssetPath.Length);
                var sourceAbsolutePath = PackageAssetPathToAbsolutePath(sourceAssetPath);
                var stagingAbsolutePath = ProjectAssetPathToAbsolutePath(
                    SampleMetadataStagingSampleAssetPath + suffix);
                if (!File.Exists(sourceAbsolutePath))
                {
                    throw new FileNotFoundException(
                        "Phase181 sample source asset was not found.",
                        sourceAbsolutePath);
                }
                FileUtil.CopyFileOrDirectory(sourceAbsolutePath, stagingAbsolutePath);
            }

            // Samples~ is intentionally outside the AssetDatabase. Stage a
            // byte-identical temporary copy under Assets so Unity itself creates
            // importer-correct sidecars and opaque GUIDs.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(
                SampleMetadataStagingSampleAssetPath,
                ImportAssetOptions.ForceSynchronousImport);

            for (var index = 0; index < SampleMetadataAssetPaths.Length; index++)
            {
                var sourceAssetPath = SampleMetadataAssetPaths[index];
                var suffix = sourceAssetPath.Substring(SampleDirectoryAssetPath.Length);
                var stagingMetadataPath = ProjectAssetMetadataAbsolutePath(
                    SampleMetadataStagingSampleAssetPath + suffix);
                if (!File.Exists(stagingMetadataPath)
                    || !IsUnityGuid(ReadUnityGuid(stagingMetadataPath)))
                {
                    throw new InvalidOperationException(
                        "Unity did not generate valid metadata for the Phase181 staging asset: "
                        + SampleMetadataStagingSampleAssetPath + suffix);
                }
                FileUtil.CopyFileOrDirectory(
                    stagingMetadataPath,
                    SampleMetadataAbsolutePath(sourceAssetPath));
            }
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        }
        finally
        {
            CleanupStaleSampleMetadataStaging();
        }
    }

    private static void CleanupStaleSampleMetadataStaging()
    {
        var stagingRootAbsolutePath = ProjectAssetPathToAbsolutePath(SampleMetadataStagingAssetPath);
        var stagingMetadataPath = stagingRootAbsolutePath + ".meta";
        if (!Directory.Exists(stagingRootAbsolutePath) && !File.Exists(stagingMetadataPath))
            return;

        if (!IsRecognizedSampleMetadataStaging(stagingRootAbsolutePath))
        {
            throw new InvalidOperationException(
                "Unexpected Phase181 metadata staging content; refusing cleanup: "
                + SampleMetadataStagingAssetPath);
        }

        AssetDatabase.DeleteAsset(SampleMetadataStagingAssetPath);
        if (Directory.Exists(stagingRootAbsolutePath))
            FileUtil.DeleteFileOrDirectory(stagingRootAbsolutePath);
        if (File.Exists(stagingMetadataPath))
            FileUtil.DeleteFileOrDirectory(stagingMetadataPath);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
    }

    private static bool IsRecognizedSampleMetadataStaging(string stagingRootAbsolutePath)
    {
        if (!Directory.Exists(stagingRootAbsolutePath))
            return File.Exists(stagingRootAbsolutePath + ".meta");

        var recognizedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            stagingRootAbsolutePath,
            stagingRootAbsolutePath + ".meta",
        };
        foreach (var sourceAssetPath in SampleMetadataAssetPaths)
        {
            var suffix = sourceAssetPath.Substring(SampleDirectoryAssetPath.Length);
            var stagingAssetPath = ProjectAssetPathToAbsolutePath(
                SampleMetadataStagingSampleAssetPath + suffix);
            recognizedPaths.Add(stagingAssetPath);
            recognizedPaths.Add(stagingAssetPath + ".meta");
        }

        foreach (var path in Directory.EnumerateFileSystemEntries(
                     stagingRootAbsolutePath,
                     "*",
                     SearchOption.AllDirectories))
        {
            if (!recognizedPaths.Contains(path))
                return false;
        }
        return true;
    }

    private static void ValidateSampleMetadata()
    {
        foreach (var assetPath in SampleMetadataAssetPaths)
        {
            var metadataPath = SampleMetadataAbsolutePath(assetPath);
            if (!File.Exists(metadataPath) || !IsUnityGuid(ReadUnityGuid(metadataPath)))
            {
                throw new InvalidOperationException(
                    "Unity did not generate valid metadata for the Phase181 sample asset: " + assetPath);
            }
        }
    }

    private static void ConfigureManager(FoxgloveManager manager)
    {
        var serialized = new SerializedObject(manager);
        SetBoolean(serialized, "_foxgloveOutputEnabled", true);
        SetBoolean(serialized, "_ros2NativeEnabled", true);
        SetBoolean(serialized, "_enableFoxRunInbound", true);
        SetEnumByName(
            serialized,
            "_defaultFoxRunSubscriptionProvider",
            nameof(FoxRunSubscriptionProvider.Ros2Native));
        serialized.ApplyModifiedPropertiesWithoutUndo();
        // Use the public setter too, so a later migration cannot restore the
        // legacy WebSocket provider over this serialized value.
        manager.DefaultFoxRunSubscriptionProvider = FoxRunSubscriptionProvider.Ros2Native;
        EditorUtility.SetDirty(manager);
    }

    private static void ConfigureAcceptanceReceiver(
        Phase181FoxRunCustomRos2InterfaceAcceptance receiver,
        FoxgloveManager manager)
    {
        var serialized = new SerializedObject(receiver);
        var managerProperty = serialized.FindProperty("_manager");
        if (managerProperty == null)
        {
            throw new InvalidOperationException(
                "Phase181 acceptance receiver no longer exposes its Manager reference.");
        }
        managerProperty.objectReferenceValue = manager;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(receiver);
    }

    private static void ValidateAcceptanceScene()
    {
        var absolutePath = Path.Combine(ProjectRoot(), AcceptanceSceneAssetPath);
        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException(
                "Create the tracked Phase181 acceptance scene from the Foxglove menu before building.",
                absolutePath);
        }
        if (!IsUnityGuid(AssetDatabase.AssetPathToGUID(AcceptanceSceneAssetPath)))
            throw new InvalidOperationException("The tracked Phase181 acceptance scene has no valid Unity GUID.");

        var scene = SceneManager.GetSceneByPath(AcceptanceSceneAssetPath);
        var closeAfterValidation = false;
        if (!scene.IsValid() || !scene.isLoaded)
        {
            scene = EditorSceneManager.OpenScene(AcceptanceSceneAssetPath, OpenSceneMode.Additive);
            closeAfterValidation = true;
        }

        try
        {
            var managers = FindComponentsInScene<FoxgloveManager>(scene);
            var receivers = FindComponentsInScene<Phase181FoxRunCustomRos2InterfaceAcceptance>(scene);
            if (managers.Count != 1 || receivers.Count != 1)
            {
                throw new InvalidOperationException(
                    "The Phase181 acceptance scene must contain exactly one FoxgloveManager and one custom ROS2 acceptance receiver.");
            }

            var serialized = new SerializedObject(managers[0]);
            if (!GetBoolean(serialized, "_foxgloveOutputEnabled")
                || !GetBoolean(serialized, "_ros2NativeEnabled")
                || !GetBoolean(serialized, "_enableFoxRunInbound")
                || GetEnumName(serialized, "_defaultFoxRunSubscriptionProvider")
                    != nameof(FoxRunSubscriptionProvider.Ros2Native))
            {
                throw new InvalidOperationException(
                    "The Phase181 acceptance Manager must enable native output, WebSocket output, and native FoxRun subscriptions.");
            }

            var receiverSerialized = new SerializedObject(receivers[0]);
            var receiverManager = receiverSerialized.FindProperty("_manager");
            if (receiverManager == null || receiverManager.objectReferenceValue != managers[0])
            {
                throw new InvalidOperationException(
                    "The Phase181 acceptance receiver must reference the scene's FoxgloveManager.");
            }
        }
        finally
        {
            if (closeAfterValidation && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, removeScene: true);
        }
    }

    private static ActiveRuntime ResolveExactlyOneActiveRuntime()
    {
        var manifestPath = Path.Combine(ProjectRoot(), "Packages", "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Unity package manifest was not found.", manifestPath);

        var activePackages = ReadPackagesWithPrefix(File.ReadAllText(manifestPath), RuntimePackagePrefix);
        if (activePackages.Count != 1)
        {
            throw new InvalidOperationException(
                "Phase181 Player builds require exactly one resolved ROS2 For Unity runtime package; found "
                + activePackages.Count + ".");
        }

        var packageName = activePackages[0];
        var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForPackageName(packageName);
        if (packageInfo == null || string.IsNullOrWhiteSpace(packageInfo.resolvedPath))
            throw new InvalidOperationException("The selected runtime package is not resolved: " + packageName);

        var runtimeManifestPath = Path.Combine(
            packageInfo.resolvedPath,
            "RuntimeSupport",
            "runtime-manifest.json");
        if (!File.Exists(runtimeManifestPath))
            throw new FileNotFoundException("Selected runtime does not contain its runtime manifest.", runtimeManifestPath);

        var manifest = JsonUtility.FromJson<RuntimeManifest>(File.ReadAllText(runtimeManifestPath));
        if (manifest == null
            || !string.Equals(manifest.packageName, packageName, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.rosDistro))
        {
            throw new InvalidOperationException(
                "Selected runtime manifest is incomplete or does not match the resolved package: " + packageName);
        }
        return new ActiveRuntime(packageName, NormalizeDistro(manifest.rosDistro));
    }

    private static void ValidateMatchingTypesupportPackage(string rosDistro)
    {
        var manifestPath = Path.Combine(ProjectRoot(), "Packages", "manifest.json");
        var candidates = ReadPackagesWithPrefix(File.ReadAllText(manifestPath), TypesupportPackagePrefix);
        var expected = TypesupportPackagePrefix + rosDistro + ".win64";
        if (candidates.Count != 1 || !string.Equals(candidates[0], expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Phase181 Player builds require exactly one matching custom ROS2 typesupport add-on. Expected "
                + expected + ", found " + candidates.Count + ".");
        }
    }

    private static StaticInterfaceLock ResolveStaticInterfaceLock()
    {
        var lockPath = Path.Combine(
            RepositoryRoot(),
            "Packages",
            StaticInterfacePackageId,
            StaticInterfaceLockRelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(lockPath))
            throw new FileNotFoundException("Phase181 static interface lock was not found.", lockPath);

        var parsed = JsonUtility.FromJson<StaticInterfaceLock>(File.ReadAllText(lockPath));
        if (parsed == null
            || parsed.interfaceRevision <= 0
            || !IsSha256(parsed.interfaceDigest))
        {
            throw new InvalidOperationException("Phase181 static interface lock is incomplete or malformed.");
        }
        return parsed;
    }

    private static List<string> ReadPackagesWithPrefix(string manifestText, string packagePrefix)
    {
        var matches = Regex.Matches(
            manifestText ?? string.Empty,
            "\\\"(?<package>" + Regex.Escape(packagePrefix) + "[^\\\"]+)\\\"\\s*:",
            RegexOptions.CultureInvariant);
        var packages = new List<string>(matches.Count);
        for (var i = 0; i < matches.Count; i++)
        {
            var package = matches[i].Groups["package"].Value;
            if (!packages.Contains(package))
                packages.Add(package);
        }
        return packages;
    }

    private static List<T> FindComponentsInScene<T>(Scene scene)
        where T : Component
    {
        var components = new List<T>();
        foreach (var root in scene.GetRootGameObjects())
            components.AddRange(root.GetComponentsInChildren<T>(includeInactive: true));
        return components;
    }

    private static void SetBoolean(SerializedObject serialized, string propertyPath, bool value)
    {
        var property = serialized.FindProperty(propertyPath);
        if (property == null || property.propertyType != SerializedPropertyType.Boolean)
            throw new InvalidOperationException("FoxgloveManager boolean property was not found: " + propertyPath);
        property.boolValue = value;
    }

    private static bool GetBoolean(SerializedObject serialized, string propertyPath)
    {
        var property = serialized.FindProperty(propertyPath);
        if (property == null || property.propertyType != SerializedPropertyType.Boolean)
            throw new InvalidOperationException("FoxgloveManager boolean property was not found: " + propertyPath);
        return property.boolValue;
    }

    private static void SetEnumByName(SerializedObject serialized, string propertyPath, string valueName)
    {
        var property = serialized.FindProperty(propertyPath);
        if (property == null || property.propertyType != SerializedPropertyType.Enum)
            throw new InvalidOperationException("FoxgloveManager enum property was not found: " + propertyPath);
        var enumIndex = Array.IndexOf(property.enumNames, valueName);
        if (enumIndex < 0)
            throw new InvalidOperationException(
                "FoxgloveManager enum value was not found for " + propertyPath + ": " + valueName);
        property.enumValueIndex = enumIndex;
    }

    private static string GetEnumName(SerializedObject serialized, string propertyPath)
    {
        var property = serialized.FindProperty(propertyPath);
        if (property == null || property.propertyType != SerializedPropertyType.Enum)
            throw new InvalidOperationException("FoxgloveManager enum property was not found: " + propertyPath);
        return property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length
            ? property.enumNames[property.enumValueIndex]
            : string.Empty;
    }

    private static string SampleMetadataAbsolutePath(string assetPath)
        => PackageAssetPathToAbsolutePath(assetPath) + ".meta";

    private static string PackageAssetPathToAbsolutePath(string assetPath)
        => Path.Combine(RepositoryRoot(), assetPath.Replace('/', Path.DirectorySeparatorChar));

    private static string ProjectAssetMetadataAbsolutePath(string assetPath)
        => ProjectAssetPathToAbsolutePath(assetPath) + ".meta";

    private static string ProjectAssetPathToAbsolutePath(string assetPath)
        => Path.Combine(ProjectRoot(), assetPath.Replace('/', Path.DirectorySeparatorChar));

    private static string ReadUnityGuid(string metadataPath)
    {
        const string prefix = "guid: ";
        foreach (var line in File.ReadLines(metadataPath))
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
                return line.Substring(prefix.Length).Trim();
        }
        return string.Empty;
    }

    private static bool IsUnityGuid(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 32)
            return false;
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (!((character >= '0' && character <= '9')
                  || (character >= 'a' && character <= 'f')
                  || (character >= 'A' && character <= 'F')))
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsSha256(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length != 64)
            return false;
        for (var i = 0; i < value.Length; i++)
        {
            var character = value[i];
            if (!((character >= '0' && character <= '9')
                  || (character >= 'a' && character <= 'f')
                  || (character >= 'A' && character <= 'F')))
            {
                return false;
            }
        }
        return true;
    }

    private static string BuildDirectoryFor(string rosDistro)
    {
        var normalized = NormalizeDistro(rosDistro);
        if (string.IsNullOrEmpty(normalized))
            throw new InvalidOperationException("The selected runtime has no supported ROS distro identifier.");
        return Path.Combine(RepositoryRoot(), "build", "phase181", normalized);
    }

    private static string NormalizeDistro(string rosDistro)
    {
        switch ((rosDistro ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "humble":
            case "jazzy":
            case "lyrical":
                return rosDistro.Trim().ToLowerInvariant();
            default:
                return string.Empty;
        }
    }

    private static void EnsurePathWithinBuildRoot(string path)
    {
        var buildRoot = Path.GetFullPath(Path.Combine(RepositoryRoot(), "build"));
        var candidate = Path.GetFullPath(path);
        var prefix = buildRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? buildRoot
            : buildRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Phase181 Player output must remain under the repository build directory.");
        }
    }

    private static string RelativeToRepository(string absolutePath)
    {
        var repositoryUri = new Uri(EnsureDirectoryUri(RepositoryRoot()));
        var pathUri = new Uri(absolutePath);
        return Uri.UnescapeDataString(repositoryUri.MakeRelativeUri(pathUri).ToString())
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static string EnsureDirectoryUri(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return fullPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? fullPath
            : fullPath + Path.DirectorySeparatorChar;
    }

    private static string ProjectRoot()
        => Directory.GetParent(Application.dataPath)?.FullName
           ?? throw new InvalidOperationException("Could not resolve the Unity project root.");

    private static string RepositoryRoot()
        => Directory.GetParent(ProjectRoot())?.FullName
           ?? throw new InvalidOperationException("Could not resolve the repository root.");

    [Serializable]
    private sealed class RuntimeManifest
    {
        public string packageName;
        public string rosDistro;
    }

    [Serializable]
    private sealed class StaticInterfaceLock
    {
        public int interfaceRevision;
        public string interfaceDigest;

        public int InterfaceRevision => interfaceRevision;
        public string InterfaceDigest => interfaceDigest;
    }

    private readonly struct ActiveRuntime
    {
        public ActiveRuntime(string packageName, string rosDistro)
        {
            PackageName = packageName;
            RosDistro = rosDistro;
        }

        public string PackageName { get; }
        public string RosDistro { get; }
    }

    [Serializable]
    private sealed class Phase181BuildReport
    {
        public string runtimePackage;
        public string rosDistro;
        public string interfacePackage;
        public int interfaceRevision;
        public string interfaceDigest;
        public string[] playerEnvironmentKeys;
        public string acceptanceScene;
        public string executablePath;
        public string playerAutoQuitFlag;
        public int totalErrors;
        public int totalWarnings;
        public ulong outputSize;
    }
}
