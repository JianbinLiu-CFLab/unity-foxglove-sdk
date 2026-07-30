// Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
// SPDX-License-Identifier: Apache-2.0
//
// Module: ManualAcceptance/Phase179
// Purpose: Create the tracked acceptance scene and build its isolated Windows Player.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Unity.FoxgloveSDK.Components;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
using Unity2Foxglove.Ros2ForUnity.Native;
#endif

/// <summary>
/// Owns only the reproducible Phase179 acceptance scene and Player build.
/// It deliberately reads the resolved runtime package directly rather than
/// binding this project-level tool to optional-package internal editor APIs.
/// </summary>
public static class Phase179AcceptancePlayerBuilder
{
    public const string AcceptanceSceneAssetPath =
        "Assets/Scenes/Phase179FoxRunRos2NativeSubscribeAcceptance.unity";

    private const string RuntimePackagePrefix =
        "dev.unity2foxglove.ros2forunity.runtime.";
    private const string ExecutableName =
        "Phase179FoxRunRos2NativeSubscribe.exe";
    private const string SampleDirectoryAssetPath =
        "Packages/dev.unity2foxglove.ros2forunity/Samples~/FoxRun ROS2 Native Subscribe";
    private const string SampleMetadataStagingAssetPath =
        "Assets/__Phase179SampleMetadataStaging";
    private const string SampleMetadataStagingSampleAssetPath =
        SampleMetadataStagingAssetPath + "/FoxRun ROS2 Native Subscribe";

    private static readonly string[] SampleMetadataAssetPaths =
    {
        SampleDirectoryAssetPath,
        SampleDirectoryAssetPath + "/Phase179FoxRunRos2NativeSubscribe.cs",
        SampleDirectoryAssetPath + "/README.md",
    };

    [MenuItem("Foxglove/Manual Acceptance/Phase179/Create Acceptance Scene")]
    public static void CreateAcceptanceScene()
    {
        EnsureSampleMetadata();
        var projectRoot = ProjectRoot();
        var sceneAbsolutePath = Path.Combine(projectRoot, AcceptanceSceneAssetPath);
        if (File.Exists(sceneAbsolutePath) || File.Exists(sceneAbsolutePath + ".meta"))
        {
            throw new InvalidOperationException(
                "The Phase179 acceptance scene already exists. Refusing to overwrite tracked manual-acceptance evidence: "
                + AcceptanceSceneAssetPath);
        }

        var acceptanceScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var managerObject = new GameObject("FoxgloveManager");
            SceneManager.MoveGameObjectToScene(managerObject, acceptanceScene);
            managerObject.SetActive(false);
            var manager = managerObject.AddComponent<FoxgloveManager>();
            ConfigureManager(manager);

            var receiverObject = new GameObject("Phase179 ROS2 Native Subscribe Acceptance");
            SceneManager.MoveGameObjectToScene(receiverObject, acceptanceScene);
            receiverObject.SetActive(false);
            var receiver = receiverObject.AddComponent<Phase179FoxRunRos2NativeSubscribeAcceptance>();
            ConfigureAcceptanceReceiver(receiver, manager);
            managerObject.SetActive(true);
            receiverObject.SetActive(true);

            if (!EditorSceneManager.SaveScene(acceptanceScene, sceneAbsolutePath, saveAsCopy: false))
                throw new IOException("Unity did not save the Phase179 acceptance scene: " + AcceptanceSceneAssetPath);
        }
        finally
        {
            EditorSceneManager.CloseScene(acceptanceScene, removeScene: true);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(AcceptanceSceneAssetPath, ImportAssetOptions.ForceSynchronousImport);
        var sceneGuid = AssetDatabase.AssetPathToGUID(AcceptanceSceneAssetPath);
        if (!IsUnityGuid(sceneGuid))
            throw new InvalidOperationException(
                "Unity did not generate a valid GUID for the Phase179 acceptance scene.");

        Debug.Log(
            "[Phase179] Created tracked native ROS2 acceptance scene at "
            + AcceptanceSceneAssetPath + ".",
            AssetDatabase.LoadAssetAtPath<SceneAsset>(AcceptanceSceneAssetPath));
    }

    [MenuItem("Foxglove/Manual Acceptance/Phase179/Generate Sample Metadata")]
    public static void GenerateSampleMetadata()
    {
        EnsureSampleMetadata();
        Debug.Log(
            "[Phase179] Unity generated valid metadata for the FoxRun native ROS2 sample assets.");
    }

    [MenuItem("Foxglove/Manual Acceptance/Phase179/Build Windows Standalone64")]
    public static void BuildWindowsStandalone64()
        => BuildWindowsStandalone64Core();

    /// <summary>
    /// Batch entry point. Invoke only after Unity is not already editing the
    /// project: <c>-executeMethod Phase179AcceptancePlayerBuilder.BuildWindowsStandalone64FromCommandLine</c>.
    /// </summary>
    public static void BuildWindowsStandalone64FromCommandLine()
        => BuildWindowsStandalone64Core();

    private static void BuildWindowsStandalone64Core()
    {
        EnsureSampleMetadata();
        ValidateAcceptanceScene();
        var runtime = ResolveExactlyOneActiveRuntime();
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
            options = BuildOptions.None
        };

        Debug.Log(
            "[Phase179] Building WindowsStandalone64 native ROS2 acceptance Player for "
            + runtime.PackageName + " (" + runtime.RosDistro + ").");
        var buildReport = BuildPipeline.BuildPlayer(options);
        var outputReport = new Phase179BuildReport
        {
            runtimePackage = runtime.PackageName,
            runtimeId = runtime.RuntimeId,
            rosDistro = runtime.RosDistro,
            defaultRmwImplementation = runtime.DefaultRmwImplementation,
            supportedRmwImplementations = runtime.SupportedRmwImplementations,
            communicationModes = runtime.CommunicationModes,
            runtimeArtifactSha256 = runtime.ArtifactSha256,
            runtimeManifestSha256 = runtime.RuntimeManifestSha256,
            acceptanceScene = AcceptanceSceneAssetPath,
            executablePath = RelativeToRepository(executablePath),
            buildTarget = BuildTarget.StandaloneWindows64.ToString(),
            totalErrors = buildReport.summary.totalErrors,
            totalWarnings = buildReport.summary.totalWarnings,
            outputSize = buildReport.summary.totalSize
        };
        var reportPath = Path.Combine(buildDirectory, "phase179-runtime-build-report.json");
        File.WriteAllText(reportPath, JsonUtility.ToJson(outputReport, prettyPrint: true));

        if (buildReport.summary.totalErrors != 0)
        {
            throw new InvalidOperationException(
                "Phase179 WindowsStandalone64 Player build failed with "
                + buildReport.summary.totalErrors + " errors. See " + reportPath);
        }

        Debug.Log(
            "[Phase179] Built WindowsStandalone64 Player: " + executablePath
            + " (report: " + reportPath + ").");
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
                "Phase179 sample metadata is incomplete. Refusing to overwrite existing Unity GUIDs; restore or remove the partial set before generation.");
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
                "Phase179 metadata staging could not be cleared: " + SampleMetadataStagingAssetPath);
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
                var stagingAbsolutePath = ProjectAssetPathToAbsolutePath(SampleMetadataStagingSampleAssetPath + suffix);
                if (!File.Exists(sourceAbsolutePath))
                {
                    throw new FileNotFoundException(
                        "Phase179 sample source asset was not found.",
                        sourceAbsolutePath);
                }

                FileUtil.CopyFileOrDirectory(sourceAbsolutePath, stagingAbsolutePath);
            }

            // Samples~ is intentionally excluded from the AssetDatabase. Stage a
            // byte-identical temporary copy under Assets so Unity, rather than this
            // tool, creates importer-correct sidecars and their opaque GUIDs.
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
                if (!File.Exists(stagingMetadataPath) || !IsUnityGuid(ReadUnityGuid(stagingMetadataPath)))
                {
                    throw new InvalidOperationException(
                        "Unity did not generate valid metadata for the Phase179 staging asset: "
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
                "Unexpected Phase179 metadata staging content; refusing cleanup: "
                + SampleMetadataStagingAssetPath);
        }

        // The fixed staging root contains only copies created by this tool. Its
        // prior attempt may have been interrupted by an OS copy dialog, so remove
        // a recognized incomplete root before retrying rather than touching any
        // package-side GUID.
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
            var stagingAssetPath = ProjectAssetPathToAbsolutePath(SampleMetadataStagingSampleAssetPath + suffix);
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
                    "Unity did not generate valid metadata for the Phase179 sample asset: " + assetPath);
            }
        }
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

    private static void ConfigureManager(FoxgloveManager manager)
    {
        var serialized = new SerializedObject(manager);
        SetBoolean(serialized, "_foxgloveOutputEnabled", false);
        SetBoolean(serialized, "_enableFoxRunInbound", true);
        serialized.ApplyModifiedPropertiesWithoutUndo();
#if UNITY2FOXGLOVE_ROS2_FOR_UNITY
        if (manager.GetComponent<FoxRunRos2TransportProvider>() == null)
            manager.gameObject.AddComponent<FoxRunRos2TransportProvider>();
        manager.ConfigureFoxRunTransports(
            Array.Empty<string>(),
            subscriptionsEnabled: true,
            FoxRunRos2TransportProvider.IdValue);
#else
        throw new InvalidOperationException(
            "Phase179 requires an active ROS2 For Unity runtime package.");
#endif
        EditorUtility.SetDirty(manager);
    }

    private static void ConfigureAcceptanceReceiver(
        Phase179FoxRunRos2NativeSubscribeAcceptance receiver,
        FoxgloveManager manager)
    {
        var serialized = new SerializedObject(receiver);
        var managerProperty = serialized.FindProperty("_manager");
        if (managerProperty == null)
            throw new InvalidOperationException("Phase179 acceptance receiver no longer exposes its Manager reference.");
        managerProperty.objectReferenceValue = manager;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(receiver);
    }

    private static void SetBoolean(SerializedObject serialized, string propertyPath, bool value)
    {
        var property = serialized.FindProperty(propertyPath);
        if (property == null)
            throw new InvalidOperationException("FoxgloveManager serialized property was not found: " + propertyPath);
        property.boolValue = value;
    }

    private static bool GetBoolean(SerializedObject serialized, string propertyPath)
    {
        var property = serialized.FindProperty(propertyPath);
        if (property == null || property.propertyType != SerializedPropertyType.Boolean)
            throw new InvalidOperationException("FoxgloveManager boolean property was not found: " + propertyPath);
        return property.boolValue;
    }

    private static void ValidateAcceptanceScene()
    {
        var absolutePath = Path.Combine(ProjectRoot(), AcceptanceSceneAssetPath);
        if (!File.Exists(absolutePath))
        {
            throw new FileNotFoundException(
                "Create the tracked Phase179 acceptance scene from the Foxglove menu before building.",
                absolutePath);
        }

        var sceneGuid = AssetDatabase.AssetPathToGUID(AcceptanceSceneAssetPath);
        if (!IsUnityGuid(sceneGuid))
            throw new InvalidOperationException("The tracked Phase179 acceptance scene has no valid Unity GUID.");

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
            var receivers = FindComponentsInScene<Phase179FoxRunRos2NativeSubscribeAcceptance>(scene);
            if (managers.Count != 1 || receivers.Count != 1)
            {
                throw new InvalidOperationException(
                    "The Phase179 acceptance scene must contain exactly one FoxgloveManager and one native subscription receiver.");
            }

            var manager = managers[0];
            var receiver = receivers[0];
            var managerSerialized = new SerializedObject(manager);
            if (GetBoolean(managerSerialized, "_foxgloveOutputEnabled")
                || !GetBoolean(managerSerialized, "_enableFoxRunInbound"))
            {
                throw new InvalidOperationException(
                    "The Phase179 acceptance Manager must keep WebSocket output disabled and FoxRun subscriptions enabled.");
            }

            var receiverSerialized = new SerializedObject(receiver);
            var receiverManager = receiverSerialized.FindProperty("_manager");
            if (receiverManager == null || receiverManager.objectReferenceValue != manager)
            {
                throw new InvalidOperationException(
                    "The Phase179 acceptance receiver must reference the scene's FoxgloveManager.");
            }
        }
        finally
        {
            if (closeAfterValidation && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, removeScene: true);
        }
    }

    private static List<T> FindComponentsInScene<T>(Scene scene)
        where T : Component
    {
        var components = new List<T>();
        foreach (var root in scene.GetRootGameObjects())
            components.AddRange(root.GetComponentsInChildren<T>(includeInactive: true));
        return components;
    }

    private static ActiveRuntime ResolveExactlyOneActiveRuntime()
    {
        var manifestPath = Path.Combine(ProjectRoot(), "Packages", "manifest.json");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("Unity package manifest was not found.", manifestPath);

        var activePackages = ReadRuntimePackageNames(File.ReadAllText(manifestPath));
        if (activePackages.Count != 1)
        {
            throw new InvalidOperationException(
                "Phase179 Player builds require exactly one resolved ROS2 For Unity runtime package; found "
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

        var manifestText = File.ReadAllText(runtimeManifestPath);
        var manifest = JsonUtility.FromJson<RuntimeManifest>(manifestText);
        if (manifest == null
            || !string.Equals(manifest.packageName, packageName, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(manifest.rosDistro)
            || string.IsNullOrWhiteSpace(manifest.runtimeId))
        {
            throw new InvalidOperationException(
                "Selected runtime manifest is incomplete or does not match the resolved package: " + packageName);
        }

        return new ActiveRuntime(
            manifest.packageName,
            manifest.runtimeId,
            NormalizeDistro(manifest.rosDistro),
            FirstNonEmpty(manifest.defaultRmwImplementation, manifest.rmwImplementation),
            manifest.supportedRmwImplementations ?? Array.Empty<string>(),
            manifest.communicationModes ?? Array.Empty<RuntimeCommunicationMode>(),
            manifest.artifactSha256 ?? string.Empty,
            Sha256Hex(manifestText));
    }

    private static List<string> ReadRuntimePackageNames(string manifestText)
    {
        var matches = Regex.Matches(
            manifestText ?? string.Empty,
            "\\\"(?<package>" + Regex.Escape(RuntimePackagePrefix) + "[^\\\"]+)\\\"\\s*:",
            RegexOptions.CultureInvariant);
        var packages = new List<string>(matches.Count);
        for (var i = 0; i < matches.Count; i++)
        {
            var packageName = matches[i].Groups["package"].Value;
            if (!packages.Contains(packageName))
                packages.Add(packageName);
        }
        return packages;
    }

    private static string BuildDirectoryFor(string rosDistro)
    {
        var normalizedDistro = NormalizeDistro(rosDistro);
        if (string.IsNullOrEmpty(normalizedDistro))
            throw new InvalidOperationException("The selected runtime has no safe ROS distro identifier.");
        return Path.Combine(RepositoryRoot(), "build", "phase179", normalizedDistro);
    }

    private static string NormalizeDistro(string rosDistro)
    {
        rosDistro = (rosDistro ?? string.Empty).Trim().ToLowerInvariant();
        switch (rosDistro)
        {
            case "humble":
            case "jazzy":
            case "lyrical":
                return rosDistro;
            default:
                return string.Empty;
        }
    }

    private static string ProjectRoot()
        => Directory.GetParent(Application.dataPath)?.FullName
           ?? throw new InvalidOperationException("Could not resolve the Unity project root.");

    private static string RepositoryRoot()
        => Directory.GetParent(ProjectRoot())?.FullName
           ?? throw new InvalidOperationException("Could not resolve the repository root.");

    private static void EnsurePathWithinBuildRoot(string path)
    {
        var buildRoot = Path.GetFullPath(Path.Combine(RepositoryRoot(), "build"));
        var candidate = Path.GetFullPath(path);
        var prefix = buildRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? buildRoot
            : buildRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Phase179 Player output must remain under the repository build directory.");
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
                return false;
        }
        return true;
    }

    private static string FirstNonEmpty(string preferred, string fallback)
        => !string.IsNullOrWhiteSpace(preferred) ? preferred : fallback ?? string.Empty;

    private static string Sha256Hex(string value)
    {
        using (var algorithm = System.Security.Cryptography.SHA256.Create())
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
            var hash = algorithm.ComputeHash(bytes);
            var builder = new System.Text.StringBuilder(hash.Length * 2);
            for (var i = 0; i < hash.Length; i++)
                builder.Append(hash[i].ToString("x2"));
            return builder.ToString();
        }
    }

    [Serializable]
    private sealed class RuntimeManifest
    {
        public string runtimeId;
        public string packageName;
        public string rosDistro;
        public string rmwImplementation;
        public string defaultRmwImplementation;
        public string artifactSha256;
        public string[] supportedRmwImplementations;
        public RuntimeCommunicationMode[] communicationModes;
    }

    [Serializable]
    private sealed class RuntimeCommunicationMode
    {
        public string id;
        public string displayName;
        public string rmwImplementation;
        public bool @default;
    }

    private readonly struct ActiveRuntime
    {
        public ActiveRuntime(
            string packageName,
            string runtimeId,
            string rosDistro,
            string defaultRmwImplementation,
            string[] supportedRmwImplementations,
            RuntimeCommunicationMode[] communicationModes,
            string artifactSha256,
            string runtimeManifestSha256)
        {
            PackageName = packageName;
            RuntimeId = runtimeId;
            RosDistro = rosDistro;
            DefaultRmwImplementation = defaultRmwImplementation;
            SupportedRmwImplementations = supportedRmwImplementations;
            CommunicationModes = communicationModes;
            ArtifactSha256 = artifactSha256;
            RuntimeManifestSha256 = runtimeManifestSha256;
        }

        public string PackageName { get; }
        public string RuntimeId { get; }
        public string RosDistro { get; }
        public string DefaultRmwImplementation { get; }
        public string[] SupportedRmwImplementations { get; }
        public RuntimeCommunicationMode[] CommunicationModes { get; }
        public string ArtifactSha256 { get; }
        public string RuntimeManifestSha256 { get; }
    }

    [Serializable]
    private sealed class Phase179BuildReport
    {
        public string runtimePackage;
        public string runtimeId;
        public string rosDistro;
        public string defaultRmwImplementation;
        public string[] supportedRmwImplementations;
        public RuntimeCommunicationMode[] communicationModes;
        public string runtimeArtifactSha256;
        public string runtimeManifestSha256;
        public string acceptanceScene;
        public string executablePath;
        public string buildTarget;
        public int totalErrors;
        public int totalWarnings;
        public ulong outputSize;
    }
}
