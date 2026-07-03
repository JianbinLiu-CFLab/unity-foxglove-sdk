// Copyright 2019-2021 Robotec.ai.
// Modifications Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
//
// Fork modifications:
// - Added Jazzy/Lyrical distro support and Lyrical ROS2CS_SPIN_FALLBACK setup.
// - Added Unicode Windows CRT environment writes for standalone native getenv callers.
// - Added reference-counted init/shutdown, editor shutdown hooks, and standalone runtime path probing.
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
using System.Runtime.InteropServices;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Xml;

namespace ROS2
{

/// <summary>
/// Handles ROS2cs validation, initialization, and shutdown for R2FU.
/// Wraps reference-counted init/shutdown so multiple callers can share one ROS 2 context.
/// </summary>
internal class ROS2ForUnity : IDisposable
{
    private static bool isInitialized = false;
    private static readonly object initMutex = new object();
    private static bool shutdownInProgress = false;
    private static int referenceCount = 0;
    // Prevents repeatedly prepending the plugin path across multiple ROS2ForUnity instances.
    private static bool pathConfigured = false;
    private static string ros2ForUnityAssetFolderName = "Ros2ForUnity";
    private const string unity2FoxgloveRuntimePackageName = "dev.unity2foxglove.ros2forunity.runtime.humble.win64";
    private const string unity2FoxgloveRuntimePackageAssetPath =
        "Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity";
    private static readonly string[] supportedVersionsOrdered = { "foxy", "galactic", "humble", "jazzy", "lyrical", "rolling" };
    private static readonly HashSet<string> supportedVersions = new HashSet<string>(supportedVersionsOrdered);
    private static readonly string supportedVersionsString = String.Join(", ", supportedVersionsOrdered);
    private static readonly Lazy<string> ros2ForUnityPath = new Lazy<string>(ComputeRos2ForUnityPath);
    private static readonly Lazy<string> pluginPath = new Lazy<string>(ComputePluginPath);
    // Kept as a field so the exact delegate instance can be unregistered during shutdown.
    private static ConsoleCancelEventHandler consoleCancelHandler;
    private const string expectedRmwImplementation = "rmw_fastrtps_cpp";
    private const string Ros2csSpinFallbackEnvVar = "ROS2CS_SPIN_FALLBACK";

    // Windows standalone ROS 2 libraries read getenv() through UCRT, so mirror managed env writes there.
    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int _wputenv_s(string name, string value);

#if UNITY_EDITOR
    // Unity editor domain reloads can construct this class repeatedly; avoid duplicate global handlers.
    private static bool editorHandlersRegistered = false;
#endif
    // Metadata files are local package files; disable external XML resolution defensively.
    private XmlDocument ros2csMetadata = new XmlDocument { XmlResolver = null };
    private XmlDocument ros2ForUnityMetadata = new XmlDocument { XmlResolver = null };
    private bool ownsReference = false;

    /// <summary>
    /// Runtime platform families supported by this Unity package.
    /// </summary>
    public enum Platform
    {
        Windows,
        Linux
    }

    /// <summary>
    /// Returns the supported platform family for the current Unity runtime.
    /// </summary>
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
    
    private static string GetEnvPathVariableName()
    {
      string envVariable = "LD_LIBRARY_PATH";
      if (GetOS() == Platform.Windows)
      {
          envVariable = "PATH";
      }
      return envVariable;
    }

    private static string GetEnvPathVariableValue()
    {
        return Environment.GetEnvironmentVariable(GetEnvPathVariableName());
    }

    private static void SetProcessEnvironmentVariable(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, value);
        if (GetOS() == Platform.Windows)
        {
            // ROS 2 Windows binaries use the dynamic UCRT, so update the CRT environment
            // as well as the managed process environment for native getenv callers.
            int result = _wputenv_s(name, value);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    "Failed to set Windows CRT environment variable '" + name + "' (ucrtbase _wputenv_s returned " + result + ")");
            }
        }
    }

    /// <summary>
    /// Returns the root Ros2ForUnity asset path for the current Editor or Player layout.
    /// </summary>
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
        return path;
    }

    /// <summary>
    /// Returns the platform-specific plugin path that contains native ROS 2 libraries.
    /// </summary>
    public static string GetPluginPath()
    {
        return pluginPath.Value;
    }

    private static string ComputePluginPath()
    {
        char separator = Path.DirectorySeparatorChar;
        string ros2ForUnityPath = GetRos2ForUnityPath();
        string path = ros2ForUnityPath;

        // Editor: Assets/Ros2ForUnity/Plugins/<OS>/x86_64
        // Windows Player: <App>_Data/Plugins/x86_64
        // Linux Player: <App>_Data/Plugins

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
    /// <remarks>
    /// Note that on Linux, LD_LIBRARY_PATH as used for dlopen() is determined on process start and this change won't
    /// affect it. Ros2 looks for rmw implementation based on this variable (independently) and the change
    /// is effective for this process, however rmw implementation's dependencies itself are loaded by dynamic linker
    /// anyway so setting it for Linux is pointless.
    /// </remarks>
    private static void SetEnvPathVariable()
    {
        string currentPath = GetEnvPathVariableValue();
        string pluginPath = GetPluginPath();

        char envPathSep = ':';
        if (GetOS() == Platform.Windows)
        {
            envPathSep = ';';
        }

        if (String.IsNullOrEmpty(currentPath))
        {
            SetProcessEnvironmentVariable(GetEnvPathVariableName(), pluginPath);
            pathConfigured = true;
            return;
        }

        StringComparison comparison = GetOS() == Platform.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (string entry in currentPath.Split(envPathSep))
        {
            if (String.Equals(entry.Trim(), pluginPath, comparison))
            {
                pathConfigured = true;
                return;
            }
        }

        SetProcessEnvironmentVariable(GetEnvPathVariableName(), pluginPath + envPathSep + currentPath);
        pathConfigured = true;
    }

    private static void SetStandalonePrefixPath()
    {
        string prefixPath = GetRos2ForUnityPath();
        string streamingAssetsPrefixPath = Path.Combine(Application.streamingAssetsPath, ros2ForUnityAssetFolderName);
        string pluginPrefixPath = GetPluginPath();
        // 1. StreamingAssets: preferred for standalone runtime share data copied beside the Player.
        if (Directory.Exists(Path.Combine(streamingAssetsPrefixPath, "share")))
        {
            prefixPath = streamingAssetsPrefixPath;
        }
        // 2. Plugins dir: compact standalone plugin bundle layout.
        else if (Directory.Exists(Path.Combine(pluginPrefixPath, "share")))
        {
            prefixPath = pluginPrefixPath;
        }
        // 3. Asset root: Editor or non-standalone fallback.
        else if (!Directory.Exists(Path.Combine(prefixPath, "share")))
        {
            Debug.LogWarning("Standalone AMENT_PREFIX_PATH fallback has no share directory: " + prefixPath);
        }
        // U2F-LOCAL-PATCH: standalone runtime must not inherit a sourced ROS2 workspace.
        SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);
    }

    private static void SetStandaloneRmwImplementation()
    {
        // U2F-LOCAL-PATCH: standalone runtime owns its RMW selection.
        SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_fastrtps_cpp");
    }

    private static void SetStandaloneRosDistro(string ros2Codename)
    {
        // U2F-LOCAL-PATCH: standalone runtime owns ROS_DISTRO even when Unity was launched from another ROS shell.
        SetProcessEnvironmentVariable("ROS_DISTRO", ros2Codename);
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

    private static void SetStandaloneRos2csSpinFallback(string ros2Codename)
    {
        if (!String.Equals(ros2Codename, "lyrical", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable(Ros2csSpinFallbackEnvVar)))
        {
            // "direct" tells ros2cs to avoid the wait-set executor path. Lyrical preview needs it
            // because repeated rcl_wait/context cycling has shown instability in standalone players.
            SetProcessEnvironmentVariable(Ros2csSpinFallbackEnvVar, "direct");
            Debug.Log("ROS2CS spin fallback enabled for Lyrical standalone runtime.");
        }
    }

    private static void WarnIfLyricalSpinFallbackUnset(string ros2Codename, bool standalone)
    {
        if (standalone || !String.Equals(ros2Codename, "lyrical", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable(Ros2csSpinFallbackEnvVar)))
        {
            Debug.LogWarning("Lyrical detected in non-standalone mode. Set " + Ros2csSpinFallbackEnvVar + "=direct before launching Unity.");
        }
    }

    private static void SetStandaloneRcutilsConsoleMode()
    {
        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("RCUTILS_COLORIZED_OUTPUT")))
        {
            // Disable ANSI color codes; Windows Player consoles can render them as raw escape sequences.
            SetProcessEnvironmentVariable("RCUTILS_COLORIZED_OUTPUT", "0");
        }
    }

    /// <summary>
    /// Returns whether the package metadata describes a standalone build with bundled ROS 2 libraries.
    /// </summary>
    public bool IsStandalone()
    {
        return ParseMetadataBool(GetMetadataValue(ros2csMetadata, "/ros2cs/standalone"), "/ros2cs/standalone");
    }

    /// <summary>
    /// Returns the effective ROS 2 distro, preferring a sourced environment over packaged metadata.
    /// </summary>
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
            string errMessage =
                "ROS2 versions in 'ros2cs' and 'ros2-for-unity' metadata files are not the same. " +
                "This is caused by mixing versions/builds.";
            FailIntegrity(errMessage);
        }

        if(!IsStandalone() && ros2SourcedCodename != ros2FromRos2csMetadata) {
            string errMessage =
                "ROS2 version in 'ros2cs' metadata doesn't match currently sourced version. " +
                "This is caused by mixing versions/builds.";
            FailIntegrity(errMessage);
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

    /// <summary>
    /// Returns the sourced ROS_DISTRO value, or null when Unity was launched without a sourced ROS 2 environment.
    /// </summary>
    public string GetROSVersionSourced()
    {
        return Environment.GetEnvironmentVariable("ROS_DISTRO");
    }

    /// <summary>
    /// Check if the ros version is supported, only applicable to non-standalone plugin versions
    /// (i. e. without ros2 libraries included in the plugin).
    /// </summary>
    private void CheckROSSupport(string ros2Codename)
    {
        if (string.IsNullOrEmpty(ros2Codename))
        {
            string errMessage = "No ROS environment sourced. You need to source your ROS2 " + supportedVersionsString
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

        if (!supportedVersions.Contains(ros2Codename))
        {
            string errMessage = "Currently sourced ROS version differs from supported one. Sourced: " + ros2Codename
              + ", supported: " + supportedVersionsString + ".";
            Debug.LogError(errMessage);
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
            throw new System.NotSupportedException(errMessage);
#else
            const int ROS_BAD_VERSION_CODE = 34;
            Application.Quit(ROS_BAD_VERSION_CODE);
            throw new System.NotSupportedException(errMessage);
#endif
        } else if (ros2Codename.Equals("foxy") || ros2Codename.Equals("galactic")) {
            Debug.LogWarning("You are using ROS2 " + ros2Codename + ", which has reached end of life.");
        } else if (ros2Codename.Equals("rolling") ) {
            Debug.LogWarning("You are using ROS2 rolling version. Bleeding edge version might not work correctly.");
        }
    }

    private static void ValidateRmwImplementation(string rmwImpl)
    {
        if (string.Equals(rmwImpl, expectedRmwImplementation, StringComparison.Ordinal))
        {
            return;
        }

        string errMessage =
            "ROS2 For Unity runtime was built for RMW implementation '" +
            expectedRmwImplementation + "' but initialized with '" + rmwImpl +
            "'. Ensure RMW_IMPLEMENTATION is unset or set to '" +
            expectedRmwImplementation + "'.";
        FailIntegrity(errMessage);
    }

    private void RegisterCtrlCHandler()
    {
#if ENABLE_MONO
        // Il2CPP build does not support Console.CancelKeyPress currently
        if (consoleCancelHandler == null)
        {
            consoleCancelHandler = (sender, eventArgs) => {
                eventArgs.Cancel = true;
                ShutdownShared();
            };
            Console.CancelKeyPress += consoleCancelHandler;
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

    private string GetMetadataValue(XmlDocument doc, string valuePath)
    {
        if (doc.DocumentElement == null)
        {
            throw new InvalidOperationException("Metadata document is empty while reading " + valuePath);
        }

        XmlNode node = doc.DocumentElement.SelectSingleNode(valuePath);
        if (node == null || node.InnerText == null)
        {
            throw new InvalidOperationException("Metadata value missing: " + valuePath);
        }

        return node.InnerText;
    }

    private bool ParseMetadataBool(string value, string valuePath)
    {
        string normalized = value.Trim();
        if (normalized == "1")
        {
            return true;
        }
        if (normalized == "0")
        {
            return false;
        }
        if (bool.TryParse(normalized, out bool parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException("Metadata value is not a boolean: " + valuePath + " = " + value);
    }

    private void LoadMetadata() 
    {
        char separator = Path.DirectorySeparatorChar;
        try
        {
            string ros2csMetadataPath = GetPluginPath() + separator + "metadata_ros2cs.xml";
            string ros2ForUnityMetadataPath = GetRos2ForUnityPath() + separator + "metadata_ros2_for_unity.xml";
            ros2csMetadata.Load(ros2csMetadataPath);
            ros2ForUnityMetadata.Load(ros2ForUnityMetadataPath);
        }
        catch (System.IO.FileNotFoundException e)
        {
#if UNITY_EDITOR
            var errMessage = "Could not find metadata files: " + e.Message;
            EditorApplication.isPlaying = false;
            throw new System.IO.FileNotFoundException(errMessage, e);
#else
            const int NO_METADATA = 1;
            Application.Quit(NO_METADATA);
            throw;
#endif
        }
        catch (XmlException e)
        {
            string errMessage = "Could not parse ros2-for-unity metadata: " + e.Message;
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            const int BAD_METADATA = 2;
            Application.Quit(BAD_METADATA);
#endif
            throw new InvalidOperationException(errMessage, e);
        }
    }

    /// <summary>
    /// Creates or shares the process-wide ROS2cs context and configures runtime paths.
    /// </summary>
    internal ROS2ForUnity()
    {
        lock (initMutex)
        {
            if (shutdownInProgress)
            {
                throw new InvalidOperationException("Ros2 For Unity is shutting down and cannot create a new context reference.");
            }

            if (isInitialized)
            {
                referenceCount++;
                ownsReference = true;
                return;
            }

            // Load metadata
            LoadMetadata();
            string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();
            if (IsStandalone())
            {
                string packagedRos2Version = GetMetadataValue(ros2csMetadata, "/ros2cs/ros2");
                SetStandaloneRosDistro(packagedRos2Version);
                SetStandalonePrefixPath();
                SetStandaloneRmwImplementation();
                SetStandaloneRcutilsConsoleMode();
            }
            string currentRos2Version = GetROSVersion();
            bool standaloneBuild = IsStandalone();
            string standalone = standaloneBuild ? "standalone" : "non-standalone";

            // Self checks
            CheckROSSupport(currentRos2Version);
            WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);
            CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);
            WarnIfLyricalSpinFallbackUnset(currentRos2Version, standaloneBuild);

            // Library loading
            if (standaloneBuild)
            {
                // For standalone, currentRos2Version comes from metadata, not ROS_DISTRO.
                // Keep ROS_DISTRO pinned so native getenv callers see the packaged distro.
                SetStandaloneRosDistro(currentRos2Version);
                SetStandaloneRos2csSpinFallback(currentRos2Version);
                SetStandalonePrefixPath();
                SetStandaloneRmwImplementation();
                SetStandaloneRcutilsConsoleMode();
            }
            if (GetOS() == Platform.Windows) {
                // Windows version can run standalone, modifies PATH to ensure all plugins visibility.
                if (!pathConfigured)
                {
                    // pathConfigured is intentionally not reset on shutdown; the process PATH
                    // persists across Play/Stop when Unity domain reload is disabled.
                    SetEnvPathVariable();
                }
            } else {
                // For foxy, it is necessary to use modified version of librcpputils to resolve custom msgs packages.
                ROS2.GlobalVariables.absolutePath = GetPluginPath() + "/";
                if (currentRos2Version == "foxy") {
                    ROS2.GlobalVariables.preloadLibrary = true;
                    ROS2.GlobalVariables.preloadLibraryName = "librcpputils.so";
                }
            }

            // Initialize
            ConnectLoggers();
            Ros2cs.Init();
            RegisterCtrlCHandler();

            string rmwImpl = Ros2cs.GetRMWImplementation();
            ValidateRmwImplementation(rmwImpl);

            LogRuntimeInfoWithoutStackTrace("ROS2 version: " + currentRos2Version + ". Build type: " + standalone + ". RMW: " + rmwImpl);

#if UNITY_EDITOR
            RegisterEditorHandlers();
#endif
            isInitialized = true;
            referenceCount = 1;
            ownsReference = true;
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
        lock (initMutex)
        {
            if (!isInitialized)
            {
                return false;
            }
            return Ros2cs.Ok();
        }
    }

    /// <summary>
    /// Releases this instance's reference to the shared ROS2cs context and shuts it down when last owner exits.
    /// </summary>
    internal void DestroyROS2ForUnity()
    {
        bool shouldShutdown = false;
        lock (initMutex)
        {
            if (!ownsReference)
            {
                return;
            }

            ownsReference = false;
            if (referenceCount > 0)
            {
                referenceCount--;
            }

            if (referenceCount == 0)
            {
                shouldShutdown = TryBeginShutdownLocked();
            }
        }

        if (shouldShutdown)
        {
            CompleteShutdownShared();
        }
    }

    /// <summary>
    /// Releases this instance's shared ROS2cs context reference.
    /// </summary>
    public void Dispose()
    {
        DestroyROS2ForUnity();
        GC.SuppressFinalize(this);
    }

    private static void ShutdownShared()
    {
        bool shouldShutdown = false;
        lock (initMutex)
        {
            shouldShutdown = TryBeginShutdownLocked();
        }

        if (shouldShutdown)
        {
            CompleteShutdownShared();
        }
    }

    private static bool TryBeginShutdownLocked()
    {
        if (shutdownInProgress)
        {
            return false;
        }

        shutdownInProgress = true;
        if (!isInitialized)
        {
            referenceCount = 0;
            shutdownInProgress = false;
            return false;
        }

        return true;
    }

    private static void CompleteShutdownShared()
    {
        // Executor joins must happen outside initMutex. Executor Tick() can call Ok(), which also
        // takes initMutex; joining here while holding it would create a fragile lock-order inversion.
        ROS2UnityComponent.StopAllExecutorsForRosShutdown();
        lock (initMutex)
        {
            if (!isInitialized)
            {
                referenceCount = 0;
                shutdownInProgress = false;
                return;
            }

            LogRuntimeInfoWithoutStackTrace("Shutting down Ros2 For Unity");
            try
            {
#if UNITY_EDITOR
                UnregisterEditorHandlers();
#endif
                Ros2cs.Shutdown();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                isInitialized = false;
                referenceCount = 0;
                shutdownInProgress = false;
                UnregisterCtrlCHandlerStatic();
            }
        }
    }

    private static void LogRuntimeInfoWithoutStackTrace(string message)
    {
        Debug.LogFormat(LogType.Log, LogOption.NoStacktrace, null, "{0}", message);
    }

    private static void UnregisterCtrlCHandlerStatic()
    {
#if ENABLE_MONO
        if (consoleCancelHandler != null)
        {
            Console.CancelKeyPress -= consoleCancelHandler;
            consoleCancelHandler = null;
        }
#endif
    }

#if UNITY_EDITOR
    private static void RegisterEditorHandlers()
    {
        if (editorHandlersRegistered)
        {
            return;
        }

        EditorApplication.playModeStateChanged += EditorPlayStateChanged;
        EditorApplication.quitting += ShutdownShared;
        editorHandlersRegistered = true;
    }

    private static void UnregisterEditorHandlers()
    {
        if (!editorHandlersRegistered)
        {
            return;
        }

        EditorApplication.playModeStateChanged -= EditorPlayStateChanged;
        EditorApplication.quitting -= ShutdownShared;
        editorHandlersRegistered = false;
    }

    private static void EditorPlayStateChanged(PlayModeStateChange change)
    {
        if (change == PlayModeStateChange.ExitingPlayMode)
        {
            ShutdownShared();
        }
    }
#endif
}

}  // namespace ROS2
