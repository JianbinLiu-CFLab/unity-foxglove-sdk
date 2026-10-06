// Copyright 2019-2021 Robotec.ai.
// Modifications Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Xml;

namespace ROS2
{

/// <summary>
/// An internal class responsible for handling checking, proper initialization and shutdown of ROS2cs,
/// </summary>
internal class ROS2ForUnity
{
    private static readonly object lifecycleGate = new object();
    private static volatile bool isInitialized = false;
    private static int ownerCount = 0;
    private static volatile bool shutdownInProgress = false;
    private static bool nativeShutdownCompleted = false;
    private const int MaxShutdownRetryAttempts = 3;
#if UNITY_EDITOR
    private static bool shutdownRetryScheduled = false;
#endif
    private static int shutdownRetryAttempts = 0;
    private const string ros2ForUnityAssetFolderName = "Ros2ForUnity";
    private const string unity2FoxgloveRuntimePackageName = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64";
    private const string unity2FoxgloveRuntimePackageAssetPath =
        "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity";
    private const string expectedRmwImplementation = "rmw_fastrtps_cpp";
    private static readonly string[] SupportedRosVersions = { "foxy", "galactic", "humble", "jazzy", "rolling" };
    private static readonly string SupportedRosVersionsString = String.Join(", ", SupportedRosVersions);
    private static readonly Lazy<string> ros2ForUnityPath = new Lazy<string>(ComputeRos2ForUnityPath);
    private static readonly Lazy<string> pluginPath = new Lazy<string>(ComputePluginPath);
    private XmlDocument ros2csMetadata = new XmlDocument();
    private XmlDocument ros2ForUnityMetadata = new XmlDocument();
    private bool ownsLifecycle;

#if UNITY_EDITOR
    private bool editorCallbacksRegistered;
#endif
#if ENABLE_MONO
    private ConsoleCancelEventHandler ctrlCHandler;
#endif

    public enum Platform
    {
        Windows,
        Linux
    }
    
    public static Platform GetOS()
    {
        if (Application.platform == RuntimePlatform.LinuxEditor || Application.platform == RuntimePlatform.LinuxPlayer)
        {
            return Platform.Linux;
        }
        else if (Application.platform == RuntimePlatform.WindowsEditor || Application.platform == RuntimePlatform.WindowsPlayer)
        {
            return Platform.Windows;
        }
        throw new System.NotSupportedException("Only Linux and Windows are supported");
    }

    private static bool InEditor() {
        return Application.isEditor;
    }
    
    private static string GetOSName()
    {
        switch (GetOS())
        {
            case Platform.Linux:
                return "Linux";
            case Platform.Windows:
                return "Windows";
            default:
                throw new System.NotSupportedException("Only Linux and Windows are supported");
        }
    }
    
    private string GetEnvPathVariableName()
    {
      string envVariable = "LD_LIBRARY_PATH";
      if (GetOS() == Platform.Windows)
      {
          envVariable = "PATH";
      }
      return envVariable;
    }

    private string GetEnvPathVariableValue()
    {
        return Environment.GetEnvironmentVariable(GetEnvPathVariableName());
    }

    private static void SetProcessEnvironmentVariable(string name, string value)
    {
        Ros2ForUnityProcessEnvironmentLease.Set(name, value, GetOS() == Platform.Windows);
    }

    private static void SetProcessEnvironmentPathVariable(string name, string value, string pathEntry, char separator)
    {
        Ros2ForUnityProcessEnvironmentLease.SetPath(
            name,
            value,
            pathEntry,
            separator,
            GetOS() == Platform.Windows);
    }

    public static string GetRos2ForUnityPath()
    {
        return ros2ForUnityPath.Value;
    }

    private static string ComputeRos2ForUnityPath()
    {
        char separator = Path.DirectorySeparatorChar;
        string appDataPath = Application.dataPath;
        string path = appDataPath;

        if (InEditor()) {
            string assetPath = path + separator + ros2ForUnityAssetFolderName;
            if (Directory.Exists(assetPath)) {
                return assetPath;
            }

            // Unity2Foxglove package path support for local packages installed with
            // Package Manager's "Add package from disk..." flow.
#if UNITY_EDITOR
            UnityEditor.PackageManager.PackageInfo runtimePackage =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath(unity2FoxgloveRuntimePackageAssetPath);
            if (runtimePackage != null && !string.IsNullOrEmpty(runtimePackage.resolvedPath)) {
                string resolvedPackagePath = Path.Combine(
                    runtimePackage.resolvedPath,
                    "Runtime",
                    ros2ForUnityAssetFolderName);
                if (Directory.Exists(resolvedPackagePath)) {
                    return resolvedPackagePath;
                }
            }
#endif

            DirectoryInfo dataDirectory = Directory.GetParent(appDataPath);
            if (dataDirectory != null) {
                string packagePath = Path.Combine(
                    dataDirectory.FullName,
                    "Packages",
                    unity2FoxgloveRuntimePackageName,
                    "Runtime",
                    ros2ForUnityAssetFolderName);
                if (Directory.Exists(packagePath)) {
                    return packagePath;
                }
            }

            // Unity2Foxglove package path support: keep upstream asset-folder fallback.
            return assetPath;
        }

        // Player metadata and share files are staged under StreamingAssets by the
        // build preprocessor, which becomes <App>_Data/StreamingAssets at runtime.
        return Path.Combine(Application.streamingAssetsPath, ros2ForUnityAssetFolderName);
    }

    public static string GetPluginPath()
    {
        return pluginPath.Value;
    }

    public static void PrewarmUnityMainThreadPaths()
    {
        _ = ros2ForUnityPath.Value;
        _ = pluginPath.Value;
    }

    private static string ComputePluginPath()
    {
        char separator = Path.DirectorySeparatorChar;
        // Unity copies package plugins to <App>_Data/Plugins for a Player,
        // while the Editor keeps them below the package asset root.
        string path = InEditor() ? GetRos2ForUnityPath() : Application.dataPath;
        path += separator + "Plugins";

        if (InEditor()) {
            path += separator + GetOSName();
        }

        if (InEditor() || GetOS() == Platform.Windows)
        {
           path += separator + "x86_64";
        }

        if (GetOS() == Platform.Windows)
        {
           path = path.Replace("/", "\\");
        }

        return path;
    }

    /// <summary>
    /// Function responsible for setting up of environment paths for standalone builds
    /// </summary>
    /// <description>
    /// Note that on Linux, LD_LIBRARY_PATH as used for dlopen() is determined on process start and this change won't
    /// affect it. Ros2 looks for rmw implementation based on this variable (independently) and the change
    /// is effective for this process, however rmw implementation's dependencies itself are loaded by dynamic linker 
    /// anyway so setting it for Linux is pointless.
    /// </description>
    private void SetEnvPathVariable()
    {
        string currentPath = GetEnvPathVariableValue();
        string pluginPath = GetPluginPath();
        
        char envPathSep = ':';
        if (GetOS() == Platform.Windows)
        {
            envPathSep = ';';
        }

        string normalizedPluginPath = NormalizeEnvPathEntry(pluginPath);
        var comparer = GetOS() == Platform.Windows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var entries = new List<string>();
        var seen = new HashSet<string>(comparer);

        entries.Add(pluginPath);
        seen.Add(normalizedPluginPath);

        if (!string.IsNullOrEmpty(currentPath))
        {
            foreach (string rawEntry in currentPath.Split(new[] { envPathSep }, StringSplitOptions.RemoveEmptyEntries))
            {
                string entry = rawEntry.Trim();
                if (entry.Length == 0)
                {
                    continue;
                }

                if (seen.Add(NormalizeEnvPathEntry(entry)))
                {
                    entries.Add(entry);
                }
            }
        }

        SetProcessEnvironmentPathVariable(
            GetEnvPathVariableName(),
            string.Join(envPathSep.ToString(), entries),
            pluginPath,
            envPathSep);
    }

    private static void SetStandalonePrefixPath()
    {
        string prefixPath = GetRos2ForUnityPath();
        string pluginPrefixPath = GetPluginPath();
        if (Directory.Exists(Path.Combine(pluginPrefixPath, "share")))
        {
            prefixPath = pluginPrefixPath;
        }
        else if (!Directory.Exists(Path.Combine(prefixPath, "share")))
        {
            Debug.LogWarning("Standalone AMENT_PREFIX_PATH fallback has no share directory: " + prefixPath);
        }

        // U2F-LOCAL-PATCH: standalone runtime must not inherit or require a sourced ROS 2 workspace.
        SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);
    }

    private static void SetStandaloneRmwImplementation()
    {
        // U2F-LOCAL-PATCH: standalone Jazzy runtime owns its RMW selection.
        SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", expectedRmwImplementation);
    }

    private static void SetStandaloneRosDistro(string ros2Codename)
    {
        // U2F-LOCAL-PATCH: standalone runtime owns ROS_DISTRO even when Unity was launched from another ROS shell.
        SetProcessEnvironmentVariable("ROS_DISTRO", ros2Codename);
    }

    private static string NormalizeEnvPathEntry(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string trimmed = value.Trim();
        string fastNormalized = trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (LooksNormalizedEnvPathEntry(fastNormalized))
        {
            return fastNormalized;
        }

        try
        {
            trimmed = Path.GetFullPath(trimmed);
        }
        catch
        {
            // Preserve invalid or shell-expanded PATH entries while still de-duplicating exact text.
        }

        return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool LooksNormalizedEnvPathEntry(string value)
    {
        if (string.IsNullOrEmpty(value) || !Path.IsPathRooted(value))
        {
            return false;
        }

        char separator = Path.DirectorySeparatorChar;
        char altSeparator = Path.AltDirectorySeparatorChar;
        return value.IndexOf(separator + "." + separator, StringComparison.Ordinal) < 0
            && value.IndexOf(altSeparator + "." + altSeparator, StringComparison.Ordinal) < 0
            && value.IndexOf(separator + ".." + separator, StringComparison.Ordinal) < 0
            && value.IndexOf(altSeparator + ".." + altSeparator, StringComparison.Ordinal) < 0
            && !value.EndsWith(separator + ".", StringComparison.Ordinal)
            && !value.EndsWith(altSeparator + ".", StringComparison.Ordinal)
            && !value.EndsWith(separator + "..", StringComparison.Ordinal)
            && !value.EndsWith(altSeparator + "..", StringComparison.Ordinal);
    }

    public bool IsStandalone() {
        return Convert.ToBoolean(Convert.ToInt16(GetMetadataValue(ros2csMetadata, "/ros2cs/standalone")));
    }

    public string GetROSVersion()
    {
        string ros2SourcedCodename = GetROSVersionSourced();
        string ros2FromRos4UMetadata = GetMetadataValue(ros2ForUnityMetadata, "/ros2_for_unity/ros2");

        //  Sourced ROS2 libs takes priority
        if (string.IsNullOrEmpty(ros2SourcedCodename)) {
            return ros2FromRos4UMetadata;
        }
        
        return ros2SourcedCodename;
    }

    private static void WarnIfStandaloneRosDistroOverride(string sourcedRosDistro, string packagedRos2Version)
    {
        if (string.IsNullOrEmpty(sourcedRosDistro)
            || string.Equals(sourcedRosDistro, packagedRos2Version, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Debug.LogWarning(
            "Ignoring sourced ROS_DISTRO '" + sourcedRosDistro +
            "' because standalone runtime package provides '" + packagedRos2Version + "'.");
    }

    /// <summary>
    /// Checks if both ros2cs and ros2-for-unity were build for the same ros version as well as
    /// the current sourced ros version matches ros2cs binaries.
    /// </summary>
    public void CheckIntegrity()
    {
        CheckIntegrity(GetROSVersionSourced());
    }

    private void CheckIntegrity(string ros2SourcedCodename)
    {
        string ros2FromRos2csMetadata = GetMetadataValue(ros2csMetadata, "/ros2cs/ros2");
        string ros2FromRos4UMetadata = GetMetadataValue(ros2ForUnityMetadata, "/ros2_for_unity/ros2");

        if (ros2FromRos4UMetadata != ros2FromRos2csMetadata) {
            FailIntegrity(
                "ROS2 versions in 'ros2cs' and 'ros2-for-unity' metadata files are not the same. " +
                "This is caused by mixing versions/builds.");
        }

        if(!IsStandalone() && ros2SourcedCodename != ros2FromRos2csMetadata) {
            FailIntegrity(
                "ROS2 version in 'ros2cs' metadata doesn't match currently sourced version. " +
                "This is caused by mixing versions/builds.");
        }

    }

    private static void FailIntegrity(string errMessage)
    {
        Debug.LogError(errMessage);
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
        throw new InvalidOperationException(errMessage);
#else
        const int ROS_METADATA_MISMATCH_ERROR_CODE = 35;
        Application.Quit(ROS_METADATA_MISMATCH_ERROR_CODE);
        throw new InvalidOperationException(errMessage);
#endif
    }

    public string GetROSVersionSourced()
    {
        return Environment.GetEnvironmentVariable("ROS_DISTRO");
    }

    /// <summary>
    /// Check whether the sourced ROS version is supported.
    /// Only applies to non-standalone plugin versions that do not bundle ROS2 libraries.
    /// </summary>
    private void CheckROSSupport(string ros2Codename)
    {
        if (string.IsNullOrEmpty(ros2Codename))
        {
            string errMessage = "No ROS environment sourced. You need to source your ROS2 " + SupportedRosVersionsString
              + " environment before launching Unity (ROS_DISTRO env variable not found)";
            Debug.LogError(errMessage);
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
            throw new System.InvalidOperationException(errMessage);
#else
            const int ROS_NOT_SOURCED_ERROR_CODE = 33;
            Application.Quit(ROS_NOT_SOURCED_ERROR_CODE);
            throw new System.InvalidOperationException(errMessage);
#endif
        }

        if (Array.IndexOf(SupportedRosVersions, ros2Codename) < 0)
        {
            string errMessage = "Currently sourced ROS version differs from supported one. Sourced: " + ros2Codename
              + ", supported: " + SupportedRosVersionsString + ".";
            Debug.LogError(errMessage);
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
            throw new System.NotSupportedException(errMessage);
#else
            const int ROS_BAD_VERSION_CODE = 34;
            Application.Quit(ROS_BAD_VERSION_CODE);
            throw new System.NotSupportedException(errMessage);
#endif
        } else if (ros2Codename.Equals("rolling") ) {
            Debug.LogWarning("You are using ROS2 rolling version. Bleeding edge version might not work correctly.");
        }
    }

    private void RegisterCtrlCHandler()
    {
#if ENABLE_MONO
        // Il2CPP build does not support Console.CancelKeyPress currently
        ctrlCHandler = (sender, eventArgs) => {
            eventArgs.Cancel = true;
            ShutdownShared();
        };
        Console.CancelKeyPress += ctrlCHandler;
#endif
    }

    private void UnregisterCallbacks()
    {
#if ENABLE_MONO
        if (ctrlCHandler != null)
        {
            Console.CancelKeyPress -= ctrlCHandler;
            ctrlCHandler = null;
        }
#endif
#if UNITY_EDITOR
        if (editorCallbacksRegistered)
        {
            EditorApplication.playModeStateChanged -= this.EditorPlayStateChanged;
            EditorApplication.quitting -= this.DestroyROS2ForUnity;
            editorCallbacksRegistered = false;
        }
#endif
    }

    private void ConnectLoggers()
    {
        Ros2csLogger.SetCallback(LogLevel.ERROR, Debug.LogError);
        Ros2csLogger.SetCallback(LogLevel.WARNING, Debug.LogWarning);
        Ros2csLogger.SetCallback(LogLevel.INFO, Debug.Log);
        Ros2csLogger.SetCallback(LogLevel.DEBUG, Debug.Log);
        Ros2csLogger.LogLevel = LogLevel.WARNING;
    }

    private static void ValidateRmwImplementation(string rmwImpl)
    {
        if (string.Equals(rmwImpl, expectedRmwImplementation, StringComparison.Ordinal))
        {
            return;
        }

        var errMessage = "Unsupported ROS2 RMW implementation '" + rmwImpl + "'. " +
            "This Unity2Foxglove Jazzy Win64 runtime package is validated only with '" +
            expectedRmwImplementation + "'. Ensure RMW_IMPLEMENTATION is unset or set to '" +
            expectedRmwImplementation + "'.";
        Debug.LogError(errMessage);
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
        throw new InvalidOperationException(errMessage);
#else
        const int ROS_BAD_RMW_CODE = 36;
        Application.Quit(ROS_BAD_RMW_CODE);
        throw new InvalidOperationException(errMessage);
#endif
    }

    private static void SuppressRos2csFinalizer()
    {
        try
        {
            var destructorField = typeof(Ros2cs).GetField(
                "destructor",
                BindingFlags.NonPublic | BindingFlags.Static);
            var destructor = destructorField != null
                ? destructorField.GetValue(null)
                : null;
            if (destructor == null)
            {
                Debug.LogError("Unable to suppress Ros2cs finalizer before shutdown: private destructor field is missing or null.");
                return;
            }

            GC.SuppressFinalize(destructor);
        }
        catch (Exception exception)
        {
            Debug.LogError("Unable to suppress Ros2cs finalizer before shutdown: " + exception.Message);
        }
    }

    private string GetMetadataValue(XmlDocument doc, string valuePath)
    {
        if (doc == null || doc.DocumentElement == null)
        {
            throw new InvalidDataException("ROS2 For Unity metadata document is empty while reading " + valuePath);
        }

        XmlNode node = doc.DocumentElement.SelectSingleNode(valuePath);
        if (node == null)
        {
            throw new InvalidDataException("ROS2 For Unity metadata is missing required node " + valuePath);
        }

        return node.InnerText;
    }

    private void LoadMetadata() 
    {
        char separator = Path.DirectorySeparatorChar;
        string ros2csMetadataPath = GetRos2ForUnityPath() + separator + "metadata_ros2cs.xml";
        string ros2ForUnityMetadataPath = GetRos2ForUnityPath() + separator + "metadata_ros2_for_unity.xml";
        try
        {
            ros2csMetadata.Load(ros2csMetadataPath);
            ros2ForUnityMetadata.Load(ros2ForUnityMetadataPath);
        }
        catch (Exception exception) when (exception is FileNotFoundException || exception is XmlException || exception is InvalidDataException)
        {
#if UNITY_EDITOR
            var errMessage = "Could not load ROS2 For Unity metadata files: " + exception.Message +
                " (ros2cs=" + ros2csMetadataPath + ", ros2_for_unity=" + ros2ForUnityMetadataPath + ")";
            EditorApplication.isPlaying = false;
            throw new InvalidDataException(errMessage, exception);
#else
            const int NO_METADATA = 1;
            Application.Quit(NO_METADATA);
#endif
        }
    }

    internal ROS2ForUnity()
    {
        LoadMetadata();
        string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();
        bool standaloneBuild = IsStandalone();
        string currentRos2Version;
        string standalone;
        bool leaseAcquired = false;
        bool nativeInitialized = false;

        lock (lifecycleGate)
        {
            if (shutdownInProgress)
            {
                throw new InvalidOperationException("Ros2 For Unity is shutting down and cannot create a new context reference.");
            }

            if (isInitialized)
            {
                ownerCount++;
                ownsLifecycle = true;
                currentRos2Version = GetROSVersion();
                standalone = standaloneBuild ? "standalone" : "non-standalone";
            }
            else
            {
                Ros2ForUnityProcessEnvironmentLease.Begin();
                leaseAcquired = true;
                try
                {
                    if (standaloneBuild)
                    {
                        SetStandalonePrefixPath();
                        SetStandaloneRmwImplementation();
                    }

                    currentRos2Version = standaloneBuild
                        ? GetMetadataValue(ros2csMetadata, "/ros2cs/ros2")
                        : GetROSVersion();
                    if (standaloneBuild)
                    {
                        SetStandaloneRosDistro(currentRos2Version);
                    }
                    standalone = standaloneBuild ? "standalone" : "non-standalone";

                    CheckROSSupport(currentRos2Version);
                    WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);
                    CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);

                    if (GetOS() == Platform.Windows)
                    {
                        SetEnvPathVariable();
                    }
                    else
                    {
                        ROS2.GlobalVariables.absolutePath = GetPluginPath() + "/";
                        if (currentRos2Version == "foxy")
                        {
                            ROS2.GlobalVariables.preloadLibrary = true;
                            ROS2.GlobalVariables.preloadLibraryName = "librcpputils.so";
                        }
                    }

                    ConnectLoggers();
                    Ros2ForUnityNativePluginBootstrap.SealNativeLibraryRegistration();
                    Ros2cs.Init();
                    nativeInitialized = true;
                    nativeShutdownCompleted = false;
                    isInitialized = true;
                    ownerCount = 1;
                    ownsLifecycle = true;
                }
                catch
                {
                    try { Ros2ForUnityNativePluginBootstrap.ResetNativeLibraryRegistration(); } catch { }
                    ownerCount = 0;
                    ownsLifecycle = false;
                    bool environmentRestored = Ros2ForUnityProcessEnvironmentLease.Abort(GetOS() == Platform.Windows);
                    if (!environmentRestored)
                    {
                        shutdownInProgress = true;
                        shutdownRetryAttempts = 0;
                        Debug.LogError("ROS2 For Unity startup rollback could not fully restore the process environment; cleanup remains pending.");
#if UNITY_EDITOR
                        ScheduleShutdownRetry();
#endif
                    }
                    throw;
                }
            }
        }

        try
        {
            RegisterCtrlCHandler();
            string rmwImpl = nativeInitialized || Ros2cs.Ok()
                ? Ros2cs.GetRMWImplementation()
                : "unknown";
            ValidateRmwImplementation(rmwImpl);
            LogRuntimeInfoWithoutStackTrace("ROS2 version: " + currentRos2Version + ". Build type: " + standalone + ". RMW: " + rmwImpl);
#if UNITY_EDITOR
            EditorApplication.playModeStateChanged += this.EditorPlayStateChanged;
            EditorApplication.quitting += this.DestroyROS2ForUnity;
            editorCallbacksRegistered = true;
#endif
        }
        catch
        {
            try
            {
                if (nativeInitialized || isInitialized)
                    DestroyROS2ForUnity();
            }
            catch { }
            if (leaseAcquired)
            {
                bool shutdownPending;
                lock (lifecycleGate)
                {
                    shutdownPending = isInitialized || shutdownInProgress;
                    if (shutdownPending)
                    {
                        shutdownInProgress = true;
                        shutdownRetryAttempts = 0;
                    }
                }
                if (shutdownPending)
                {
#if UNITY_EDITOR
                    ScheduleShutdownRetry();
#endif
                }
            }
            throw;
        }
    }

    private static void ThrowIfUninitialized(string callContext)
    {
        if (!isInitialized)
        {
            throw new InvalidOperationException("Ros2 For Unity is not initialized, can't " + callContext);
        }
    }

    /// <summary>
    /// Check if ROS2 module is properly initialized and no shutdown was called yet
    /// </summary>
    /// <returns>The state of ROS2 module. Should be checked before attempting to create or use pubs/subs</returns>
    public bool Ok()
    {
        if (!isInitialized)
        {
            return false;
        }
        return Ros2cs.Ok();
    }

    internal void DestroyROS2ForUnity()
    {
        UnregisterCallbacks();

        var shouldShutdown = false;
        lock (lifecycleGate)
        {
            if (!ownsLifecycle)
                return;

            ownsLifecycle = false;
            ownerCount = Math.Max(0, ownerCount - 1);
            shouldShutdown = ownerCount == 0 && isInitialized && !shutdownInProgress;
            if (shouldShutdown)
                shutdownInProgress = true;
        }

        if (shouldShutdown)
            CompleteShutdownShared();
    }

    internal static void RetryPendingShutdown()
    {
        bool restoreOnly;
        lock (lifecycleGate)
        {
            if (!shutdownInProgress)
                return;

            restoreOnly = !isInitialized;
        }

        if (restoreOnly)
            FinishShutdownShared();
        else
            CompleteShutdownShared();
    }

    internal static bool IsShutdownCompleteForEditor()
    {
        lock (lifecycleGate)
        {
            return !isInitialized && !shutdownInProgress;
        }
    }

    private static void CompleteShutdownShared()
    {
        if (!ROS2UnityComponent.StopAllExecutorsForRosShutdown())
        {
#if UNITY_EDITOR
            ScheduleShutdownRetry();
#else
            TryCompleteShutdownWithBoundedRetry();
#endif
            return;
        }

        FinishShutdownShared();
    }

#if UNITY_EDITOR
    private static void ScheduleShutdownRetry()
    {
        if (shutdownRetryScheduled || !shutdownInProgress)
            return;

        if (shutdownRetryAttempts >= MaxShutdownRetryAttempts)
        {
            Debug.LogError("ROS2 For Unity shutdown deferred: executor threads remain active after bounded retries.");
            return;
        }

        shutdownRetryAttempts++;
        shutdownRetryScheduled = true;
        EditorApplication.delayCall += RetryShutdownOnMainThread;
    }

    private static void RetryShutdownOnMainThread()
    {
        EditorApplication.delayCall -= RetryShutdownOnMainThread;
        shutdownRetryScheduled = false;
        if (!shutdownInProgress)
            return;

        if (ROS2UnityComponent.StopAllExecutorsForRosShutdown())
        {
            FinishShutdownShared();
            return;
        }

        ScheduleShutdownRetry();
    }
#else
    private static void TryCompleteShutdownWithBoundedRetry()
    {
        for (int attempt = 0; attempt < MaxShutdownRetryAttempts - 1; attempt++)
        {
            if (ROS2UnityComponent.StopAllExecutorsForRosShutdown())
            {
                FinishShutdownShared();
                return;
            }
        }

        Debug.LogError("ROS2 For Unity shutdown deferred: executor threads remain active after bounded retries.");
    }
#endif

    private static void FinishShutdownShared()
    {
        bool retryEnvironmentRestore = false;
        bool retryNativeShutdown = false;
        lock (lifecycleGate)
        {
            if (!isInitialized)
            {
                ownerCount = 0;
                bool environmentRestored = Ros2ForUnityProcessEnvironmentLease.Restore(
                    Environment.OSVersion.Platform == PlatformID.Win32NT);
                if (environmentRestored)
                {
                    shutdownInProgress = false;
                    shutdownRetryAttempts = 0;
                }
                else
                {
                    shutdownInProgress = true;
                    shutdownRetryAttempts = 0;
                    retryEnvironmentRestore = true;
                    Debug.LogError("ROS2 For Unity process environment lease could not be fully restored; shutdown remains pending.");
                }
            }
            else
            {
                LogRuntimeInfoWithoutStackTrace("Shutting down Ros2 For Unity");
                bool nativeShutdownSucceeded = nativeShutdownCompleted;
                if (!nativeShutdownSucceeded)
                {
                    try
                    {
                        SuppressRos2csFinalizer();
                        Ros2cs.Shutdown();
                        nativeShutdownCompleted = true;
                        nativeShutdownSucceeded = true;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        retryNativeShutdown = true;
                    }
                }

                if (nativeShutdownSucceeded)
                {
                    try
                    {
                        Ros2ForUnityNativePluginBootstrap.ResetNativeLibraryRegistration();
                        isInitialized = false;
                        ownerCount = 0;
                        nativeShutdownCompleted = false;
                    }
                    catch (Exception exception)
                    {
                        Debug.LogException(exception);
                        retryNativeShutdown = true;
                    }
                }

                if (!isInitialized)
                {
                    ownerCount = 0;
                    bool environmentRestored = Ros2ForUnityProcessEnvironmentLease.Restore(
                        Environment.OSVersion.Platform == PlatformID.Win32NT);
                    if (environmentRestored)
                    {
                        shutdownInProgress = false;
                        shutdownRetryAttempts = 0;
                    }
                    else
                    {
                        shutdownInProgress = true;
                        shutdownRetryAttempts = 0;
                        retryEnvironmentRestore = true;
                        Debug.LogError("ROS2 For Unity process environment lease could not be fully restored; shutdown remains pending.");
                    }
                }
                else
                {
                    shutdownInProgress = true;
                    shutdownRetryAttempts = 0;
                    retryNativeShutdown = true;
                }
            }
        }

#if UNITY_EDITOR
        if (retryEnvironmentRestore || retryNativeShutdown)
            ScheduleShutdownRetry();
#endif
    }

    private static void LogRuntimeInfoWithoutStackTrace(string message)
    {
        Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message);
    }

#if UNITY_EDITOR
    void EditorPlayStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            DestroyROS2ForUnity();
        }
    }
#endif
}


/// <summary>Tracks and conditionally restores process-wide environment values owned by the runtime.</summary>
internal static class Ros2ForUnityProcessEnvironmentLease
{
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int _wputenv_s(string name, string value);
#endif
    private sealed class Entry
    {
        internal readonly string Previous;
        internal string Applied;
        internal bool HasApplied;
        internal string PendingRestore;
        internal bool HasPendingRestore;
        internal string PathEntry;
        internal char PathSeparator;

        internal Entry(string previous)
        {
            Previous = previous;
        }
    }

    private static readonly object Gate = new object();
    private static readonly Dictionary<string, Entry> Entries =
        new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    private static bool active;
    private static bool restorePending;

    internal static void Begin()
    {
        lock (Gate)
        {
            if (active || restorePending)
                throw new InvalidOperationException(
                    "The ROS2 For Unity process environment lease is still pending cleanup.");

            Entries.Clear();
            active = true;
        }
    }

    internal static void Set(string name, string value, bool windows)
    {
        if (String.IsNullOrEmpty(name))
            throw new ArgumentException("Environment variable name is required.", nameof(name));

        lock (Gate)
        {
            if (!active)
                throw new InvalidOperationException(
                    "The ROS2 For Unity process environment lease is not active.");

            if (restorePending)
                throw new InvalidOperationException(
                    "The ROS2 For Unity process environment lease is still pending cleanup.");

            if (!Entries.TryGetValue(name, out var entry))
            {
                entry = new Entry(Environment.GetEnvironmentVariable(name));
                Entries.Add(name, entry);
            }

            string rollbackValue = Environment.GetEnvironmentVariable(name);
            string previousApplied = entry.Applied;
            bool hadApplied = entry.HasApplied;
            entry.Applied = value;
            entry.HasApplied = true;
            try
            {
                Apply(name, value, windows);
                entry.HasPendingRestore = false;
                entry.PendingRestore = null;
            }
            catch
            {
                try
                {
                    Apply(name, rollbackValue, windows);
                    if ((hadApplied && String.Equals(rollbackValue, previousApplied, StringComparison.Ordinal))
                        || (!hadApplied && String.Equals(rollbackValue, entry.Previous, StringComparison.Ordinal)))
                    {
                        entry.Applied = rollbackValue;
                        entry.HasApplied = hadApplied;
                        entry.HasPendingRestore = false;
                        entry.PendingRestore = null;
                    }
                    else
                    {
                        Entries.Remove(name);
                    }
                }
                catch
                {
                    restorePending = true;
                    entry.PendingRestore = rollbackValue;
                    entry.HasPendingRestore = true;
                }
                throw;
            }
        }
    }

    internal static void SetPath(
        string name,
        string value,
        string pathEntry,
        char separator,
        bool windows)
    {
        Set(name, value, windows);
        lock (Gate)
        {
            if (Entries.TryGetValue(name, out var entry)
                && !ContainsPathEntry(entry.Previous, pathEntry, separator))
            {
                entry.PathEntry = pathEntry;
                entry.PathSeparator = separator;
            }
        }
    }

    internal static bool Restore(bool windows)
    {
        lock (Gate)
        {
            if (!active)
                return true;

            bool success = true;
            var completed = new List<string>();
            foreach (var pair in Entries)
            {
                var entry = pair.Value;
                var current = Environment.GetEnvironmentVariable(pair.Key);
                if (entry.HasPendingRestore
                    && String.Equals(current, entry.PendingRestore, StringComparison.Ordinal))
                {
                    try
                    {
                        ApplyNativeIfSupported(pair.Key, entry.PendingRestore, windows);
                        completed.Add(pair.Key);
                    }
                    catch (Exception exception)
                    {
                        success = false;
                        restorePending = true;
                        Debug.LogException(exception);
                    }
                    continue;
                }

                entry.HasPendingRestore = false;
                entry.PendingRestore = null;
                if (!entry.HasApplied
                    || !String.Equals(current, entry.Applied, StringComparison.Ordinal))
                {
                    if (entry.PathEntry != null
                        && TryRemovePathEntry(
                            current,
                            entry.PathEntry,
                            entry.PathSeparator,
                            out var restoredPath))
                    {
                        try
                        {
                            Apply(pair.Key, restoredPath, windows);
                            entry.HasPendingRestore = false;
                            entry.PendingRestore = null;
                            completed.Add(pair.Key);
                        }
                        catch (Exception exception)
                        {
                            success = false;
                            restorePending = true;
                            entry.PendingRestore = restoredPath;
                            entry.HasPendingRestore = true;
                            Debug.LogException(exception);
                        }
                        continue;
                    }

                    // A caller changed the managed value. Preserve that change,
                    // and synchronize the native CRT view on Windows as well.
                    if (windows)
                    {
                        try
                        {
                            ApplyNative(pair.Key, current);
                        }
                        catch (Exception exception)
                        {
                            success = false;
                            restorePending = true;
                            entry.PendingRestore = current;
                            entry.HasPendingRestore = true;
                            Debug.LogException(exception);
                            continue;
                        }
                    }
                    completed.Add(pair.Key);
                    continue;
                }

                try
                {
                    Apply(pair.Key, pair.Value.Previous, windows);
                    entry.HasPendingRestore = false;
                    entry.PendingRestore = null;
                    completed.Add(pair.Key);
                }
                catch (Exception exception)
                {
                    success = false;
                    restorePending = true;
                    entry.PendingRestore = pair.Value.Previous;
                    entry.HasPendingRestore = true;
                    Debug.LogException(exception);
                }
            }

            foreach (var name in completed)
                Entries.Remove(name);

            if (Entries.Count == 0)
            {
                active = false;
                restorePending = false;
            }

            return success && !active;
        }
    }

    private static bool ContainsPathEntry(string path, string entry, char separator)
    {
        if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(entry))
            return false;

        foreach (var part in path.Split(new[] { separator }, StringSplitOptions.None))
        {
            if (String.Equals(part.Trim(), entry, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool TryRemovePathEntry(
        string path,
        string entry,
        char separator,
        out string restoredPath)
    {
        restoredPath = path;
        if (String.IsNullOrEmpty(path) || String.IsNullOrEmpty(entry))
            return false;

        var parts = path.Split(new[] { separator }, StringSplitOptions.None);
        var kept = new List<string>(parts.Length);
        var removed = false;
        foreach (var part in parts)
        {
            if (!removed
                && String.Equals(part.Trim(), entry, StringComparison.OrdinalIgnoreCase))
            {
                removed = true;
                continue;
            }
            kept.Add(part);
        }

        if (!removed)
            return false;

        restoredPath = kept.Count == 0 ? String.Empty : String.Join(separator.ToString(), kept);
        return !String.Equals(restoredPath, path, StringComparison.Ordinal);
    }

    internal static bool Abort(bool windows)
    {
        return Restore(windows);
    }

    private static void Apply(string name, string value, bool windows)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (windows)
            ApplyNative(name, value);
    }

    private static void ApplyNative(string name, string value)
    {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        int result = _wputenv_s(name, value ?? String.Empty);
        if (result != 0)
        {
            throw new InvalidOperationException(
                "Failed to set Windows CRT environment variable '" + name
                + "' (ucrtbase _wputenv_s returned " + result + ")");
        }
#else
        throw new PlatformNotSupportedException("Windows CRT environment updates are only supported on Windows.");
#endif
    }

    private static void ApplyNativeIfSupported(string name, string value, bool windows)
    {
        if (windows)
            ApplyNative(name, value);
    }
}

}  // namespace ROS2
